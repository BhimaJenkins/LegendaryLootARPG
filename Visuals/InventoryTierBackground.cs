using HarmonyLib;
using LegendaryLoot.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryLoot
{
	/// <summary>
	/// Gives magic items a weathered gradient behind their icon, in the tier's color: in the inventory,
	/// in chests, and on the hotbar.
	/// </summary>
	public static class InventoryTierBackground
	{
		private const string ChildName = "LLA_TierBackground";

		/// <summary>Shows or hides the gradient in one slot.</summary>
		public static void Apply(Transform slot, Image icon, MagicItem magic)
		{
			if (slot == null) return;
			Transform existing = slot.Find(ChildName);
			Image bg = existing != null ? existing.GetComponent<Image>() : null;
			if (magic == null)
			{
				if (bg != null) { bg.enabled = false; IconShine.Set(bg, false); UniqueStyle.SetSparkles(bg, false); }
				return;
			}
			if (bg == null) bg = Create(slot, icon);
			bg.enabled = true;
			bool unique = magic.Tier == Tier.Unique;
			// Uniques: polished gold foil with sparkles. Other tiers: the weathered gradient in their color.
			Sprite want = unique ? UniqueStyle.SlotFoil() : Weathering.SlotGradient();
			if (bg.sprite != want) bg.sprite = want;
			bg.color = unique ? Color.white : Tiers.GetColor(magic.Tier);
			UniqueStyle.SetSparkles(bg, unique);
			IconShine.Set(bg, unique);
		}

		private static Image Create(Transform slot, Image icon)
		{
			var go = new GameObject(ChildName, typeof(RectTransform));
			go.transform.SetParent(slot, false);
			// Sit just behind the icon so the item art, amount and durability bar stay on top.
			go.transform.SetSiblingIndex(icon != null && icon.transform.parent == slot ? icon.transform.GetSiblingIndex() : 0);

			var rt = (RectTransform)go.transform;
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.offsetMin = new Vector2(2f, 2f);
			rt.offsetMax = new Vector2(-2f, -2f);

			var layout = go.AddComponent<LayoutElement>();
			layout.ignoreLayout = true;

			var img = go.AddComponent<Image>();
			img.sprite = Weathering.SlotGradient();
			img.type = Image.Type.Simple;
			img.raycastTarget = false; // never block clicks or controller selection
			return img;
		}

		/// <summary>Inventory and chest grids.</summary>
		[HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGui))]
		private static class Patch_InventoryGrid
		{
			private static void Postfix(InventoryGrid __instance)
			{
				Inventory inv = __instance.m_inventory;
				if (inv == null) return;
				for (int y = 0; y < __instance.m_height; y++)
				for (int x = 0; x < __instance.m_width; x++)
				{
					InventoryElement element = __instance.GetElement(x, y, __instance.m_width);
					if (element == null) continue;
					Apply(element.transform, element.m_icon, inv.GetItemAt(x, y)?.GetMagic());
				}
			}
		}

		/// <summary>The hotbar is drawn separately from the inventory, so it needs its own pass.</summary>
		[HarmonyPatch(typeof(HotkeyBar), nameof(HotkeyBar.UpdateIcons))]
		private static class Patch_HotkeyBar
		{
			private static void Postfix(HotkeyBar __instance)
			{
				if (__instance.m_elements == null) return;
				for (int i = 0; i < __instance.m_elements.Count; i++)
				{
					HotkeyBar.ElementData e = __instance.m_elements[i];
					if (e?.m_go == null) continue;
					MagicItem magic = null;
					if (__instance.m_items != null)
						foreach (ItemDrop.ItemData item in __instance.m_items)
							if (item != null && item.m_gridPos.x == i) { magic = item.GetMagic(); break; }
					Apply(e.m_go.transform, e.m_icon, magic);
				}
			}
		}
	}
}
