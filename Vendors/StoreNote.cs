using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryLoot.Vendors
{
	/// <summary>
	/// A line under the shop title of gear vendors: "Discover new lands and recipes so I can offer new item types!"
	/// The item list and its scrollbar move down to make room.
	/// </summary>
	public static class StoreNote
	{
		public const string Text = "Discover new lands and recipes so I can offer new item types!";
		private const float Room = 44f; // two lines of note plus a gap under the title
		public const float TopPad = 10f; // tabs sit this much lower than the vanilla title, for breathing room

		private static TMP_Text _note;
		private static bool _shifted;

		private static void Ensure(StoreGui store)
		{
			if (_note != null || store.m_rootPanel == null) return;
			TMP_Text title = FindTitle(store);
			if (title == null) { Plugin.Debug("Shop note: no title found."); return; }

			var go = new GameObject("LLA_ShopNote", typeof(RectTransform));
			go.transform.SetParent(title.transform.parent, false);
			var t = go.AddComponent<TextMeshProUGUI>();
			t.font = title.font;
			t.fontSize = Mathf.Max(14f, title.fontSize * 0.42f);
			t.fontStyle = FontStyles.Italic;
			t.color = new Color(0.93f, 0.86f, 0.72f);
			t.alignment = TextAlignmentOptions.Center;
			t.enableWordWrapping = true;
			t.raycastTarget = false;
			t.text = Text;

			RectTransform tr = title.rectTransform;
			var rt = (RectTransform)go.transform;
			rt.anchorMin = tr.anchorMin;
			rt.anchorMax = tr.anchorMax;
			rt.pivot = new Vector2(0.5f, 1f);
			float width = ((RectTransform)store.m_rootPanel.transform).rect.width;
			rt.sizeDelta = new Vector2(width > 1f ? width - 60f : 360f, Room);
			// Just under the title's bottom edge.
			float titleBottom = tr.anchoredPosition.y - tr.rect.height * tr.pivot.y;
			rt.anchoredPosition = new Vector2(tr.anchoredPosition.x, titleBottom - 10f - TopPad);
			_note = t;

			ShiftList(store, title.transform);
		}

		/// <summary>The biggest text in the panel that isn't in the item list or on a button: the "STORE" title.</summary>
		public static TMP_Text FindTitle(StoreGui store)
		{
			TMP_Text best = null;
			foreach (TMP_Text t in store.m_rootPanel.GetComponentsInChildren<TMP_Text>(true))
			{
				if (store.m_listRoot != null && t.transform.IsChildOf(store.m_listRoot)) continue;
				if (t.GetComponentInParent<Button>() != null) continue;
				if (t == store.m_coinText) continue;
				if (best == null || t.fontSize > best.fontSize) best = t;
			}
			return best;
		}

		/// <summary>Moves the top edge of the list (and its scrollbar) down to make room for the note.</summary>
		private static void ShiftList(StoreGui store, Transform title)
		{
			if (_shifted) return;
			_shifted = true;
			Transform panel = store.m_rootPanel.transform;
			foreach (Transform t in new[] { store.m_listRoot, store.m_listScroll != null ? store.m_listScroll.transform : null })
			{
				// The outermost container of the list that doesn't also hold the title.
				Transform top = t;
				while (top != null && top.parent != null && top.parent != panel && !title.IsChildOf(top.parent)) top = top.parent;
				if (!(top is RectTransform rt)) continue;
				if (rt.anchorMin.y != rt.anchorMax.y) rt.offsetMax = new Vector2(rt.offsetMax.x, rt.offsetMax.y - Room - TopPad);
				else
				{
					// Fixed height: shrink it from the top.
					float h = rt.sizeDelta.y;
					rt.sizeDelta = new Vector2(rt.sizeDelta.x, h - Room - TopPad);
					rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, rt.anchoredPosition.y - (Room + TopPad) * (1f - rt.pivot.y));
				}
			}
		}

		public const string QuestText = "Up to 3 quests at a time. Accepted quests are marked on your map.";
		public const string EmptyText = "No work for you yet. Defeat the next boss and new lands open up, then come back.";

		public static void SetFor(Tabs.Tab tab)
		{
			if (_note == null) return;
			if (tab != Tabs.Tab.Quests) _note.text = Text;
			else _note.text = QuestBoard.LastListEmpty ? EmptyText : QuestText;
		}

		[HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Show))]
		private static class Patch_Show
		{
			private static void Postfix(StoreGui __instance)
			{
				Ensure(__instance);
				if (_note != null) _note.gameObject.SetActive(Shop.ShopOf(__instance.m_trader, out _) != null);
			}
		}
	}
}
