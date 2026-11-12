using System;

namespace SellGuard;

internal static class Lang
{
	internal static bool Chinese
	{
		get
		{
			try
			{
				string code = LocHelper.GetCurrentLocaleCode();
				return string.IsNullOrEmpty(code) || code.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
			}
			catch
			{
				return true;
			}
		}
	}

	internal static string T(string zh, string en)
	{
		return Chinese ? zh : en;
	}

	internal static string MenuMark => T("标记为不可售", "Mark Not for Sale");

	internal static string MenuUnmark => T("取消不可售标记", "Unmark Not for Sale");

	internal static string RowLabel => T("不可售", "Not for sale");

	internal static string TooltipMarked => T("【不可售】客户不会购买此物品", "[Not for sale] Customers will not buy this");

	internal static string TooltipContains => T("【内含不可售物品】整件不会被出售", "[Contains not-for-sale items] Will not be sold");

	internal static string Marked(string name) => T("已标记为不可售：" + name, "Marked not for sale: " + name);

	internal static string Unmarked(string name) => T("已取消不可售标记：" + name, "No longer marked: " + name);

	internal static string Blocked(string name) => T("不可售物品，已阻止出售：" + name, "Blocked sale of a not-for-sale item: " + name);

	internal static string NothingToSell => T("台面上没有这位客户要买的物品", "Nothing on the counter that this customer will buy");

	internal static string KeyLabel => T("出售（确认交易）", "Sell (confirm deal)");

	internal static string KeyTip => T("客户来买东西时，按下等同于点击谈判平板上的「出售」按钮；标记为不可售的物品不会被卖出。默认 G。", "When a customer is buying, pressing this is the same as clicking Sell on the negotiation tablet. Items marked not for sale are never sold. Default G.");
}
