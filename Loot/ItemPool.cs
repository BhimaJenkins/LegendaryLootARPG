using System;
using System.Collections.Generic;
using System.Linq;
using LegendaryLoot.Core;
using LegendaryLoot.Data;
using UnityEngine;

namespace LegendaryLoot.Loot
{
	/// <summary>
	/// Every item that can drop, grouped by biome step and by weapon vs. armor.
	/// Built lazily from ObjectDB, so items added by other mods (Therzie's, WoW Weapons) are included automatically.
	/// An item qualifies if it has a crafting recipe and classifies as gear. Its biome is the highest biome among
	/// its recipe materials (materialBiomes in loot.yml), unless itemBiomes sets it by hand.
	/// </summary>
	public static class ItemPool
	{
		public class Entry
		{
			public GameObject Prefab;
			public ItemDrop.ItemData.SharedData Shared;
			public ItemCategory Category;
			public int BiomeStep;
		}

		/// <summary>
		/// Vanilla items that look like gear to the game but aren't real loot. Built in, so it works even with
		/// an older loot.yml. Add your own exclusions under excludedItems in loot.yml.
		/// </summary>
		private static readonly HashSet<string> BuiltInExclusions = new HashSet<string>
		{
			"Tankard", "TankardAnniversary", "Tankard_dvergr", "TankardOdin", // drinking mugs
			"SpearChitin",                                                       // Abyssal Harpoon (a tool for pulling)
			"KnifeButcher",                                                      // only for butchering tamed animals
		};

		private static List<Entry>[] _weapons;
		private static List<Entry>[] _armor;
		private static readonly Dictionary<string, int> ItemBiomeCache = new Dictionary<string, int>();
		private static ObjectDB _builtFor;

		static ItemPool()
		{
			DataStore.Reloaded += () => _builtFor = null;
		}

		public static List<Entry> Get(int biomeStep, bool weapons)
		{
			EnsureBuilt();
			List<Entry>[] lists = weapons ? _weapons : _armor;
			if (lists == null || lists.Length == 0) return null;
			biomeStep = Mathf.Clamp(biomeStep, 0, lists.Length - 1);
			// If a biome has nothing of this kind, fall back to the nearest lower biome that does.
			for (int s = biomeStep; s >= 0; s--)
				if (lists[s].Count > 0) return lists[s];
			return null;
		}

		/// <summary>Biome step of any item (used by crafting). -1 if unknown.</summary>
		public static int GetItemBiomeStep(GameObject prefab)
		{
			if (prefab == null) return -1;
			EnsureBuilt();
			return ItemBiomeCache.TryGetValue(prefab.name, out int step) ? step : ComputeBiomeStep(prefab, new HashSet<string>());
		}

		private static void EnsureBuilt()
		{
			ObjectDB db = ObjectDB.instance;
			if (db == null || db == _builtFor) return;
			_builtFor = db;
			ItemBiomeCache.Clear();

			LootTables loot = DataStore.Loot;
			int steps = loot.BiomeOrder.Count;
			_weapons = new List<Entry>[steps];
			_armor = new List<Entry>[steps];
			for (int i = 0; i < steps; i++)
			{
				_weapons[i] = new List<Entry>();
				_armor[i] = new List<Entry>();
			}

			var excluded = new HashSet<string>(loot.ExcludedItems);
			excluded.UnionWith(BuiltInExclusions);
			var recipeItems = new HashSet<string>();
			foreach (Recipe r in db.m_recipes)
				if (r != null && r.m_item != null && r.m_enabled) recipeItems.Add(r.m_item.name);

			int count = 0;
			foreach (GameObject prefab in db.m_items)
			{
				if (prefab == null || excluded.Contains(prefab.name)) continue;
				ItemDrop drop = prefab.GetComponent<ItemDrop>();
				if (drop == null) continue;
				ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
				if (!ItemClassifier.CanBeMagic(shared)) continue;
				if (shared.m_tamedOnly) continue; // only works on tamed creatures (Butcher Knife and modded copies)
				if (!string.IsNullOrEmpty(shared.m_dlc)) continue; // DLC items (e.g. Cape of Odin) need the DLC to equip
				// Needs a crafting recipe (skips enemy weapons, dev items), unless loot.yml places it by hand:
				// Deep North gear is cast in moulds rather than crafted, so it's listed under itemBiomes.
				bool listed = loot.ItemBiomes != null && loot.ItemBiomes.ContainsKey(prefab.name);
				if (!recipeItems.Contains(prefab.name) && !listed) continue;

				ItemCategory cat = ItemClassifier.Classify(shared);
				string why = NotLoot(prefab, shared, cat);
				if (why != null)
				{
					Plugin.Debug($"  Left out of drops: {prefab.name} ({why}). It can still be crafted and roll affixes.");
					continue;
				}
				int step = GetItemBiomeStep(prefab);
				if (step < 0) continue;
				var entry = new Entry { Prefab = prefab, Shared = shared, Category = cat, BiomeStep = step };
				(ItemClassifier.IsWeaponCategory(cat) ? _weapons : _armor)[step].Add(entry);
				count++;
			}
			Plugin.Log.LogInfo($"Loot pool built: {count} items.");
			if (Plugin.DebugLogging.Value)
				for (int i = 0; i < steps; i++)
					foreach (Entry e in _weapons[i].Concat(_armor[i])) Plugin.Debug($"  [{loot.BiomeOrder[i]}] {e.Prefab.name} ({e.Category})");
			if (Plugin.DebugLogging.Value)
				for (int i = 0; i < steps; i++)
					Plugin.Debug($"  {loot.BiomeOrder[i]}: {_weapons[i].Count} weapons, {_armor[i].Count} armor/trinkets");
		}

