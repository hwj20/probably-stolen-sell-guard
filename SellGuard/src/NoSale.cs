using System;

namespace SellGuard;

// 「不可售」标记：一个保存在物品 state 里的自定义 tag，随存档保存。
// 不复用原版 NOT_FOR_RESALE：那是游戏自带的类型标签（会显示为物品类型，且部分任务物品原生就带），
// 用独立 tag 可以避免取消标记时误删原生属性。
internal static class NoSale
{
	internal const string Tag = "SELL_GUARD_NOT_FOR_SALE_TAG";

	internal static bool IsMarked(GameItem item)
	{
		try
		{
			return item != null && item.IsTag(Tag);
		}
		catch
		{
			return false;
		}
	}

	// 物品本身或其内容物（容器里装的东西）带标记。
	internal static bool ContainsMarked(GameItem item)
	{
		if (item == null)
		{
			return false;
		}
		if (IsMarked(item))
		{
			return true;
		}
		try
		{
			if (item.contentWindow == null)
			{
				return false;
			}
			foreach (GameItem child in GraphUtils.FindAllChildrenType<GameItem>(item))
			{
				if (IsMarked(child))
				{
					return true;
				}
			}
		}
		catch
		{
		}
		return false;
	}

	// 只允许给自己拥有的物品打标记；已标记的物品随时可以取消。
	internal static bool CanToggle(GameItem item)
	{
		if (item == null)
		{
			return false;
		}
		if (IsMarked(item))
		{
			return true;
		}
		try
		{
			return GeneralHelper.IsItemOwned(item);
		}
		catch
		{
			return false;
		}
	}

	internal static void Toggle(GameItem item)
	{
		if (!CanToggle(item))
		{
			return;
		}
		bool wasMarked = IsMarked(item);
		try
		{
			if (wasMarked)
			{
				item.DisableTag(Tag);
			}
			else
			{
				item.EnableTag(Tag);
			}
		}
		catch (Exception ex)
		{
			SellGuardMod.Warn("切换不可售标记失败: " + ex.Message);
			return;
		}
		string name = SafeName(item);
		SellGuardMod.Notify(wasMarked ? Lang.Unmarked(name) : Lang.Marked(name));
		try
		{
			NegociationUIManager.Instance?.RefreshUI();
		}
		catch
		{
		}
	}

	internal static string SafeName(GameItem item)
	{
		try
		{
			string text = item.GetDisplayName(preferCustomName: true);
			if (!string.IsNullOrEmpty(text))
			{
				return text;
			}
		}
		catch
		{
		}
		try
		{
			return string.IsNullOrEmpty(item.name) ? item.identifier : item.name;
		}
		catch
		{
			return "?";
		}
	}

	// ---- Harmony 补丁体 ----

	// StoreClient.IsClientRefusingItem(GameItem gameItem, bool ignoreContents)
	// 客户拒收带标记的物品；原版会对容器内容物递归调用，所以装了标记物品的容器也会被拒收。
	internal static void RefusingPostfix(GameItem gameItem, ref bool __result)
	{
		if (!__result && IsMarked(gameItem))
		{
			__result = true;
		}
	}

	// NegociationUIManager.GetRefusalLabel(StoreClient storeClient, GameItem gameItem)
	internal static void RefusalLabelPostfix(GameItem gameItem, ref string __result)
	{
		if (ContainsMarked(gameItem))
		{
			__result = Lang.RowLabel;
		}
	}

	// NegociationUIManager.GetNotBuyingReason(GameItem gameItem)
	internal static void NotBuyingReasonPostfix(GameItem gameItem, ref string __result)
	{
		if (!string.IsNullOrEmpty(__result) && ContainsMarked(gameItem))
		{
			__result = Lang.RowLabel;
		}
	}

	// PlayerStore.SellItem(GameItem item)：最后一道保险。正常流程在上面的拒收检查就会挡住，走不到这里。
	internal static bool SellItemPrefix(GameItem item)
	{
		if (!ContainsMarked(item))
		{
			return true;
		}
		string name = SafeName(item);
		SellGuardMod.Warn("拦截了一次不可售物品的出售: " + name);
		SellGuardMod.Notify(Lang.Blocked(name));
		return false;
	}

	internal static void OnTooltipLate(RichTextBuilder builder, GameItem item)
	{
		if (builder == null || item == null)
		{
			return;
		}
		try
		{
			if (IsMarked(item))
			{
				builder.AddLine(Lang.TooltipMarked, ignoreEmpty: true, RenderHandler.ColorPalette.LightRed);
			}
			else if (ContainsMarked(item))
			{
				builder.AddLine(Lang.TooltipContains, ignoreEmpty: true, RenderHandler.ColorPalette.LightOrange);
			}
		}
		catch
		{
		}
	}
}
