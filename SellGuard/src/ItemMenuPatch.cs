using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SellGuard;

// 在物品右键菜单（ItemContextHandler 的 contextWindow）末尾加一行「标记为不可售 / 取消不可售标记」。
// 原版菜单是固定 8 行的 GridPixelElement；这里换成 9 行的新网格，原有按钮原样搬过去，
// 并把私有字段 contextButtons 指向新网格，原版的 Validate 调用照常生效。
internal static class ItemMenuPatch
{
	private const string NodeTag = "ContextMenuNode";

	private static readonly FieldInfo WindowField = AccessTools.Field(typeof(ItemContextHandler), "contextWindow");

	private static readonly FieldInfo GridField = AccessTools.Field(typeof(ItemContextHandler), "contextButtons");

	private static readonly FieldInfo ItemField = AccessTools.Field(typeof(ItemContextHandler), "currentItem");

	private static readonly MethodInfo ResetMethod = AccessTools.Method(typeof(ItemContextHandler), "OnEventReset");

	private static readonly string[] NativeButtonNames = new string[8] { "buttonOpen", "buttonUse", "buttonEquip", "buttonActivate", "buttonConfigure", "buttonToggle", "buttonUnload", "buttonPin" };

	private static readonly List<FieldInfo> NativeButtonFields = new List<FieldInfo>();

	private static ItemContextHandler _owner;

	private static PixelWindow _window;

	private static GridPixelElement _original;

	private static GridPixelElement _grid;

	private static RichTextElement _button;

	private static (PixelElement element, int x, int y)[] _children = Array.Empty<(PixelElement, int, int)>();

	private static bool _canToggle;

	internal static bool Ready => WindowField != null && GridField != null && ItemField != null;

	static ItemMenuPatch()
	{
		foreach (string name in NativeButtonNames)
		{
			FieldInfo fieldInfo = AccessTools.Field(typeof(ItemContextHandler), name);
			if (fieldInfo != null)
			{
				NativeButtonFields.Add(fieldInfo);
			}
		}
	}

	private static GameItem CurrentItem(ItemContextHandler owner)
	{
		try
		{
			return ItemField?.GetValue(owner) as GameItem;
		}
		catch
		{
			return null;
		}
	}

	internal static void EnsureInjected(ItemContextHandler owner)
	{
		if (owner == null || !Ready)
		{
			return;
		}
		try
		{
			PixelWindow window = WindowField.GetValue(owner) as PixelWindow;
			GridPixelElement grid = GridField.GetValue(owner) as GridPixelElement;
			if (window == null || grid == null)
			{
				return;
			}
			if (owner == _owner && grid == _grid && _button != null)
			{
				return;
			}
			if (grid.gridWidth != 1 || window.childElement != grid)
			{
				SellGuardMod.WarnOnce("右键菜单结构与预期不同，未添加「不可售」选项。");
				return;
			}
			List<(PixelElement, int, int)> children = new List<(PixelElement, int, int)>();
			for (int y = 0; y < grid.gridHeight; y++)
			{
				PixelElement element = grid.GetElement(0, y);
				if (element != null)
				{
					children.Add((element, 0, y));
				}
			}
			GridPixelElement newGrid = new GridPixelElement(1, grid.gridHeight + 1);
			newGrid.justifyWidth = grid.justifyWidth;
			newGrid.searchUsingPriority = grid.searchUsingPriority;
			RichTextElement button = new RichTextElement();
			if (!window.Detach(grid))
			{
				SellGuardMod.WarnOnce("右键菜单网格无法拆下，未添加「不可售」选项。");
				return;
			}
			grid.DetachAll();
			foreach (var (element2, x, y2) in children)
			{
				newGrid.AttachPos(element2, x, y2);
			}
			newGrid.AttachPos(button, 0, grid.gridHeight);
			window.Attach(newGrid);
			button.handler.gameObject.name = "nodeButtonSellGuard";
			foreach (Transform transform in window.handler.GetComponentsInChildren<Transform>(includeInactive: true))
			{
				transform.gameObject.tag = NodeTag;
			}
			GridField.SetValue(owner, newGrid);
			_owner = owner;
			_window = window;
			_original = grid;
			_grid = newGrid;
			_button = button;
			_children = children.ToArray();
			SetLabel(owner, null);
			newGrid.Validate();
			window.Validate();
			SellGuardMod.Info("右键菜单已加入「不可售」选项。");
		}
		catch (Exception ex)
		{
			SellGuardMod.WarnOnce("添加右键菜单选项失败: " + ex.GetType().Name + ": " + ex.Message);
		}
	}

