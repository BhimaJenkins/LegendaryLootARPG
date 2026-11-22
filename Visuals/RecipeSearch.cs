using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryLoot
{
	/// <summary>
	/// A search box at the top of the crafting list: type part of an item's name and only matching recipes show.
	///  - Mouse and keyboard: click the box and type; Esc or Enter leaves it.
	///  - Controller: press the right stick (R3) to focus it. In Steam Big Picture (always on SteamOS, and on Windows
	///    when launched through Big Picture) Steam's on-screen keyboard opens, because the box is a copy of the game's
	///    own text field (the map's pin-name box), which asks Steam for it.
	/// While the box has focus the game treats it like the chat box, so typed letters don't close the inventory or
	/// swap gear. The search clears when the inventory closes.
	/// </summary>
	public static class RecipeSearch
	{
		private const string Name = "LLA_RecipeSearch";
		private const float Height = 30f;
		private static TMP_InputField _field;
		private static TMP_Text _hint;
		private static string _query = "";

		public static bool Focused => _field != null && (_field.isFocused || OnScreenKeyboard.Blocking);

		/// <summary>True when Steam's own on-screen keyboard can open (Steam Big Picture / SteamOS).</summary>
		private static bool SteamKeyboardAvailable()
		{
			try
			{
				var m = AccessTools.Method(_field.GetType(), "VirtualKeyboardAvailable");
				return m != null && (bool)m.Invoke(_field, null);
			}
			catch { return false; }
		}

		[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateCraftingPanel))]
		private static class Patch_Create
		{
			private static void Prefix(InventoryGui __instance)
			{
				if (_field == null && Plugin.RecipeSearchEnabled.Value) Create(__instance);
			}
		}

		/// <summary>Keeps only recipes whose item name contains the search text.</summary>
		[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipeList))]
		private static class Patch_Filter
		{
			private static void Prefix(List<Recipe> recipes)
			{
				if (recipes == null || string.IsNullOrWhiteSpace(_query) || !Plugin.RecipeSearchEnabled.Value) return;
				string q = _query.Trim();
				recipes.RemoveAll(r =>
				{
					string name = r?.m_item != null ? Localization.instance.Localize(r.m_item.m_itemData.m_shared.m_name) : "";
					return name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0;
				});
			}
		}

		/// <summary>R3 focuses the box; the hint shows the button while a controller is in use.</summary>
		[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
		private static class Patch_Input
		{
			private static void Postfix(InventoryGui __instance)
			{
				if (_field == null || !InventoryGui.IsVisible()) return;
				bool shown = _field.gameObject.activeInHierarchy;
				bool pad = ZInput.IsGamepadActive();
				if (_hint != null)
				{
					bool showHint = pad && !Focused;
					if (_hint.gameObject.activeSelf != showHint) _hint.gameObject.SetActive(showHint);
					if (showHint) _hint.text = Localization.instance.GetBoundKeyString("JoyRStick", true);
				}
				if (shown && pad && !Focused && ZInput.GetButtonDown("JoyRStick"))
				{
					// Steam's keyboard where Steam offers it (unless set to always use the mod's); otherwise the mod's own.
					if (!Plugin.AlwaysModKeyboard.Value && SteamKeyboardAvailable())
					{
						_field.Select();
						_field.ActivateInputField();
					}
					else OnScreenKeyboard.Open(_field);
				}
			}
		}

		/// <summary>While the box has focus, the game treats it like the chat box: keys type instead of acting.</summary>
		[HarmonyPatch(typeof(Chat), nameof(Chat.HasFocus))]
		private static class Patch_Typing
		{
			private static void Postfix(ref bool __result)
			{
				if (Focused) __result = true;
			}
		}

		[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
		private static class Patch_Clear
		{
			private static void Postfix()
			{
				if (_field == null) return;
				if (_field.isFocused) _field.DeactivateInputField();
				if (_field.text.Length > 0) _field.text = "";
				_query = "";
			}
		}

		private static void Create(InventoryGui gui)
		{
			try
			{
				// The scroll view holding the recipe list: make room at its top for the box.
				RectTransform list = gui.m_recipeListRoot;
				ScrollRect scroll = list != null ? list.GetComponentInParent<ScrollRect>() : null;
				var area = scroll != null ? (RectTransform)scroll.transform : list != null ? (RectTransform)list.parent : null;
				if (area == null) return;

				// The game's own text field (Steam keyboard support), from the map.
				TMP_InputField source = Minimap.instance != null
					? AccessTools.Field(typeof(Minimap), "m_nameInput")?.GetValue(Minimap.instance) as TMP_InputField
					: null;
				if (source == null) { Plugin.Log.LogWarning("Recipe search: no text field to copy."); return; }

				GameObject go = UnityEngine.Object.Instantiate(source.gameObject, area.parent, false);
				go.name = Name;
				go.SetActive(true);
				_field = go.GetComponent<TMP_InputField>();
				_field.onValueChanged.RemoveAllListeners();
				_field.onSubmit.RemoveAllListeners();
				_field.onEndEdit.RemoveAllListeners();
				_field.text = "";
				_field.characterLimit = 30;
				_field.lineType = TMP_InputField.LineType.SingleLine;
				SetMember(_field, "VirtualKeyboardTitle", "Search recipes");
				SetMember(_field, "VirtualKeyboardOnActivate", true);
				SetMember(_field, "CaretOnGamepadUsage", true);
				if (_field.placeholder is TMP_Text ph) { ph.text = "Search recipes..."; ph.enableWordWrapping = false; }
				_field.onValueChanged.AddListener(OnChanged);
				_field.onSubmit.AddListener(_ => _field.DeactivateInputField());

				// Place it where the list's top used to be, and shift the list down.
				var rt = (RectTransform)go.transform;
				rt.anchorMin = new Vector2(area.anchorMin.x, area.anchorMax.y);
				rt.anchorMax = new Vector2(area.anchorMax.x, area.anchorMax.y);
				rt.pivot = new Vector2(0.5f, 1f);
				rt.offsetMin = new Vector2(area.offsetMin.x, area.offsetMax.y - Height);
				rt.offsetMax = new Vector2(area.offsetMax.x, area.offsetMax.y);
				rt.SetAsLastSibling();
				area.offsetMax = new Vector2(area.offsetMax.x, area.offsetMax.y - Height - 4f);

				// The controller hint (R3 glyph) at the box's right end.
				TMP_Text label = _field.textComponent;
				if (label != null)
				{
					var hint = UnityEngine.Object.Instantiate(label.gameObject, go.transform, false);
					hint.name = "LLA_SearchHint";
					foreach (MonoBehaviour mb in hint.GetComponents<MonoBehaviour>()) if (!(mb is TMP_Text)) UnityEngine.Object.Destroy(mb);
					_hint = hint.GetComponent<TMP_Text>();
					var hrt = (RectTransform)hint.transform;
					hrt.anchorMin = new Vector2(1f, 0f);
					hrt.anchorMax = new Vector2(1f, 1f);
					hrt.pivot = new Vector2(1f, 0.5f);
					hrt.sizeDelta = new Vector2(60f, 0f);
					hrt.anchoredPosition = new Vector2(-6f, 0f);
					_hint.alignment = TextAlignmentOptions.Right;
					_hint.richText = true;
					_hint.raycastTarget = false;
				}
				Plugin.Debug("[UI] Recipe search added.");
			}
			catch (Exception ex) { Plugin.Log.LogWarning($"Recipe search: {ex.Message}"); }
		}

		private static void SetMember(object target, string name, object value)
		{
			var f = AccessTools.Field(target.GetType(), name);
			if (f != null && f.FieldType.IsInstanceOfType(value)) f.SetValue(target, value);
		}

		private static void OnChanged(string text)
		{
			_query = text ?? "";
			if (InventoryGui.instance != null && InventoryGui.IsVisible()) InventoryGui.instance.UpdateCraftingPanel(false);
		}
	}
}
