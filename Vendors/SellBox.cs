using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using LegendaryLoot.Core;
using LegendaryLoot.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryLoot.Vendors
{
	/// <summary>
	/// The vendor Sell tab: a box beside your inventory that uses the normal chest screen and controls.
	/// Drop mod gear in, and the chest's "Take all" button becomes "Sell: N coins". Orange and Unique gear needs
	/// a second press. The box exists only on your screen: it's a Container that never wakes up (no network object,
	/// never saved), carried on the player so the chest screen doesn't close for distance. Whatever is left in it
	/// goes back to your bags when the screen closes (or drops at your feet if they're full).
	/// </summary>
	public static class SellBox
	{
		private const int Width = 8, Height = 4;
		private const float WalkAwayDistance = 12f;

		private static Container _box;
		private static Trader _trader;
		private static bool _armed;
		private static int _armedTotal;
		private static string _takeAllText;
		private static bool _stackAllWasActive;

		public static bool Is(Container c) => c != null && _box != null && c == _box;
		public static bool IsInventory(Inventory inv) => inv != null && _box != null && _box.m_inventory == inv;

		public static bool IsOpen =>
			_box != null && InventoryGui.instance != null && InventoryGui.instance.m_currentContainer == _box && InventoryGui.IsVisible();

		public static void Open(Trader trader)
		{
			Player p = Player.m_localPlayer;
			if (p == null || InventoryGui.instance == null) return;
			if (StoreGui.instance != null) StoreGui.instance.Hide();
			_trader = trader;
			_armed = false;
			// Wait a frame so the button press that opened the box can't also press a button on the chest screen.
			Plugin.Instance.StartCoroutine(OpenNextFrame(p));
		}

		private static IEnumerator OpenNextFrame(Player p)
		{
			yield return null;
			if (p == null || p != Player.m_localPlayer || p.IsDead()) yield break;
			Container box = GetBox(p);
			string who = _trader != null ? Localization.instance.Localize(_trader.m_name) : "the vendor";
			box.m_inventory.m_name = "Sell to " + who;
			InventoryGui.instance.Show(box, 1);
		}

		private static Container GetBox(Player p)
		{
			if (_box != null && _box.transform.parent == p.transform) return _box;
			var go = new GameObject("LLA_SellBox");
			go.SetActive(false); // Container.Awake never runs: no ZNetView, not registered, never saved
			go.transform.SetParent(p.transform, false);
			_box = go.AddComponent<Container>();
			_box.m_name = "Sell";
			_box.m_width = Width;
			_box.m_height = Height;
			Sprite bkg = null;
			GameObject chest = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("piece_chest_blackmetal") ?? ZNetScene.instance.GetPrefab("piece_chest_wood") : null;
			Container proto = chest != null ? chest.GetComponent<Container>() : null;
			if (proto != null) bkg = proto.m_bkg;
			_box.m_bkg = bkg;
			_box.m_inventory = new Inventory("Sell", bkg, Width, Height);
			return _box;
		}

		// ---------- selling ----------

		private static int Total(out bool needsConfirm, out int count)
		{
			needsConfirm = false;
			count = 0;
			int total = 0;
			if (_box == null) return 0;
			foreach (ItemDrop.ItemData item in _box.m_inventory.GetAllItems())
			{
				int price = SellPrice.Of(item);
				if (price <= 0) continue;
				total += price;
				count++;
				MagicItem m = item.GetMagic();
				if (m != null && DataStore.Vendors.NeedsConfirm(m.Tier)) needsConfirm = true;
			}
			return total;
		}

		private static void Sell()
		{
			Player p = Player.m_localPlayer;
			if (p == null || _box == null) return;
			int total = Total(out bool needsConfirm, out int count);
			if (count == 0)
			{
				p.Message(MessageHud.MessageType.Center, "Put gear in the box to sell it.");
				return;
			}
			if (needsConfirm && (!_armed || _armedTotal != total))
			{
				_armed = true;
				_armedTotal = total;
				p.Message(MessageHud.MessageType.Center, "Legendary or Unique gear in the box. Press Sell again to confirm.");
				return;
			}

			Inventory inv = _box.m_inventory;
			foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(inv.GetAllItems()))
				if (SellPrice.Of(item) > 0) inv.RemoveItem(item);

			Coins.Give(p, total);
			_armed = false;
			if (StoreGui.instance != null && StoreGui.instance.m_sellEffects != null)
				StoreGui.instance.m_sellEffects.Create(p.transform.position, Quaternion.identity);
			if (_trader != null) _trader.OnSold();
			p.Message(MessageHud.MessageType.Center, $"Sold {count} item{(count == 1 ? "" : "s")} for {Coins.Exact(total)} coins.");
			Plugin.Debug($"Sold {count} items for {total} coins.");
		}

		/// <summary>Everything left in the box goes back to the player's bags, or drops at their feet.</summary>
		public static void ReturnItems()
		{
			if (_box == null) return;
			Inventory inv = _box.m_inventory;
			if (inv == null || inv.NrOfItems() == 0) return;
			Player p = Player.m_localPlayer;
			bool dropped = false;
			foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(inv.GetAllItems()))
			{
				if (p != null && p.GetInventory().CanAddItem(item))
				{
					p.GetInventory().MoveItemToThis(inv, item);
					if (!inv.ContainsItem(item)) continue;
				}
				if (p == null) continue; // no player to give it to; keep it in the box for next time
				inv.RemoveItem(item);
				Transform t = p.transform;
				ItemDrop.DropItem(item, item.m_stack, t.position + t.forward * 0.6f + Vector3.up, t.rotation);
				dropped = true;
			}
			if (dropped) p.Message(MessageHud.MessageType.Center, "Your bags are full. Unsold gear fell at your feet.");
		}

		private static void Refuse(ItemDrop.ItemData item)
		{
			Player p = Player.m_localPlayer;
			if (p == null || item == null) return;
			string why = item.m_equipped ? "Unequip it first to sell it."
				: item.IsMagic() ? "This can't be sold here."
				: "Vendors only buy enchanted gear here. Sell treasure in the shop.";
			p.Message(MessageHud.MessageType.Center, why);
		}

		// ---------- UI per frame ----------

		private static void UpdateUi(InventoryGui gui)
		{
			Button takeAll = gui.m_takeAllButton;
			TMP_Text label = takeAll != null ? takeAll.GetComponentInChildren<TMP_Text>(true) : null;
			bool open = IsOpen;

			if (open)
			{
				if (label != null && _takeAllText == null) _takeAllText = label.text;
				int total = Total(out bool needsConfirm, out int count);
				if (_armed && _armedTotal != total) _armed = false;
				if (label != null)
					label.text = count == 0 ? "Sell"
						: _armed ? $"Confirm: {Coins.Exact(total)}"
						: $"Sell: {Coins.Exact(total)}";
				if (gui.m_stackAllButton != null && gui.m_stackAllButton.gameObject.activeSelf)
				{
					_stackAllWasActive = true;
					gui.m_stackAllButton.gameObject.SetActive(false);
				}
				// Walked away from the vendor: close up and hand the gear back.
				Player p = Player.m_localPlayer;
				if (_trader != null && p != null && Vector3.Distance(_trader.transform.position, p.transform.position) > WalkAwayDistance)
					gui.Hide();
			}
			else if (_takeAllText != null)
			{
				if (label != null) label.text = _takeAllText;
				_takeAllText = null;
				if (_stackAllWasActive && gui.m_stackAllButton != null) gui.m_stackAllButton.gameObject.SetActive(true);
				_stackAllWasActive = false;
			}
		}

		// ---------- patches ----------

		[HarmonyPatch(typeof(Container), nameof(Container.IsOwner))]
		private static class Patch_IsOwner
		{
			private static bool Prefix(Container __instance, ref bool __result)
			{
				if (!Is(__instance)) return true;
				__result = true;
				return false;
			}
		}

		[HarmonyPatch(typeof(Container), nameof(Container.SetInUse))]
		private static class Patch_SetInUse
		{
			private static bool Prefix(Container __instance, bool inUse)
			{
				if (!Is(__instance)) return true;
				if (!inUse) ReturnItems();
				return false;
			}
		}

		[HarmonyPatch(typeof(Container), nameof(Container.StackAll))]
		private static class Patch_StackAll
		{
			private static bool Prefix(Container __instance) => !Is(__instance);
		}

		[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnTakeAll))]
		private static class Patch_TakeAll
		{
			private static bool Prefix(InventoryGui __instance)
			{
				if (!Is(__instance.m_currentContainer)) return true;
				Sell();
				return false;
			}
		}

		[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnStackAll))]
		private static class Patch_OnStackAll
		{
			private static bool Prefix(InventoryGui __instance) => !Is(__instance.m_currentContainer);
		}

		[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
		private static class Patch_Update
		{
			private static void Postfix(InventoryGui __instance) => UpdateUi(__instance);
		}

		/// <summary>Equipped gear can't go in the box (vanilla would unequip it first, before the move checks run).</summary>
		[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnSelectedItem))]
		private static class Patch_Selected
		{
			private static bool Prefix(InventoryGui __instance, InventoryGrid grid, ItemDrop.ItemData item, InventoryGrid.Modifier mod)
			{
				if (!Is(__instance.m_currentContainer)) return true;
				Player p = Player.m_localPlayer;
				if (p == null) return true;
				if (mod == InventoryGrid.Modifier.Move && item != null && grid == __instance.m_playerGrid && p.IsItemEquiped(item))
				{
					Refuse(item);
					return false;
				}
				ItemDrop.ItemData drag = __instance.m_dragItem;
				if (drag != null && grid == __instance.m_containerGrid && __instance.m_dragInventory != _box.m_inventory && p.IsItemEquiped(drag))
				{
					Refuse(drag);
					return false;
				}
				return true;
			}
		}

		/// <summary>Quick-move (click with the box open, or the controller's move button).</summary>
		[HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis), typeof(Inventory), typeof(ItemDrop.ItemData))]
		private static class Patch_QuickMove
		{
			private static bool Prefix(Inventory __instance, ItemDrop.ItemData item)
			{
				if (!IsInventory(__instance) || SellPrice.CanSell(item)) return true;
				Refuse(item);
				return false;
			}
		}

		/// <summary>Drag and drop, including swaps with whatever is already in the slot.</summary>
		[HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem))]
		private static class Patch_Drop
		{
			private static bool Prefix(InventoryGrid __instance, Inventory fromInventory, ItemDrop.ItemData item, Vector2i pos, ref bool __result)
			{
				Inventory to = __instance.m_inventory;
				if (to == fromInventory) return true;
				if (IsInventory(to) && !SellPrice.CanSell(item))
				{
					Refuse(item);
					__result = false;
					return false;
				}
				if (IsInventory(fromInventory))
				{
					// A swap would put the item in that slot into the box.
					ItemDrop.ItemData there = to.GetItemAt(pos.x, pos.y);
					if (there != null && there != item && !SellPrice.CanSell(there) &&
					    !(there.m_shared.m_name == item.m_shared.m_name && there.m_shared.m_maxStackSize > 1))
					{
						Refuse(there);
						__result = false;
						return false;
					}
				}
				return true;
			}
		}

		/// <summary>Hand the gear back before the game saves, dies or logs out.</summary>
		[HarmonyPatch(typeof(Game), nameof(Game.SavePlayerProfile))]
		private static class Patch_Save
		{
			private static void Prefix()
			{
				if (IsOpen) InventoryGui.instance.Hide();
				ReturnItems();
			}
		}

		[HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
		private static class Patch_Death
		{
			private static void Prefix(Player __instance)
			{
				if (__instance == Player.m_localPlayer) ReturnItems();
			}
		}
	}
}
