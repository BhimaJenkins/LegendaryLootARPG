using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using LegendaryLoot.Core;
using LegendaryLoot.Data;
using LegendaryLoot.Loot;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LegendaryLoot.Vendors
{
	/// <summary>
	/// Gear for sale at Haldor, Hildir and the Bog Witch (vendors.yml), shown at the top of their normal shop list:
	///  - daily specials: pre-rolled items you can inspect, the same for everyone, new every in-game day
	///  - a mystery item, once a day (any type, tier odds from the boss biome, small Unique chance)
	///  - a random item of each tier, all of today's item type (e.g. Rare Sword ... Legendary Sword)
	///  - the Unique gamble of today's type (once the Ashlands boss is down)
	/// The Reroll button under the shop swaps today's item type for a growing price. Gear comes from the biome of the
	/// last boss you defeated. The entries are vanilla trade items with no prefab (like Hildir's key items), so the shop
	/// shows, selects and prices them itself; buying them is handled here.
	/// </summary>
	public static class Shop
	{
		public enum Kind { Special, Mystery, Tier, Gamble }

		public class Offer
		{
			public Kind Kind;
			public Tier Tier;
			public int SpecialIndex;
			public LootRoller.Result Special; // pre-rolled item (specials only)
			public bool SoldOut;              // Mystery item bought in the last 24 hours
		}

		private static readonly Dictionary<Trader.TradeItem, Offer> Offers = new Dictionary<Trader.TradeItem, Offer>();
		private static List<Trader.TradeItem> _entries;
		private static Trader _for;
		private static string _builtKey;

		// ---------- which shop, which biome ----------

		public static ShopDef ShopOf(Trader trader, out string key)
		{
			key = null;
			if (trader == null) return null;
			string name = Utils.GetPrefabName(trader.gameObject).ToLowerInvariant();
			foreach (KeyValuePair<string, ShopDef> kv in DataStore.Vendors.Shops)
				if (kv.Value != null && name.Contains(kv.Key.ToLowerInvariant()))
				{
					key = kv.Key;
					return kv.Value;
				}
			return null;
		}

		/// <summary>Biome step of the last boss defeated (0 = Meadows, also before any boss).</summary>
		public static int BossStep()
		{
			int step = 0;
			Player p = Player.m_localPlayer;
			foreach (KeyValuePair<string, string> kv in DataStore.Vendors.BossKeys)
			{
				bool done = (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(kv.Key)) || (p != null && p.HaveUniqueKey(kv.Key));
				if (done) step = Mathf.Max(step, DataStore.Loot.BiomeStep(kv.Value));
			}
			return step;
		}

		public static float BuyMultiplier() => VendorTables.AtStep(DataStore.Vendors.BiomeBuyMultiplier, BossStep());

		public static bool GambleUnlocked() => BossStep() >= Math.Max(0, DataStore.Loot.BiomeStep(DataStore.Vendors.UniqueGambleFrom));

		// ---------- what fits ----------

		public static bool Matches(ItemPool.Entry e, string type)
		{
			if (string.IsNullOrEmpty(type)) return false;
			if (ItemClassifier.IsWeaponCategory(e.Category))
				return string.Equals(ItemClassifier.SkillName(e.Shared.m_skillType), type, StringComparison.OrdinalIgnoreCase);
			if (string.Equals(type, "Cape", StringComparison.OrdinalIgnoreCase)) return e.Category == ItemCategory.Shoulder;
			return string.Equals(e.Category.ToString(), type, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Items this shop can sell you: from your boss biome, and only ones whose recipe you've discovered (no
		/// spears before you've picked up flint). A type with nothing there comes from the nearest lower biome that
		/// has one you know; types you don't know yet aren't offered.
		/// </summary>
		public static List<ItemPool.Entry> Pool(ShopDef shop, string type, int step)
		{
			var list = new List<ItemPool.Entry>();
			for (int s = step; s >= 0 && list.Count == 0; s--) Collect(shop, type, s, list);
			return list;
		}

		/// <summary>Like Pool, but every item, discovered or not (quest rewards).</summary>
		public static List<ItemPool.Entry> PoolAll(ShopDef shop, int step)
		{
			var list = new List<ItemPool.Entry>();
			for (int s = step; s >= 0 && list.Count == 0; s--) Collect(shop, null, s, list, knownOnly: false);
			return list;
		}

		private static void Collect(ShopDef shop, string type, int s, List<ItemPool.Entry> list, bool knownOnly = true)
		{
			foreach (bool weapons in new[] { true, false })
			{
				List<ItemPool.Entry> pool = ItemPool.Get(s, weapons);
				if (pool == null) continue;
				foreach (ItemPool.Entry e in pool)
				{
					if (e.BiomeStep != s || list.Contains(e)) continue;
					if (knownOnly && !Known(e)) continue;
					if (type != null ? Matches(e, type) : shop.Sells.Exists(t => Matches(e, t))) list.Add(e);
				}
			}
		}

		private static HashSet<string> _recipeItems;
		private static ObjectDB _recipesFor;

		/// <summary>True if you've discovered this item's recipe. Items with no recipe (Deep North moulds) always count.</summary>
		public static bool Known(ItemPool.Entry e)
		{
			Player p = Player.m_localPlayer;
			if (p == null) return false;
			if (_recipesFor != ObjectDB.instance || _recipeItems == null)
			{
				_recipesFor = ObjectDB.instance;
				_recipeItems = new HashSet<string>();
				if (ObjectDB.instance != null)
					foreach (Recipe r in ObjectDB.instance.m_recipes)
						if (r != null && r.m_item != null && r.m_enabled) _recipeItems.Add(r.m_item.name);
			}
			if (!_recipeItems.Contains(e.Prefab.name)) return true;
			return p.IsRecipeKnown(e.Shared.m_name);
		}

		/// <summary>The types this shop has anything for right now (for the Type button).</summary>
		public static List<string> Types(ShopDef shop)
		{
			int step = BossStep();
			var types = new List<string>();
			foreach (string t in shop.Sells)
				if (Pool(shop, t, step).Count > 0) types.Add(t);
			return types;
		}

		// ---------- prices ----------

		public static int Price(Offer o)
		{
			VendorTables v = DataStore.Vendors;
			float m = BuyMultiplier();
			float p;
			switch (o.Kind)
			{
				case Kind.Special: p = v.SpecialPrice * v.TierPrice(o.Special.Magic.Tier) * m; break;
				case Kind.Mystery: p = v.MysteryPrice * m; break;
				case Kind.Tier: p = v.ChosenTierPrice * v.TierPrice(o.Tier) * m; break;
				default: p = v.UniqueGamblePrice * m; break;
			}
			return Mathf.Max(1, Mathf.RoundToInt(p));
		}

		// ---------- the list ----------

		[HarmonyPatch(typeof(Trader), nameof(Trader.GetAvailableItems))]
		private static class Patch_Available
		{
			private static void Postfix(Trader __instance, ref List<Trader.TradeItem> __result)
			{
				if (__result == null) return;
				if (Tabs.Current == Tabs.Tab.Quests && ShopOf(__instance, out _) != null)
				{
					__result = QuestBoard.Entries(__instance);
					return;
				}
				List<Trader.TradeItem> ours = Entries(__instance);
				if (ours == null || ours.Count == 0) return;
				var list = new List<Trader.TradeItem>(ours);
				list.AddRange(__result);
				__result = list;
			}
		}

		private static int Day => EnvMan.instance != null ? EnvMan.instance.GetDay() : 0;

		public static void Refresh() => _builtKey = null;

		public static Trader.TradeItem MysteryEntry { get; private set; }

		private static List<Trader.TradeItem> Entries(Trader trader)
		{
			ShopDef shop = ShopOf(trader, out string shopKey);
			if (shop == null || Player.m_localPlayer == null) return null;
			int step = BossStep();
			string type = TodaysType(shop, shopKey, out _);
			string key = $"{shopKey}|{step}|{Day}|{type}|{BoughtText()}|{DataStore.Vendors.GetHashCode()}|{Player.m_localPlayer.m_knownRecipes.Count}|{(int)MysteryWait(shopKey).TotalMinutes}";
			if (trader == _for && key == _builtKey && _entries != null) return _entries;
			_for = trader;
			_builtKey = key;
			Offers.Clear();
			MysteryEntry = null;
			_entries = new List<Trader.TradeItem>();

			for (int i = 0; i < DataStore.Vendors.SpecialsPerVendor; i++)
			{
				if (Bought(shopKey, i.ToString(CultureInfo.InvariantCulture))) continue;
				LootRoller.Result r = RollSpecial(shop, shopKey, i, step);
				if (r == null) continue;
				ItemDrop.ItemData item = Make(r);
				var o = new Offer { Kind = Kind.Special, SpecialIndex = i, Special = r };
				string name = TooltipPatches.GetDisplayName(item, Localization.instance.Localize(item.m_shared.m_name));
				Add(o, name, item.GetIcon(),
					item.GetTooltip() + "\n\n<color=#A8A8A8>Daily special: new stock tomorrow.</color>");
			}

			{
				Sprite mysteryIcon = MysteryIcon ?? SampleIcon(shop, null, step);
				TimeSpan wait = MysteryWait(shopKey);
				string tip = $"A random {shop.Label} from {Biome(step)}, any type, with a small chance of one from the next biome up. Tier odds follow the last boss you defeated, with a " +
				             $"{DataStore.Vendors.MysteryUniqueChance.ToString("0.#", CultureInfo.InvariantCulture)}% chance of a Unique.\n\n";
				tip += wait > TimeSpan.Zero
					? $"<color=#E06060>Sold out. Back in {(int)wait.TotalHours}h {wait.Minutes:00}m.</color>"
					: "<color=#A8A8A8>One every 24 hours (real time).</color>";
				Add(new Offer { Kind = Kind.Mystery, SoldOut = wait > TimeSpan.Zero }, $"Mystery {shop.Label}", mysteryIcon, tip);
				MysteryEntry = _entries[_entries.Count - 1];
			}

			if (type != null)
			{
				string what = DataStore.Vendors.TypeName(type);
				Sprite icon = SampleIcon(shop, type, step);
				List<ItemPool.Entry> typePool = Pool(shop, type, step);
				int typeStep = typePool.Count > 0 ? typePool[0].BiomeStep : step; // a type missing here comes from the nearest biome
				for (Tier t = Tier.Uncommon; t <= Tier.Legendary; t++)
					Add(new Offer { Kind = Kind.Tier, Tier = t }, $"{Tiers.Colorize(Tiers.GetLabel(t), t)} {what}", icon,
						$"A random {Tiers.GetLabel(t)} {what.ToLowerInvariant()} from {Biome(typeStep)}. Same price however well it rolls.\n\n" +
						"<color=#A8A8A8>Reroll to see another item type.</color>");

				if (GambleUnlocked())
					Add(new Offer { Kind = Kind.Gamble }, $"{Tiers.Colorize("Unique", Tier.Unique)} {what}", icon,
						$"A guaranteed Unique {what.ToLowerInvariant()} from {Biome(typeStep)}.");
			}
			return _entries;
		}

		// ---------- today's item type and rerolls ----------

		private const string StateKey = "LLA.shop";

		/// <summary>Per player: "day|Haldor=Swords,2|Hildir=Chest,0" (type, rerolls today).</summary>
		private static Dictionary<string, KeyValuePair<string, int>> ReadState()
		{
			var d = new Dictionary<string, KeyValuePair<string, int>>(StringComparer.OrdinalIgnoreCase);
			Player p = Player.m_localPlayer;
			if (p == null || p.m_customData == null || !p.m_customData.TryGetValue(StateKey, out string text)) return d;
			string[] parts = text.Split('|');
			if (parts.Length == 0 || parts[0] != Day.ToString(CultureInfo.InvariantCulture)) return d; // new day: fresh start
			for (int i = 1; i < parts.Length; i++)
			{
				int eq = parts[i].IndexOf('=');
				if (eq <= 0) continue;
				string[] v = parts[i].Substring(eq + 1).Split(',');
				int.TryParse(v.Length > 1 ? v[1] : "0", out int n);
				d[parts[i].Substring(0, eq)] = new KeyValuePair<string, int>(v[0], n);
			}
			return d;
		}

		private static void WriteState(Dictionary<string, KeyValuePair<string, int>> d)
		{
			Player p = Player.m_localPlayer;
			if (p == null) return;
			var sb = new System.Text.StringBuilder(Day.ToString(CultureInfo.InvariantCulture));
			foreach (KeyValuePair<string, KeyValuePair<string, int>> kv in d)
				sb.Append('|').Append(kv.Key).Append('=').Append(kv.Value.Key).Append(',').Append(kv.Value.Value);
			p.m_customData[StateKey] = sb.ToString();
		}

		/// <summary>The item type of today's tier offers: rerolled by you, or the day's pick (the same for everyone).</summary>
		public static string TodaysType(ShopDef shop, string shopKey, out int rerolls)
		{
			rerolls = 0;
			List<string> types = Types(shop);
			if (types.Count == 0) return null;
			if (ReadState().TryGetValue(shopKey, out KeyValuePair<string, int> st) && types.Contains(st.Key))
			{
				rerolls = st.Value;
				return st.Key;
			}
			int seed = (WorldGenerator.instance != null ? WorldGenerator.instance.GetSeed() : 0) * 17 + Day * 6151 + StableHash(shopKey);
			return types[(int)((uint)seed % (uint)types.Count)];
		}

		public static int NextRerollPrice(Trader trader)
		{
			ShopDef shop = ShopOf(trader, out string shopKey);
			if (shop == null) return 0;
			TodaysType(shop, shopKey, out int n);
			return DataStore.Vendors.RerollPrice(n);
		}

		/// <summary>Pays for and swaps today's item type. Returns false if it couldn't.</summary>
		public static bool Reroll(StoreGui store)
		{
			Player p = Player.m_localPlayer;
			ShopDef shop = ShopOf(store.m_trader, out string shopKey);
			if (p == null || shop == null) return false;
			List<string> types = Types(shop);
			string current = TodaysType(shop, shopKey, out int n);
			if (types.Count < 2)
			{
				p.Message(MessageHud.MessageType.Center, "Nothing else to show yet.");
				return false;
			}
			int price = DataStore.Vendors.RerollPrice(n);
			Inventory inv = p.GetInventory();
			string coinName = store.m_coinPrefab.m_itemData.m_shared.m_name;
			if (inv.CountItems(coinName) < price)
			{
				p.Message(MessageHud.MessageType.Center, $"Rerolling costs {Coins.Exact(price)} coins.");
				return false;
			}
			types.Remove(current);
			string next = types[Random.Range(0, types.Count)];
			inv.RemoveItem(coinName, price);
			Dictionary<string, KeyValuePair<string, int>> state = ReadState();
			state[shopKey] = new KeyValuePair<string, int>(next, n + 1);
			WriteState(state);
			store.m_buyEffects.Create(store.transform.position, Quaternion.identity);
			Plugin.Debug($"[Shop] Rerolled {shopKey}: {current} -> {next} for {price} coins.");
			Refresh();
			return true;
		}

		private static void Add(Offer o, string name, Sprite icon, string tooltip)
		{
			var ti = new Trader.TradeItem
			{
				m_prefab = null,
				m_stack = 1,
				m_price = Price(o),
				m_name = name,
				m_icon = icon,
				m_tooltip = tooltip,
				m_buyPlayerEffects = new EffectList(),
			};
			Offers[ti] = o;
			_entries.Add(ti);
		}

		private static string Biome(int step)
		{
			string b = DataStore.Loot.BiomeAt(step);
			switch (b)
			{
				case "BlackForest": return "the Black Forest";
				case "AshLands": return "the Ashlands";
				case "DeepNorth": return "the Deep North";
				case "Mountain": return "the Mountains";
				case "Mistlands": return "the Mistlands";
				case "Plains": return "the Plains";
				case "Meadows": return "the Meadows";
				case "Swamp": return "the Swamp";
				default: return b;
			}
		}

		private static Sprite SampleIcon(ShopDef shop, string type, int step)
		{
			List<ItemPool.Entry> pool = Pool(shop, type, step);
			if (pool.Count == 0) return null;
			pool.Sort((a, b) => string.CompareOrdinal(a.Prefab.name, b.Prefab.name));
			ItemDrop.ItemData.SharedData s = pool[0].Shared;
			return s.m_icons != null && s.m_icons.Length > 0 ? s.m_icons[0] : null;
		}

		private static Sprite _mysteryIcon;
		private static bool _mysteryLoaded;

		/// <summary>textures/mystery.png, if the mod has one.</summary>
		private static Sprite MysteryIcon
		{
			get
			{
				if (!_mysteryLoaded) { _mysteryLoaded = true; _mysteryIcon = Weathering.LoadOverride("mystery.png", 0); }
				return _mysteryIcon;
			}
		}

		// ---------- rolling ----------

		public static ItemDrop.ItemData Make(LootRoller.Result r)
		{
			ItemDrop.ItemData item = r.Prefab.GetComponent<ItemDrop>().m_itemData.Clone();
			item.m_dropPrefab = r.Prefab;
			item.m_stack = 1;
			item.m_quality = 1;
			item.m_durability = item.GetMaxDurability();
			item.m_customData = new Dictionary<string, string>();
			item.SetMagic(r.Magic);
			return item;
		}

		/// <summary>Daily special: seeded by world, day, shop and slot (players who know the same recipes see the same items).</summary>
		private static LootRoller.Result RollSpecial(ShopDef shop, string shopKey, int index, int step)
		{
			int seed = (WorldGenerator.instance != null ? WorldGenerator.instance.GetSeed() : 0) * 31 + Day * 7919 + StableHash(shopKey) * 13 + index * 104729;
			Random.State saved = Random.state;
			try
			{
				Random.InitState(seed);
				Tier tier = Tiers.Bump(LootRoller.RollTier(step), DataStore.Vendors.SpecialTierBump);
				if (tier == Tier.Unique) tier = Tier.Legendary;
				return RollTier(Pool(shop, null, step), tier);
			}
			finally
			{
				Random.state = saved;
			}
		}

		private static int StableHash(string s)
		{
			unchecked
			{
				int h = 23;
				foreach (char c in s) h = h * 31 + c;
				return h;
			}
		}

		private static LootRoller.Result RollTier(List<ItemPool.Entry> pool, Tier tier)
		{
			var left = new List<ItemPool.Entry>(pool);
			while (left.Count > 0)
			{
				int i = Random.Range(0, left.Count);
				ItemPool.Entry e = left[i];
				left.RemoveAt(i);
				MagicItem magic = AffixRoller.Roll(new ItemDrop.ItemData { m_shared = e.Shared }, tier, null);
				if (magic != null) return new LootRoller.Result { Prefab = e.Prefab, Magic = magic };
			}
			return null;
		}

		private static LootRoller.Result RollUnique(List<ItemPool.Entry> pool, int step)
		{
			var left = new List<ItemPool.Entry>(pool);
			while (left.Count > 0)
			{
				int i = Random.Range(0, left.Count);
				ItemPool.Entry e = left[i];
				left.RemoveAt(i);
				UniqueDef u = AffixRoller.PickUnique(e.Shared, step);
				if (u == null) continue;
				MagicItem magic = AffixRoller.Roll(new ItemDrop.ItemData { m_shared = e.Shared }, Tier.Unique, u);
				if (magic != null) return new LootRoller.Result { Prefab = e.Prefab, Magic = magic };
			}
			return null;
		}

		private static LootRoller.Result Roll(Offer o, ShopDef shop, string shopKey)
		{
			int step = BossStep();
			string type = o.Kind == Kind.Mystery ? null : TodaysType(shop, shopKey, out _);
			List<ItemPool.Entry> pool = Pool(shop, type, step);
			switch (o.Kind)
			{
				case Kind.Special:
					return o.Special;
				case Kind.Tier:
					return RollTier(pool, o.Tier);
				case Kind.Gamble:
					return RollUnique(pool, step);
				default:
					// A small chance the mystery item comes from the next biome up (tier odds stay this biome's).
					if (Random.value * 100f < DataStore.Vendors.MysteryNextBiomeChance && step < DataStore.Loot.BiomeOrder.Count - 1)
					{
						var next = new List<ItemPool.Entry>();
						Collect(shop, null, step + 1, next, knownOnly: false); // any item from the next biome, known or not
						if (next.Count > 0) { pool = next; Plugin.Debug("[Shop] Mystery item from the next biome up."); }
					}
					if (Random.value * 100f < DataStore.Vendors.MysteryUniqueChance)
					{
						LootRoller.Result u = RollUnique(pool, step);
						if (u != null) return u;
					}
					for (Tier t = LootRoller.RollTier(step); t >= Tier.Uncommon; t--)
					{
						LootRoller.Result r = RollTier(pool, t);
						if (r != null) return r;
					}
					return null;
			}
		}

		// ---------- daily specials bought ----------

		private const string BoughtKey = "LLA.specials";

		private static string BoughtText()
		{
			Player p = Player.m_localPlayer;
			return p != null && p.m_customData != null && p.m_customData.TryGetValue(BoughtKey, out string s) ? s : "";
		}

		// Format: day;shop:index;shop:index
		private static bool Bought(string shop, string index)
		{
			string[] parts = BoughtText().Split(';');
			if (parts.Length < 2 || parts[0] != Day.ToString(CultureInfo.InvariantCulture)) return false;
			string want = shop + ":" + index;
			for (int i = 1; i < parts.Length; i++)
				if (parts[i] == want) return true;
			return false;
		}

		private static void MarkBought(string shop, string index)
		{
			Player p = Player.m_localPlayer;
			if (p == null) return;
			string day = Day.ToString(CultureInfo.InvariantCulture);
			string text = BoughtText();
			if (!text.StartsWith(day + ";", StringComparison.Ordinal) && text != day) text = day;
			p.m_customData[BoughtKey] = text + ";" + shop + ":" + index;
		}

		// ---------- the Mystery item: one every 24 real hours, per vendor ----------

		private const string MysteryKey = "LLA.mystery"; // "Haldor=ticks;Hildir=ticks" (UTC time bought)
		public static readonly TimeSpan MysteryCooldown = TimeSpan.FromHours(24);

		private static Dictionary<string, long> ReadMystery()
		{
			var d = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
			Player p = Player.m_localPlayer;
			if (p == null || p.m_customData == null || !p.m_customData.TryGetValue(MysteryKey, out string text)) return d;
			foreach (string part in text.Split(';'))
			{
				int eq = part.IndexOf('=');
				if (eq > 0 && long.TryParse(part.Substring(eq + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out long t))
					d[part.Substring(0, eq)] = t;
			}
			return d;
		}

		public static TimeSpan MysteryWait(string shopKey)
		{
			if (!ReadMystery().TryGetValue(shopKey, out long ticks)) return TimeSpan.Zero;
			TimeSpan left = new DateTime(ticks, DateTimeKind.Utc) + MysteryCooldown - DateTime.UtcNow;
			return left > TimeSpan.Zero ? left : TimeSpan.Zero;
		}

		private static void MarkMystery(string shopKey)
		{
			Player p = Player.m_localPlayer;
			if (p == null) return;
			Dictionary<string, long> d = ReadMystery();
			d[shopKey] = DateTime.UtcNow.Ticks;
			var parts = new List<string>();
			foreach (KeyValuePair<string, long> kv in d) parts.Add(kv.Key + "=" + kv.Value.ToString(CultureInfo.InvariantCulture));
			p.m_customData[MysteryKey] = string.Join(";", parts);
		}

		public static bool IsSoldOut(Trader.TradeItem ti) => ti != null && Offers.TryGetValue(ti, out Offer o) && o.SoldOut;

		/// <summary>A sold-out Mystery item can't be bought: the Buy button greys out and the price shows grey.</summary>
		[HarmonyPatch(typeof(StoreGui), nameof(StoreGui.CanAfford))]
		private static class Patch_CanAfford
		{
			private static void Postfix(Trader.TradeItem item, ref bool __result)
			{
				if (__result && IsSoldOut(item)) __result = false;
			}
		}

		// ---------- buying ----------

		[HarmonyPatch(typeof(StoreGui), nameof(StoreGui.BuySelectedItem))]
		private static class Patch_Buy
		{
			private static bool Prefix(StoreGui __instance)
			{
				Trader.TradeItem ti = __instance.m_selectedItem;
				if (ti == null || !Offers.TryGetValue(ti, out Offer o)) return true;
				Buy(__instance, ti, o);
				return false;
			}
		}

		private static void Buy(StoreGui store, Trader.TradeItem ti, Offer o)
		{
			Player p = Player.m_localPlayer;
			ShopDef shop = ShopOf(store.m_trader, out string shopKey);
			if (p == null || shop == null) return;
			if (o.SoldOut)
			{
				TimeSpan wait = MysteryWait(shopKey);
				p.Message(MessageHud.MessageType.Center, $"Sold out. Back in {(int)wait.TotalHours}h {wait.Minutes:00}m.");
				return;
			}
			Inventory inv = p.GetInventory();
			string coinName = store.m_coinPrefab.m_itemData.m_shared.m_name;
			int price = ti.m_price;
			if (inv.CountItems(coinName) < price)
			{
				p.Message(MessageHud.MessageType.Center, "Not enough coins.");
				return;
			}
			if (!inv.HaveEmptySlot())
			{
				p.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$inventory_full"));
				return;
			}
			LootRoller.Result r = Roll(o, shop, shopKey);
			if (r == null)
			{
				p.Message(MessageHud.MessageType.Center, "The vendor has nothing like that right now.");
				return;
			}

			ItemDrop.ItemData added = inv.AddItem(r.Prefab.name, 1, 1, 0, 0L, "", false, true);
			if (added == null)
			{
				p.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$inventory_full"));
				return;
			}
			added.SetMagic(r.Magic);
			inv.Changed();
			inv.RemoveItem(coinName, price);
			if (o.Kind == Kind.Special) MarkBought(shopKey, o.SpecialIndex.ToString(CultureInfo.InvariantCulture));
			if (o.Kind == Kind.Mystery) MarkMystery(shopKey);

			store.m_trader.OnBought(ti);
			store.m_buyEffects.Create(store.transform.position, Quaternion.identity);
			p.ShowPickupMessage(added, 1);
			Plugin.Debug($"[Shop] Bought {o.Kind} {Tiers.GetLabel(r.Magic.Tier)} {r.Prefab.name}" +
			             (r.Magic.IsUnique ? $" ({r.Magic.UniqueId})" : "") + $" for {price} coins.");
			Refresh();
			store.FillList();
		}

		[HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Show))]
		private static class Patch_Show
		{
			private static void Prefix() => Refresh();
		}
	}
}
