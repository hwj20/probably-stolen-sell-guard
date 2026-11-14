using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Steamworks;

static class Program
{
    const uint AppId = 4348910;
    static string _vdfPath;
    static string _vdf;

    // 键不存在时返回 null
    static string TryGet(string key)
    {
        var m = Regex.Match(_vdf, "\"" + key + "\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"");
        if (!m.Success) return null;
        return Regex.Replace(m.Groups[1].Value, @"\\(.)", mm => mm.Groups[1].Value == "n" ? "\n" : mm.Groups[1].Value == "t" ? "\t" : mm.Groups[1].Value);
    }

    static string Get(string key) => TryGet(key) ?? throw new Exception("vdf missing key: " + key);

    // "tags" 是可选键，逗号分隔。没有这个键返回 null（不改标签）；值为空串返回空列表（清空标签）。
    static List<string> GetTags()
    {
        var raw = TryGet("tags");
        if (raw == null) return null;
        return raw.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
        // --tags-only：只更新标签，不上传文件、不改标题/简介/预览图/可见性，也不写更新说明
        bool tagsOnly = false; string vdfArg = null;
        foreach (var a in args)
        {
            if (a == "--tags-only") tagsOnly = true;
            else if (a.StartsWith("--")) { Console.WriteLine("unknown option: " + a); return 1; }
            else if (vdfArg == null) vdfArg = a;
            else { Console.WriteLine("unexpected argument: " + a); return 1; }
        }
        if (vdfArg == null) { Console.WriteLine("usage: WorkshopUploader <workshop_item.vdf> [--tags-only]"); return 1; }

        _vdfPath = Path.GetFullPath(vdfArg);
        _vdf = File.ReadAllText(_vdfPath, Encoding.UTF8);
        ulong id = ulong.Parse(Get("publishedfileid"));
        List<string> tags = GetTags();
        string content = null, preview = null, title = null, desc = null, note = null;
        int vis = 0;
        if (tagsOnly)
        {
            if (id == 0) { Console.WriteLine("--tags-only needs an existing publishedfileid in the vdf"); return 2; }
            if (tags == null) { Console.WriteLine("--tags-only needs a \"tags\" key in the vdf"); return 2; }
        }
        else
        {
            content = Get("contentfolder"); preview = Get("previewfile"); title = Get("title"); desc = Get("description"); note = Get("changenote");
            vis = int.Parse(Get("visibility"));
            if (!Directory.Exists(content) || !File.Exists(Path.Combine(content, "manifest.xml"))) { Console.WriteLine("content folder or manifest.xml missing: " + content); return 2; }
            if (!File.Exists(preview)) { Console.WriteLine("preview missing: " + preview); return 2; }
        }

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
            else Console.WriteLine("Updating existing item id " + id + (tagsOnly ? " (tags only)" : ""));

            var h = SteamUGC.StartItemUpdate(new AppId_t(AppId), new PublishedFileId_t(id));
            bool ok = true;
            if (!tagsOnly)
                ok = SteamUGC.SetItemTitle(h, title)
                    & SteamUGC.SetItemDescription(h, desc)
                    & SteamUGC.SetItemContent(h, content)
                    & SteamUGC.SetItemPreview(h, preview)
                    & SteamUGC.SetItemVisibility(h, (ERemoteStoragePublishedFileVisibility)vis);
            if (tags != null)
            {
                Console.WriteLine("Tags: " + (tags.Count == 0 ? "(clear all)" : string.Join(", ", tags)));
                ok &= SteamUGC.SetItemTags(h, tags);
            }
            Console.WriteLine("Fields set: " + ok);
            bool done = false; SubmitItemUpdateResult_t res = default; bool ioFail = false;
            var cr = CallResult<SubmitItemUpdateResult_t>.Create((r, f) => { res = r; ioFail = f; done = true; });
            cr.Set(SteamUGC.SubmitItemUpdate(h, tagsOnly ? null : note));
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
