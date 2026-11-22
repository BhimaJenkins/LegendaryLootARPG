using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryLoot.Vendors
{
	/// <summary>
	/// STORE and QUESTS tabs in place of the shop title, for the gear vendors (Haldor, Hildir, the Bog Witch).
	/// Click a tab, or use Left and Right Trigger on a controller. The quest list reuses the shop's own list, so it
	/// scrolls and selects the same way; its Buy button reads Accept.
	/// </summary>
	public static class Tabs
	{
		public enum Tab { Store, Quests }

		public static Tab Current = Tab.Store;

		private static TMP_Text _title, _store, _quests, _hintLeft, _hintRight;
		private static Button _storeBox, _questsBox;
		private static Sprite _normalSprite;
		private static string _titleText, _buyText;
		private static Color _activeColor;

		public static bool QuestsOpen => Current == Tab.Quests && StoreGui.IsVisible();

		private static void Ensure(StoreGui store)
		{
			if (_store != null) return;
			_title = StoreNote.FindTitle(store);
			if (_title == null) return;
			Widen(store);
			_titleText = _title.text;
			_activeColor = _title.color;
			_store = MakeTab(store, _title, "STORE", -1, () => Switch(Tab.Store), out _storeBox);
			_quests = MakeTab(store, _title, "QUESTS", 1, () => Switch(Tab.Quests), out _questsBox);
			_hintLeft = MakeHint(_title, -1);
			_hintRight = MakeHint(_title, 1);
		}

		private const float WidenBy = 0.2f;

		/// <summary>
		/// Makes the shop window 20% wider (once), so the tabs and the buttons underneath fit inside it and quest
		/// titles have room. Widens the panel and every fixed-width piece on the way down to the item list.
		/// </summary>
		private static void Widen(StoreGui store)
		{
			var root = (RectTransform)store.m_rootPanel.transform;
			float w = root.rect.width;
			if (w <= 1f) return;
			float extra = w * WidenBy;
			Grow(root, extra);
			for (Transform t = store.m_listRoot; t != null && t != root; t = t.parent)
				if (t is RectTransform rt && rt.anchorMin.x == rt.anchorMax.x) Grow(rt, extra);
			KeepScrollbarBeside(store);
			ListExtra = extra;
			MoveOutside(store.m_sellButton != null ? store.m_sellButton.transform : null, root, extra / 2f);
			_widened = true;
			Plugin.Debug($"Shop window widened from {w:0} to {root.rect.width:0}.");
		}

		private static bool _widened;

		/// <summary>How much wider each list row should be (the rows come from a fixed-width template).</summary>
		public static float ListExtra;

		/// <summary>
		/// The vanilla sell-treasure button sits just off the panel's right edge; the wider panel covered it.
		/// Slide it (or its little frame) right by the panel's growth on that side.
		/// </summary>
		private static void MoveOutside(Transform button, RectTransform root, float dx)
		{
			if (button == null) return;
			Transform move = button;
			if (!button.IsChildOf(root))
				while (move.parent != null && move.parent != root.parent) move = move.parent;
			else
				while (move.parent != null && move.parent != root) move = move.parent;
			if (move is RectTransform mrt && mrt.IsChildOf(root) && mrt.anchorMin.x >= 0.99f && mrt.anchorMax.x >= 0.99f) return; // already follows the right edge
			move.position += root.TransformVector(new Vector3(dx, 0f, 0f));
			Plugin.Debug($"Moved {move.name} right by {dx:0} for the wider shop.");
		}

		/// <summary>If the wider list now runs under the scrollbar, slide the scrollbar out to the list's right edge.</summary>
		private static void KeepScrollbarBeside(StoreGui store)
		{
			if (store.m_listScroll == null || store.m_listRoot == null) return;
			Canvas.ForceUpdateCanvases();
			RectTransform list = store.m_listRoot.parent as RectTransform ?? store.m_listRoot;
			var bar = (RectTransform)store.m_listScroll.transform;
			if (bar.IsChildOf(list)) return; // scrollbar inside the list's own frame moves with it
			var c = new Vector3[4];
			list.GetWorldCorners(c);
			float listRight = c[2].x;
			bar.GetWorldCorners(c);
			float barLeft = c[0].x;
			float gap = 4f * bar.lossyScale.x;
			if (barLeft >= listRight + gap) return;
			Vector3 shift = new Vector3(listRight + gap - barLeft, 0f, 0f);
			bar.position += shift;
		}

		private static void Grow(RectTransform rt, float extra)
		{
			if (rt.anchorMin.x == rt.anchorMax.x) rt.sizeDelta = new Vector2(rt.sizeDelta.x + extra, rt.sizeDelta.y);
			else
			{
				rt.offsetMin = new Vector2(rt.offsetMin.x - extra / 2f, rt.offsetMin.y);
				rt.offsetMax = new Vector2(rt.offsetMax.x + extra / 2f, rt.offsetMax.y);
			}
		}

		private const float TabWidth = 150f, TabHeight = 46f, TabGap = 8f;

		/// <summary>A tab: a copy of the shop's Buy button (its box and hover look) with the tab name in the title font.</summary>
		private static TMP_Text MakeTab(StoreGui store, TMP_Text title, string text, int side, System.Action onClick, out Button box)
		{
			GameObject go = Object.Instantiate(store.m_buyButton.gameObject, title.transform.parent, false);
			go.name = "LLA_Tab_" + text;
			// No controller prompt on the tabs (the copied one showed Buy's button): the triggers switch tabs.
			UIGamePad pad = go.GetComponent<UIGamePad>();
			if (pad != null && pad.m_hint != null) Object.Destroy(pad.m_hint);
			foreach (Component c in go.GetComponentsInChildren<Component>(true))
				if (c != null && (c.GetType().Name == "Localize" || c is UIGamePad || c is UITooltip)) Object.Destroy(c);
			box = go.GetComponent<Button>();
			box.onClick = new Button.ButtonClickedEvent();
			box.onClick.AddListener(() => onClick());
			box.interactable = true;
			if (_normalSprite == null && box.image != null) _normalSprite = box.image.sprite;

			TMP_Text t = go.GetComponentInChildren<TMP_Text>(true);
			if (t != null)
			{
				t.text = text;
				t.font = title.font;
				t.fontSharedMaterial = title.fontSharedMaterial;
				t.enableAutoSizing = false;
				t.fontSize = title.fontSize * 0.62f;
				t.enableWordWrapping = false;
				t.alignment = TextAlignmentOptions.Center;
				t.raycastTarget = false;
			}
			Place((RectTransform)go.transform, title.rectTransform, new Vector2(TabWidth, TabHeight), side * (TabWidth / 2f + TabGap / 2f));
			return t;
		}

		/// <summary>The open tab's box looks hovered (like Accept under the mouse); the other looks normal.</summary>
		private static void SetBox(Button box, bool active)
		{
			if (box == null || box.image == null) return;
			if (box.transition == Selectable.Transition.SpriteSwap)
				box.image.sprite = active && box.spriteState.highlightedSprite != null ? box.spriteState.highlightedSprite : _normalSprite;
			else if (box.transition == Selectable.Transition.ColorTint)
				box.image.color = active ? box.colors.highlightedColor : box.colors.normalColor;
		}

		/// <summary>Controller glyph beside the tabs: Left Trigger before STORE, Right Trigger after QUESTS.</summary>
		private static TMP_Text MakeHint(TMP_Text title, int side)
		{
			var go = new GameObject(side < 0 ? "LLA_TabHintL" : "LLA_TabHintR", typeof(RectTransform));
			go.transform.SetParent(title.transform.parent, false);
			var t = go.AddComponent<TextMeshProUGUI>();
			t.font = title.font;
			t.fontSize = title.fontSize * 0.5f;
			t.color = new Color(0.9f, 0.85f, 0.75f);
			t.alignment = TextAlignmentOptions.Center;
			t.raycastTarget = false;
			string key = side < 0 ? "JoyLTrigger" : "JoyRTrigger";
			string glyph = null;
			try { glyph = Localization.instance.GetBoundKeyString(key, true); } catch { /* older game */ }
			t.text = string.IsNullOrEmpty(glyph) ? (side < 0 ? "LT" : "RT") : glyph;
			Place((RectTransform)go.transform, title.rectTransform, new Vector2(44f, 40f), side * (TabWidth + TabGap / 2f + 24f));
			return t;
		}

		/// <summary>Centers rt on the title's center, shifted sideways (works whatever the title's anchors are).</summary>
		private static void Place(RectTransform rt, RectTransform title, Vector2 size, float dx)
		{
			rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.sizeDelta = size;
			Rect r = title.rect;
			Vector3 center = title.localPosition + new Vector3((0.5f - title.pivot.x) * r.width, (0.5f - title.pivot.y) * r.height, 0f);
			rt.localPosition = center + new Vector3(dx, -StoreNote.TopPad, 0f);
		}

		public static void Switch(Tab tab)
		{
			StoreGui store = StoreGui.instance;
			if (store == null || Current == tab) return;
			Current = tab;
			Apply(store);
			Shop.Refresh();
			QuestBoard.Refresh();
			store.FillList();
			if (store.m_itemList.Count > 0) store.SelectItem(0, true);
		}

		/// <summary>Shows the tabs for gear vendors (the plain title for anyone else) and highlights the open one.</summary>
		public static void Apply(StoreGui store)
		{
			bool shop = Shop.ShopOf(store.m_trader, out _) != null;
			if (!shop) Current = Tab.Store;
			// Hide the vanilla title rather than blanking it: its translation component puts "STORE" back on enable.
			if (_title != null) _title.enabled = !shop;
			if (_storeBox != null) _storeBox.gameObject.SetActive(shop);
			if (_questsBox != null) _questsBox.gameObject.SetActive(shop);
			if (_store != null) _store.color = Current == Tab.Store ? _activeColor : Dim(_activeColor);
			if (_quests != null) _quests.color = Current == Tab.Quests ? _activeColor : Dim(_activeColor);
			SetBox(_storeBox, Current == Tab.Store);
			SetBox(_questsBox, Current == Tab.Quests);
			bool pad = ZInput.IsGamepadActive();
			if (_hintLeft != null) _hintLeft.gameObject.SetActive(shop && pad);
			if (_hintRight != null) _hintRight.gameObject.SetActive(shop && pad);

			// Buy reads Accept on the quest list.
			TMP_Text buy = store.m_buyButton != null ? store.m_buyButton.GetComponentInChildren<TMP_Text>(true) : null;
			if (buy != null)
			{
				if (_buyText == null) _buyText = buy.text;
				buy.text = Current == Tab.Quests ? QuestBoard.ButtonLabel(store.m_selectedItem) : _buyText;
			}
			StoreNote.SetFor(Current);
			StoreButtons.SetFor(Current);
		}

		private static Color Dim(Color c) => new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f, c.a * 0.8f);

		[HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Show))]
		private static class Patch_Show
		{
			private static void Prefix() => Current = Tab.Store; // every visit opens on the store

			[HarmonyPriority(Priority.Low)]
			private static void Postfix(StoreGui __instance)
			{
				bool first = !_widened;
				Ensure(__instance);
				Apply(__instance);
				if (first && _widened) __instance.FillList(); // rebuild the rows at the new width
			}
		}

		/// <summary>Rows come from a fixed-width template: widen each one to match the wider list.</summary>
		[HarmonyPatch(typeof(StoreGui), nameof(StoreGui.FillList))]
		private static class Patch_FillList
		{
			[HarmonyPriority(Priority.First)]
			private static void Postfix(StoreGui __instance)
			{
				if (ListExtra <= 0f) return;
				foreach (GameObject row in __instance.m_itemList)
				{
					var rt = row.transform as RectTransform;
					if (rt == null || rt.anchorMin.x != rt.anchorMax.x) continue; // stretched rows already fit
					Vector2 pos = rt.anchoredPosition;
					rt.sizeDelta = new Vector2(rt.sizeDelta.x + ListExtra, rt.sizeDelta.y);
					// Vanilla centers rows at x = (list width - row width) / 2, so recenter for the new width.
					rt.anchoredPosition = new Vector2(pos.x - ListExtra / 2f, pos.y);
				}
			}
		}

		[HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Update))]
		private static class Patch_Update
		{
			private static void Postfix(StoreGui __instance)
			{
				if (!StoreGui.IsVisible() || _storeBox == null || !_storeBox.gameObject.activeInHierarchy) return;
				if (_title != null && _title.enabled) _title.enabled = false; // in case something re-enabled it
				TMP_Text buy = __instance.m_buyButton != null ? __instance.m_buyButton.GetComponentInChildren<TMP_Text>(true) : null;
				string want = Current == Tab.Quests ? QuestBoard.ButtonLabel(__instance.m_selectedItem) : _buyText;
				if (buy != null && want != null && buy.text != want) buy.text = want;
				if (ZInput.GetButtonDown("JoyLTrigger")) Switch(Tab.Store);
				else if (ZInput.GetButtonDown("JoyRTrigger")) Switch(Tab.Quests);
				bool pad = ZInput.IsGamepadActive();
				if (_hintLeft != null && _hintLeft.gameObject.activeSelf != pad) { _hintLeft.gameObject.SetActive(pad); _hintRight.gameObject.SetActive(pad); }
			}
		}
	}
}
