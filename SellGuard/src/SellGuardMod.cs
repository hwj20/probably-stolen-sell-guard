using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SellGuard;

public sealed class SellGuardMod : IMod
{
	public const string DefaultModId = "local.sell_guard";

	private const string HarmonyId = "mod.local.sell_guard";

	private const string KeyOptionId = "sellKey";

	private const string KeyPref = "SellGuard.sellKey";

	private const KeyCode DefaultSellKey = KeyCode.G;

	private static string _modId = DefaultModId;

	private static ModLog _log;

	private static Harmony _harmony;

	private static GameObject _tickerGo;

	private static KeyCode _sellKey = DefaultSellKey;

	private static readonly HashSet<string> SaidOnce = new HashSet<string>();

	private static readonly MethodInfo ResolveModeMethod = AccessTools.Method(typeof(NegociationUIManager), "ResolveMode", new Type[1] { typeof(StoreClient) });

	internal static KeyCode SellKey => _sellKey;

	public void Init(ModManifest manifest)
	{
		if (manifest != null && !string.IsNullOrEmpty(manifest.ID))
		{
			_modId = manifest.ID;
		}
		try
		{
			_log = new ModLog(manifest);
		}
		catch
		{
		}
		_sellKey = ReadKey();
		Info("SellGuard init，出售快捷键 = " + _sellKey);
	}

	public void OnEnable()
	{
		PatchAll();
		StartTicker();
		RegisterKeyBind();
		ModHook.OnCreateTooltipLate -= NoSale.OnTooltipLate;
		ModHook.OnCreateTooltipLate += NoSale.OnTooltipLate;
		if (ItemContextHandler.current != null)
		{
			ItemMenuPatch.EnsureInjected(ItemContextHandler.current);
		}
		Info("enabled");
	}

	public void OnDisable()
	{
		try
		{
			ModHook.OnCreateTooltipLate -= NoSale.OnTooltipLate;
		}
		catch
		{
		}
		try
		{
			ModHelper.RemoveGameOption(_modId, KeyOptionId);
		}
		catch
		{
		}
		ItemMenuPatch.Restore();
		try
		{
			_harmony?.UnpatchAll(HarmonyId);
		}
		catch
		{
		}
		_harmony = null;
		try
		{
			if (_tickerGo != null)
			{
				UnityEngine.Object.Destroy(_tickerGo);
			}
		}
		catch
		{
		}
		_tickerGo = null;
		Info("disabled");
	}

	// ---- Harmony ----

	private static void PatchAll()
	{
		if (_harmony != null)
		{
			return;
		}
		_harmony = new Harmony(HarmonyId);
		int ok = 0;
		int total = 0;
		Patch(ref ok, ref total, "StoreClient.IsClientRefusingItem", AccessTools.Method(typeof(StoreClient), "IsClientRefusingItem", new Type[2] { typeof(GameItem), typeof(bool) }), null, AccessTools.Method(typeof(NoSale), "RefusingPostfix"));
		Patch(ref ok, ref total, "NegociationUIManager.GetRefusalLabel", AccessTools.Method(typeof(NegociationUIManager), "GetRefusalLabel", new Type[2] { typeof(StoreClient), typeof(GameItem) }), null, AccessTools.Method(typeof(NoSale), "RefusalLabelPostfix"));
		Patch(ref ok, ref total, "NegociationUIManager.GetNotBuyingReason", AccessTools.Method(typeof(NegociationUIManager), "GetNotBuyingReason", new Type[1] { typeof(GameItem) }), null, AccessTools.Method(typeof(NoSale), "NotBuyingReasonPostfix"));
		Patch(ref ok, ref total, "PlayerStore.SellItem", AccessTools.Method(typeof(PlayerStore), "SellItem", new Type[1] { typeof(GameItem) }), AccessTools.Method(typeof(NoSale), "SellItemPrefix"), null);
		Patch(ref ok, ref total, "ItemContextHandler.OnEventInit", AccessTools.Method(typeof(ItemContextHandler), "OnEventInit"), null, AccessTools.Method(typeof(ItemMenuPatch), "OnEventInitPostfix"));
		Patch(ref ok, ref total, "ItemContextHandler.OnEventPress", AccessTools.Method(typeof(ItemContextHandler), "OnEventPress"), AccessTools.Method(typeof(ItemMenuPatch), "OnEventPressPrefix"), null);
		Patch(ref ok, ref total, "ItemContextHandler.UpdateConditions", AccessTools.Method(typeof(ItemContextHandler), "UpdateConditions"), null, AccessTools.Method(typeof(ItemMenuPatch), "UpdateConditionsPostfix"));
		Patch(ref ok, ref total, "ItemContextHandler.ResetButtonColor", AccessTools.Method(typeof(ItemContextHandler), "ResetButtonColor"), null, AccessTools.Method(typeof(ItemMenuPatch), "ResetButtonColorPostfix"));
		Patch(ref ok, ref total, "ItemContextHandler.UpdateSelectButtonColor", AccessTools.Method(typeof(ItemContextHandler), "UpdateSelectButtonColor"), null, AccessTools.Method(typeof(ItemMenuPatch), "UpdateSelectButtonColorPostfix"));
		Patch(ref ok, ref total, "ItemContextHandler.EqualizeButtonWidths", AccessTools.Method(typeof(ItemContextHandler), "EqualizeButtonWidths"), null, AccessTools.Method(typeof(ItemMenuPatch), "EqualizeButtonWidthsPostfix"));
		Patch(ref ok, ref total, "ItemContextHandler.TriggerButton", AccessTools.Method(typeof(ItemContextHandler), "TriggerButton"), AccessTools.Method(typeof(ItemMenuPatch), "TriggerButtonPrefix"), null);
		Info($"Harmony 补丁 {ok}/{total} 个成功");
	}