	internal static void Restore()
	{
		try
		{
			if (_window != null && _grid != null && _original != null && _window.childElement == _grid)
			{
				_window.Detach(_grid);
				_grid.DetachAll();
				(PixelElement, int, int)[] children = _children;
				foreach (var (element, x, y) in children)
				{
					_original.AttachPos(element, x, y);
				}
				_window.Attach(_original);
				if (_owner != null)
				{
					GridField?.SetValue(_owner, _original);
				}
				_original.Validate();
				_window.Validate();
			}
		}
		catch (Exception ex)
		{
			SellGuardMod.Warn("还原右键菜单失败: " + ex.Message);
		}
		_owner = null;
		_window = null;
		_original = null;
		_grid = null;
		_button = null;
		_children = Array.Empty<(PixelElement, int, int)>();
	}

	private static void SetLabel(ItemContextHandler owner, RichTextElement selected)
	{
		if (_button == null || owner != _owner)
		{
			return;
		}
		GameItem item = CurrentItem(owner);
		bool enabled = item != null && _canToggle;
		bool isSelected = selected != null && selected == _button;
		RenderHandler.ColorPalette color = (enabled ? (isSelected ? RenderHandler.ColorPalette.VeryLightGray : RenderHandler.ColorPalette.LightGray) : (isSelected ? RenderHandler.ColorPalette.DarkRed : RenderHandler.ColorPalette.DarkGray));
		_button.SetText(NoSale.IsMarked(item) ? Lang.MenuUnmark : Lang.MenuMark, 10000, color);
	}

	// ---- Harmony 补丁体 ----

	internal static void OnEventInitPostfix(ItemContextHandler __instance, bool __result)
	{
		if (__result)
		{
			EnsureInjected(__instance);
		}
	}

	internal static void OnEventPressPrefix(ItemContextHandler __instance)
	{
		EnsureInjected(__instance);
	}

	internal static void UpdateConditionsPostfix(ItemContextHandler __instance)
	{
		if (__instance == _owner)
		{
			_canToggle = NoSale.CanToggle(CurrentItem(__instance));
		}
	}

	internal static void ResetButtonColorPostfix(ItemContextHandler __instance)
	{
		SetLabel(__instance, null);
	}

	internal static void UpdateSelectButtonColorPostfix(ItemContextHandler __instance, RichTextElement selectedButton)
	{
		if (CurrentItem(__instance) != null)
		{
			SetLabel(__instance, selectedButton);
		}
	}

	internal static void EqualizeButtonWidthsPostfix(ItemContextHandler __instance)
	{
		if (_button == null || __instance != _owner)
		{
			return;
		}
		try
		{
			_button.SetFixedSize(-1, -1);
			int width = _button.widthPixels;
			List<RichTextElement> natives = new List<RichTextElement>();
			foreach (FieldInfo field in NativeButtonFields)
			{
				if (field.GetValue(__instance) is RichTextElement richTextElement)
				{
					natives.Add(richTextElement);
					width = Math.Max(width, richTextElement.widthPixels);
				}
			}
			foreach (RichTextElement item in natives)
			{
				item.SetFixedSize(width, -1);
			}
			_button.SetFixedSize(width, -1);
		}
		catch
		{
		}
	}

	internal static bool TriggerButtonPrefix(ItemContextHandler __instance, RichTextElement button)
	{
		if (_button == null || button != _button || __instance != _owner)
		{
			return true;
		}
		GameItem item = CurrentItem(__instance);
		if (item == null || !_canToggle)
		{
			return false;
		}
		try
		{
			ResetMethod?.Invoke(__instance, null);
		}
		catch
		{
		}
		NoSale.Toggle(item);
		return false;
	}
}
