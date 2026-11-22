using System;
using System.Collections.Generic;
using HarmonyLib;
using LegendaryLoot.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LegendaryLoot.Vendors
{
	/// <summary>
	/// Two buttons under every vendor's shop window, copied from the shop's own Sell button:
	///  - "Sell gear" (every trader): opens the sell box. Left Bumper on a controller.
	///  - "Reroll" with a dice icon and its price (gear shops only): swaps today's item type. Right Bumper.
	/// Y always opens the inventory, the shop itself only uses A, B and up/down, and the triggers are kept free
	/// for switching vendor tabs (Store / Bounties and Quests).
	/// </summary>
	public static class StoreButtons
	{
		private static Button _sell, _type;
		private static TMP_Text _price;

		private static void Ensure(StoreGui store)
		{
			if (store == null || store.m_sellButton == null || store.m_rootPanel == null) return;
			if (_sell == null)
			{
				_sell = Make(store, "LLA_SellGearButton", "Sell gear", new[] { "JoyLBumper", "JoyRBumper" }, OnSell);
				// Our own icon; the shop's coin stays if textures/sellgear.png is missing.
				Image icon = Icon(_sell);
				Sprite gear = Weathering.LoadOverride("sellgear.png", 0);
				if (icon != null && gear != null) SetSprite(icon, gear);
			}
			if (_type == null)
			{
				_type = Make(store, "LLA_RerollButton", "Reroll today's item type", new[] { "JoyRBumper", "JoyLBumper" }, OnType);
				SetupReroll(store, _type);
			}
		}

		private static Button Make(StoreGui store, string name, string tooltip, string[] keys, Action onClick)
		{
			Button original = store.m_sellButton;
			GameObject go = Object.Instantiate(original.gameObject, store.m_rootPanel.transform, false);
			go.name = name;
			Button b = go.GetComponent<Button>();
			b.onClick = new Button.ButtonClickedEvent(); // drop the copied "sell treasure" listener
			b.onClick.AddListener(() => onClick());
			b.interactable = true;
			foreach (Component c in go.GetComponentsInChildren<Component>(true))
				if (c != null && c.GetType().Name == "Localize") Object.Destroy(c);
			UITooltip tip = go.GetComponent<UITooltip>();
			if (tip != null) { tip.m_topic = ""; tip.m_text = tooltip; }

			var rt = (RectTransform)go.transform;
			var srcRt = (RectTransform)original.transform;
			rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
			rt.pivot = new Vector2(0.5f, 1f);
			rt.sizeDelta = new Vector2(Mathf.Max(srcRt.rect.width, 180f), srcRt.rect.height > 1f ? srcRt.rect.height : 40f);
			SetupPad(store, go, keys);
			return b;
		}

		/// <summary>
		/// The coin picture copied from the shop's Sell button: the picture whose sprite is a coin, or else the
		/// smallest visible picture inside the button (never the button's own background or its controller hint).
		/// </summary>
		private static Image Icon(Button button)
		{
			if (button == null) return null;
			UIGamePad pad = button.GetComponent<UIGamePad>();
			Transform hint = pad != null && pad.m_hint != null ? pad.m_hint.transform : null;
			Image coin = null, smallest = null;
			float smallestArea = float.MaxValue;
			foreach (Image img in button.GetComponentsInChildren<Image>(true))
			{
				if (img.gameObject == button.gameObject) continue;
				if (hint != null && img.transform.IsChildOf(hint)) continue;
				string sprite = img.sprite != null ? img.sprite.name : "";
				Plugin.Debug($"{button.name}: picture '{img.name}' sprite '{sprite}' {img.rectTransform.rect.size} active={img.gameObject.activeSelf}");
				if (coin == null && sprite.IndexOf("coin", StringComparison.OrdinalIgnoreCase) >= 0) coin = img;
				if (!img.gameObject.activeSelf || !img.enabled || img.sprite == null) continue;
				Rect r = img.rectTransform.rect;
				float area = r.width * r.height;
				if (area > 0f && area < smallestArea) { smallestArea = area; smallest = img; }
			}
			return coin ?? smallest;
		}

		private static void SetSprite(Image img, Sprite sprite)
		{
			img.sprite = sprite;
			img.preserveAspect = true;
			img.color = Color.white;
		}

		/// <summary>Reroll: the dice (textures/dice.png) in place of the coin, and the price beside them.</summary>
		private static void SetupReroll(StoreGui store, Button button)
		{
			if (button == null) return;
			Image icon = Icon(button);
			Sprite dice = Weathering.LoadOverride("dice.png", 0);
			float iconWidth = 0f;
			if (icon != null)
			{
				if (dice != null)
				{
					SetSprite(icon, dice);
					RectTransform irt = icon.rectTransform;
					iconWidth = irt.rect.width > 1f ? irt.rect.width : 32f;
					irt.anchoredPosition = new Vector2(irt.anchoredPosition.x - iconWidth / 2f - 6f, irt.anchoredPosition.y);
				}
				else
				{
					icon.enabled = false; // no coin: the button reads "Reroll 50" instead
				}
			}

			var go = new GameObject("LLA_RerollPrice", typeof(RectTransform));
			go.transform.SetParent(button.transform, false);
			var text = go.AddComponent<TextMeshProUGUI>();
			TMP_Text like = store.m_coinText;
			if (like != null)
			{
				text.font = like.font;
				text.fontSharedMaterial = like.fontSharedMaterial;
				text.fontSize = like.fontSize * 0.8f;
				text.fontStyle = like.fontStyle;
			}
			text.color = new Color(1f, 0.82f, 0.2f);
			text.raycastTarget = false;
			text.enableWordWrapping = false;
			text.alignment = dice != null ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
			var rt = (RectTransform)go.transform;
			rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
			rt.pivot = new Vector2(dice != null ? 0f : 0.5f, 0.5f);
			rt.sizeDelta = new Vector2(dice != null ? 90f : 170f, 36f);
			rt.anchoredPosition = new Vector2(dice != null ? 2f : 0f, 0f);
			_price = text;
			_diceShown = dice != null;
		}

		private static bool _diceShown;

		private static void SetupPad(StoreGui store, GameObject go, string[] keys)
		{
			UIGamePad pad = go.GetComponent<UIGamePad>();
			if (pad == null) return;
			var used = new HashSet<string>();
			foreach (UIGamePad other in store.m_rootPanel.GetComponentsInChildren<UIGamePad>(true))
				if (other != pad && !string.IsNullOrEmpty(other.m_zinputKey)) used.Add(other.m_zinputKey);
			string key = null;
			foreach (string k in keys)
				if (!used.Contains(k)) { key = k; break; }
			if (key == null)
			{
				Object.Destroy(pad); // nothing free: mouse and touch only
				return;
			}
			pad.m_zinputKey = key;
			if (pad.m_hint == null) return;
			string glyph = null;
			try { glyph = Localization.instance.GetBoundKeyString(key, true); } catch { /* older game */ }
			TMP_Text hint = pad.m_hint.GetComponentInChildren<TMP_Text>(true);
			if (hint != null && !string.IsNullOrEmpty(glyph)) hint.text = glyph;
			foreach (Component c in pad.m_hint.GetComponentsInChildren<Component>(true))
				if (c != null && c.GetType().Name == "Localize") Object.Destroy(c);
			Plugin.Debug($"{go.name} uses {key} on controllers (hint '{glyph}').");
		}

		/// <summary>Lays out the buttons for this trader: both side by side for gear shops, Sell gear alone otherwise.</summary>
		private static void Layout(StoreGui store)
		{
			bool shop = Shop.ShopOf(store.m_trader, out _) != null;
			if (_type != null) _type.gameObject.SetActive(shop);
			float half = _sell != null ? ((RectTransform)_sell.transform).sizeDelta.x / 2f + 6f : 100f;
			if (_sell != null) ((RectTransform)_sell.transform).anchoredPosition = new Vector2(shop ? -half : 0f, -10f);
			if (_type != null) ((RectTransform)_type.transform).anchoredPosition = new Vector2(half, -10f);
			UpdateTypeLabel();
		}

		private static void UpdateTypeLabel()
		{
			if (_price == null || StoreGui.instance == null) return;
			string n = Coins.Exact(Shop.NextRerollPrice(StoreGui.instance.m_trader));
			_price.text = _diceShown ? n : "Reroll " + n;
		}

		/// <summary>Reroll only makes sense on the store tab.</summary>
		public static void SetFor(Tabs.Tab tab)
		{
			StoreGui store = StoreGui.instance;
			bool shop = store != null && Shop.ShopOf(store.m_trader, out _) != null;
			if (_type != null) _type.gameObject.SetActive(shop && tab == Tabs.Tab.Store);
		}

		private static void OnSell()
		{
			StoreGui store = StoreGui.instance;
			SellBox.Open(store != null ? store.m_trader : null);
		}

		private static void OnType()
		{
			StoreGui store = StoreGui.instance;
			if (store == null || !Shop.Reroll(store)) return;
			UpdateTypeLabel();
			int selected = store.GetSelectedItemIndex();
			store.FillList();
			if (selected >= 0) store.SelectItem(selected, false);
		}

		[HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Show))]
		private static class Patch_Show
		{
			private static void Postfix(StoreGui __instance)
			{
				Ensure(__instance);
				Layout(__instance);
			}
		}
	}
}
