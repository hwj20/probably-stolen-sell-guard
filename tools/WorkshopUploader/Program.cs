using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Steamworks;

static class Program
{
    const uint AppId = 4348910;
    static string _vdfPath;
    static string _vdf;

    static string Get(string key)
    {
        var m = Regex.Match(_vdf, "\"" + key + "\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"");
        if (!m.Success) throw new Exception("vdf missing key: " + key);
        return Regex.Replace(m.Groups[1].Value, @"\\(.)", mm => mm.Groups[1].Value == "n" ? "\n" : mm.Groups[1].Value == "t" ? "\t" : mm.Groups[1].Value);
    }

    static void SaveId(ulong id)
    {
        _vdf = Regex.Replace(_vdf, "(\"publishedfileid\"\\s+\")\\d+(\")", "${1}" + id + "${2}");
        File.WriteAllText(_vdfPath, _vdf, new UTF8Encoding(false));
    }

    static T Wait<T>(SteamAPICall_t call, string what, int timeoutSec)
    {
        bool done = false; T result = default; bool ioFail = false;
        var cr = CallResult<T>.Create((r, fail) => { result = r; ioFail = fail; done = true; });
        cr.Set(call);
        var start = DateTime.UtcNow;
        while (!done)
        {
            SteamAPI.RunCallbacks();
            Thread.Sleep(100);
            if ((DateTime.UtcNow - start).TotalSeconds > timeoutSec) throw new TimeoutException(what + " timed out");
        }
        if (ioFail) throw new Exception(what + " IO failure");
        return result;
    }

    static int Main(string[] args)
    {
        _vdfPath = Path.GetFullPath(args[0]);
        _vdf = File.ReadAllText(_vdfPath, Encoding.UTF8);
        ulong id = ulong.Parse(Get("publishedfileid"));
        string content = Get("contentfolder"), preview = Get("previewfile"), title = Get("title"), desc = Get("description"), note = Get("changenote");
        int vis = int.Parse(Get("visibility"));
        if (!Directory.Exists(content) || !File.Exists(Path.Combine(content, "manifest.xml"))) { Console.WriteLine("content folder or manifest.xml missing: " + content); return 2; }
        if (!File.Exists(preview)) { Console.WriteLine("preview missing: " + preview); return 2; }

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "steam_appid.txt"), AppId.ToString());
        Environment.SetEnvironmentVariable("SteamAppId", AppId.ToString());
        Environment.SetEnvironmentVariable("SteamGameId", AppId.ToString());
        var init = SteamAPI.InitEx(out string err);
        if (init != ESteamAPIInitResult.k_ESteamAPIInitResult_OK) { Console.WriteLine("Steam init failed: " + init + " " + err); return 3; }
        try
        {
            Console.WriteLine("Steam user: logged in = " + SteamUser.BLoggedOn());
            bool legal = false;
            if (id == 0)
            {
                var created = Wait<CreateItemResult_t>(SteamUGC.CreateItem(new AppId_t(AppId), EWorkshopFileType.k_EWorkshopFileTypeCommunity), "CreateItem", 120);
                Console.WriteLine("CreateItem: " + created.m_eResult);
                if (created.m_eResult != EResult.k_EResultOK) return 4;
                id = created.m_nPublishedFileId.m_PublishedFileId;
                legal |= created.m_bUserNeedsToAcceptWorkshopLegalAgreement;
                SaveId(id);
                Console.WriteLine("Created item id " + id + " (saved into vdf)");
            }
            else Console.WriteLine("Updating existing item id " + id);

            var h = SteamUGC.StartItemUpdate(new AppId_t(AppId), new PublishedFileId_t(id));
            bool ok = SteamUGC.SetItemTitle(h, title)
                & SteamUGC.SetItemDescription(h, desc)
                & SteamUGC.SetItemContent(h, content)
                & SteamUGC.SetItemPreview(h, preview)
                & SteamUGC.SetItemVisibility(h, (ERemoteStoragePublishedFileVisibility)vis);
            Console.WriteLine("Fields set: " + ok);
            bool done = false; SubmitItemUpdateResult_t res = default; bool ioFail = false;
            var cr = CallResult<SubmitItemUpdateResult_t>.Create((r, f) => { res = r; ioFail = f; done = true; });
            cr.Set(SteamUGC.SubmitItemUpdate(h, note));
            var start = DateTime.UtcNow; EItemUpdateStatus last = EItemUpdateStatus.k_EItemUpdateStatusInvalid;
            while (!done)
            {
                SteamAPI.RunCallbacks();
                var st = SteamUGC.GetItemUpdateProgress(h, out ulong p, out ulong t);
                if (st != last) { Console.WriteLine("  " + st + " " + p + "/" + t); last = st; }
                Thread.Sleep(150);
                if ((DateTime.UtcNow - start).TotalMinutes > 10) { Console.WriteLine("Submit timed out"); return 5; }
            }
            Console.WriteLine("SubmitItemUpdate: " + res.m_eResult + (ioFail ? " (IO failure)" : ""));
            legal |= res.m_bUserNeedsToAcceptWorkshopLegalAgreement;
            Console.WriteLine("NeedsLegalAgreement: " + legal);
            Console.WriteLine("URL: https://steamcommunity.com/sharedfiles/filedetails/?id=" + id);
            return res.m_eResult == EResult.k_EResultOK ? 0 : 6;
        }
        finally { SteamAPI.Shutdown(); }
    }
}