		/// <summary>Materials that only make a plain wooden weapon.</summary>
		private static readonly HashSet<string> WoodMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
			{ "Wood", "FineWood", "RoundLog", "ElderBark", "YggdrasilWood", "Blackwood" };

		/// <summary>
		/// Items that should never drop (but can still be crafted, and roll affixes when crafted):
		///  - crowns (the Crown of Valheim makes creatures ignore you entirely)
		///  - wooden weapons: anything named "wood", or made only from wood
		/// Returns the reason, or null if it's fine as loot.
		/// </summary>
		private static string NotLoot(GameObject prefab, ItemDrop.ItemData.SharedData shared, ItemCategory cat)
		{
			string name = prefab.name;
			string token = shared.m_name ?? "";
			if (name.IndexOf("crown", StringComparison.OrdinalIgnoreCase) >= 0 || token.IndexOf("crown", StringComparison.OrdinalIgnoreCase) >= 0)
				return "crown";
			if (!ItemClassifier.IsWeaponCategory(cat) || cat == ItemCategory.Shield) return null;
			if (name.IndexOf("wood", StringComparison.OrdinalIgnoreCase) >= 0 || token.IndexOf("wood", StringComparison.OrdinalIgnoreCase) >= 0)
				return "wooden weapon";
			Recipe recipe = FindRecipe(name);
			if (recipe?.m_resources == null || recipe.m_resources.Length == 0) return null;
			foreach (Piece.Requirement req in recipe.m_resources)
				if (req?.m_resItem != null && !WoodMaterials.Contains(req.m_resItem.gameObject.name)) return null;
			return "wooden weapon";
		}

		private static int ComputeBiomeStep(GameObject prefab, HashSet<string> visiting)
		{
			string name = prefab.name;
			if (ItemBiomeCache.TryGetValue(name, out int cached)) return cached;
			LootTables loot = DataStore.Loot;

			int result = -1;
			if (loot.ItemBiomes != null && loot.ItemBiomes.TryGetValue(name, out string forced))
			{
				result = loot.BiomeStep(forced);
			}
			else if (loot.MaterialBiomes != null && loot.MaterialBiomes.TryGetValue(name, out string matBiome))
			{
				result = loot.BiomeStep(matBiome);
			}
			else if (visiting.Add(name))
			{
				// Highest biome among the recipe's materials. Materials that are themselves crafted
				// (e.g. a modded bar) are followed one level down through their own recipe.
				Recipe recipe = FindRecipe(name);
				if (recipe != null && recipe.m_resources != null)
				{
					foreach (Piece.Requirement req in recipe.m_resources)
					{
						if (req?.m_resItem == null) continue;
						int s = ComputeBiomeStep(req.m_resItem.gameObject, visiting);
						if (s > result) result = s;
					}
				}
				// ...and never below its crafting station's biome (the Arbalest is made of early materials, but at the
				// Black Forge, so it's a Mistlands item).
				if (recipe != null && recipe.m_craftingStation != null && loot.StationBiomes != null
				    && loot.StationBiomes.TryGetValue(recipe.m_craftingStation.gameObject.name, out string stationBiome))
				{
					int s = loot.BiomeStep(stationBiome);
					if (s > result) result = s;
				}
			}
			ItemBiomeCache[name] = result;
			return result;
		}

		private static Recipe FindRecipe(string itemName)
		{
			foreach (Recipe r in ObjectDB.instance.m_recipes)
				if (r != null && r.m_item != null && r.m_item.name == itemName) return r;
			return null;
		}
	}
}