	private static void Patch(ref int ok, ref int total, string label, MethodInfo target, MethodInfo prefix, MethodInfo postfix)
	{
		total++;
		if (target == null || (prefix == null && postfix == null))
		{
			Warn("找不到补丁目标或补丁体（" + label + "），该功能跳过");
			return;
		}
		try
		{
			HarmonyMethod pre = ((prefix != null) ? new HarmonyMethod(prefix) : null);
			HarmonyMethod post = ((postfix != null) ? new HarmonyMethod(postfix) : null);
			_harmony.Patch(target, pre, post);
			ok++;
		}
		catch (Exception ex)
		{
			Warn("补丁 " + label + " 失败: " + ex.GetType().Name + ": " + ex.Message);
		}
	}

	// ---- 出售快捷键 ----

	private static KeyCode ReadKey()
	{
		try
		{
			if (PlayerPrefs.HasKey(KeyPref))
			{
				return (KeyCode)PlayerPrefs.GetInt(KeyPref);
			}
		}
		catch
		{
		}
		return DefaultSellKey;
	}

	private static void RegisterKeyBind()
	{
		try
		{
			ModHelper.RemoveGameOption(_modId, KeyOptionId);
		}
		catch
		{
		}
		try
		{
			ModHelper.AddGameOptionKeyBind(_modId, "keybind", KeyOptionId, () => Lang.KeyLabel, () => _sellKey, delegate(KeyCode k)
			{
				_sellKey = k;
				try
				{
					PlayerPrefs.SetInt(KeyPref, (int)k);
					PlayerPrefs.Save();
				}
				catch
				{
				}
				Info("出售快捷键改为 " + k);
			}, () => Lang.KeyTip);
		}
		catch (Exception ex)
		{
			Warn("注册出售快捷键失败（仍可用默认键 " + _sellKey + "）: " + ex.Message);
		}
	}

	private static void StartTicker()
	{
		if (_tickerGo != null)
		{
			return;
		}
		try
		{
			_tickerGo = new GameObject("SellGuard.Ticker");
			_tickerGo.AddComponent<Ticker>();
		}
		catch (Exception ex)
		{
			Warn("创建按键监听失败，出售快捷键不可用: " + ex.Message);
		}
	}

	internal static void Tick()
	{
		try
		{
			if (_sellKey == KeyCode.None || KeyBindHelper.SuppressHotkeys || !Input.GetKeyDown(_sellKey))
			{
				return;
			}
			if (StoreUIManager.IsAnyTyping())
			{
				return;
			}
			TrySell();
		}
		catch (Exception ex)
		{
			WarnOnce("出售快捷键处理异常: " + ex.GetType().Name + ": " + ex.Message);
		}
	}

	private static void TrySell()
	{
		NegociationUIManager nego = NegociationUIManager.Instance;
		PlayerStore store = PlayerStore.Instance;
		if (nego == null || store == null || store.currentClientInstance == null || NegociationUIManager.IsTradeLocked())
		{
			return;
		}
		StoreClient client = store.currentClientInstance.GetClientBlueprint();
		if (client == null || client.isBusinessComplete || !IsCustomerBuying(nego, client))
		{
			return;
		}
		List<GameItem> sellable = nego.GetSellableItem();
		if (sellable == null || sellable.Count == 0)
		{
			Notify(Lang.NothingToSell);
			return;
		}
		// 客户预算不够时，原版会在平板上弹「预算不足，仍要卖吗」确认框；平板没打开就看不到，所以先打开。
		if (!nego.IsOpen && !nego.IsClientHaveEnoughToBuy())
		{
			nego.OpenUI();
		}
		nego.OnSellAllClick();
	}

	// 与谈判平板判断一致：平板处于「客户购买（玩家出售）」模式时才响应。
	private static bool IsCustomerBuying(NegociationUIManager nego, StoreClient client)
	{
		if (ResolveModeMethod != null)
		{
			try
			{
				object mode = ResolveModeMethod.Invoke(nego, new object[1] { client });
				return mode != null && mode.ToString() == "Buying";
			}
			catch
			{
			}
		}
		return client.clientIntent == StoreClient.ClientIntent.BUY;
	}

	// ---- 日志 / 提示 ----

	internal static void Notify(string message)
	{
		try
		{
			StoreUIManager.Instance?.Notify(message);
		}
		catch
		{
		}
	}

	internal static void Info(string message)
	{
		try
		{
			_log?.Log(message);
		}
		catch
		{
		}
	}

	internal static void Warn(string message)
	{
		try
		{
			_log?.Warning(message);
		}
		catch
		{
		}
	}

	internal static void WarnOnce(string message)
	{
		lock (SaidOnce)
		{
			if (!SaidOnce.Add(message))
			{
				return;
			}
		}
		Warn(message);
	}
}

internal sealed class Ticker : MonoBehaviour
{
	private void Awake()
	{
		try
		{
			UnityEngine.Object.DontDestroyOnLoad(base.gameObject);
		}
		catch
		{
		}
	}

	private void Update()
	{
		SellGuardMod.Tick();
	}
}
