using System;
using System.Collections.Generic;
using LegendaryLoot.Core;

namespace LegendaryLoot.Data
{
	/// <summary>One affix from affixes.yml (exported from the Sheet's Affix list).</summary>
	public class AffixDef
	{
		public string Id;                 // internal ID (Sheet column R). Code looks effects up by this.
		public string Name;               // internal name, for logs only
		public string Tooltip;            // what players see. X is replaced by the rolled value.
		public string Group;              // Sheet category, for sorting tooltip lines
		public List<string> AllowedOn = new List<string>();     // item categories (see ItemCategory)
		public List<string> AllowedSkills = new List<string>(); // item also allowed if its skill is listed here
		public List<string> RequiredSkills = new List<string>(); // if set, the item's skill must be one of these
		public List<string> Requires = new List<string>();       // extra checks, e.g. "frostDamage"
		public bool Toggle;               // on/off effect (no number)
		public Dictionary<string, List<float>> Values = new Dictionary<string, List<float>>(); // tier name -> [min, max]
		public float Weight = 1f;

		/// <summary>Value range for a tier, or null if the affix can't roll at that tier.</summary>
		public List<float> RangeFor(Tier tier)
		{
			if (Values == null) return null;
			return Values.TryGetValue(Tiers.ValueTier(tier).ToString(), out List<float> r) && r != null && r.Count > 0 ? r : null;
		}
	}

	/// <summary>One Unique from uniques.yml (exported from the Sheet's Uniques tab).</summary>
	public class UniqueDef
	{
		public string Id;
		public string Name;               // the item takes this name, e.g. "Eaglehorn"
		public string Description;        // tooltip text
		public List<string> AllowedOn = new List<string>();
		public List<string> AllowedSkills = new List<string>();
		public List<string> RequiredSkills = new List<string>();
		public List<string> Items = new List<string>();  // only these items (prefab names, e.g. StaffSkeleton); empty = any that fits
		public Dictionary<string, float> Values = new Dictionary<string, float>(); // numbers its effect reads
		public string MinBiome = "Meadows";
		public float Weight = 1f;
		public bool Enabled = true;       // false = cut

		/// <summary>True when Items is empty or lists this item (by prefab name, matched against its "$item_..." name).</summary>
		public bool AllowsItem(ItemDrop.ItemData.SharedData shared)
		{
			if (Items == null || Items.Count == 0) return true;
			string token = shared?.m_name ?? "";
			foreach (string i in Items)
				if (string.Equals(token, "$item_" + i, System.StringComparison.OrdinalIgnoreCase) || string.Equals(token, i, System.StringComparison.OrdinalIgnoreCase))
					return true;
			return false;
		}

		public float Value(string key, float fallback) => Values != null && Values.TryGetValue(key, out float v) ? v : fallback;
	}

	/// <summary>Drop tables and rules from loot.yml.</summary>
	public class LootTables
	{
		// Biome ladder, lowest to highest. Index = "biome step".
		public List<string> BiomeOrder = new List<string>
			{ "Meadows", "BlackForest", "Swamp", "Mountain", "Plains", "Mistlands", "AshLands", "DeepNorth" };
		// Biomes that aren't on the ladder use another biome's tables.
		public Dictionary<string, string> BiomeAliases = new Dictionary<string, string> { { "Ocean", "Plains" } };

		// 1. Drop chance = (base + perBiomeStep x step + perStar x stars) x slider, capped at 100%.
		public float DropChanceBase = 0.10f;
		public float DropChancePerBiomeStep = 0.03f;
		public float DropChancePerStar = 0.03f;
		public float SwarmDropMultiplier = 0.5f;
		public List<string> SwarmCreatures = new List<string> { "Tick", "Bat", "Deathsquito" };

		// 2. Item biome: chance the item comes from the next biome up (still uses the mob's tier odds).
		public float NextBiomeChance = 0.03f;

		// 4. Unique chance (percent) by biome, plus per star, capped (bosses are uncapped).
		public Dictionary<string, float> UniqueBaseChance = new Dictionary<string, float>();
		public float UniqueChancePerStar = 1f;
		public float UniqueChanceCap = 15f;

		// 5. Tier odds (percent) by biome: Uncommon, Magic, Rare, Epic, Legendary.
		public Dictionary<string, List<float>> TierOdds = new Dictionary<string, List<float>>();

		// 6. Star bump: chance per star to move up one tier.
		public float StarBumpPerStar = 0.10f;
		public float StarBumpMax = 0.50f;

		// Bosses
		public int BossDropsPerPlayer = 2;
		public float BossUniqueMultiplier = 3f;
		public float BossTierBumpChance = 0.55f;

		// Item rules
		public List<string> MagicSkills = new List<string> { "ElementalMagic", "BloodMagic" };
		public List<string> ExcludedSkills = new List<string> { "Pickaxes", "Fishing" };
		public List<string> ExcludedCategories = new List<string> { "Torch" };
		public List<string> ExcludedItems = new List<string>();

		// Which biome a creature belongs to (prefab name). Unlisted creatures use the biome they died in.
		public Dictionary<string, string> CreatureBiomes = new Dictionary<string, string>();
		// Which biome an item belongs to: the highest biome among its recipe materials.
		public Dictionary<string, string> MaterialBiomes = new Dictionary<string, string>();
		public float UniqueChanceScale = 0.5f; // every Unique chance is multiplied by this (then by the config's Unique drop rate)
		public Dictionary<string, string> StationBiomes = new Dictionary<string, string>(); // an item is at least its crafting station's biome
		// Hand-set item biomes (prefab name), for items the material rule gets wrong.
		public Dictionary<string, string> ItemBiomes = new Dictionary<string, string>();

		// Helpers -----------------------------------------------------------

		[NonSerialized] private HashSet<string> _magicSkills, _excludedSkills, _swarm;

		public void Prepare()
		{
			_magicSkills = new HashSet<string>(MagicSkills ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
			_excludedSkills = new HashSet<string>(ExcludedSkills ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
			_swarm = new HashSet<string>(SwarmCreatures ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
			ExcludedCategories = ExcludedCategories ?? new List<string>();
			ExcludedItems = ExcludedItems ?? new List<string>();
		}

		public bool IsMagicSkill(string skill) => _magicSkills != null && _magicSkills.Contains(skill);
		public bool IsExcludedSkill(string skill) => _excludedSkills != null && _excludedSkills.Contains(skill);
		public bool IsSwarm(string creature) => _swarm != null && _swarm.Contains(creature);

		/// <summary>Index on the biome ladder (0 = Meadows), or -1 if unknown.</summary>
		public int BiomeStep(string biome)
		{
			if (string.IsNullOrEmpty(biome)) return -1;
			if (BiomeAliases != null && BiomeAliases.TryGetValue(biome, out string alias)) biome = alias;
			for (int i = 0; i < BiomeOrder.Count; i++)
				if (string.Equals(BiomeOrder[i], biome, StringComparison.OrdinalIgnoreCase)) return i;
			// Forgiving match for typed commands: "deep-north", "deepnorth" and "Deep North" all work.
			string want = LettersOnly(biome);
			for (int i = 0; i < BiomeOrder.Count; i++)
				if (LettersOnly(BiomeOrder[i]) == want) return i;
			return -1;
		}

		private static string LettersOnly(string s)
		{
			var sb = new System.Text.StringBuilder();
			foreach (char c in s) if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
			return sb.ToString();
		}

		public string BiomeAt(int step) => BiomeOrder[Math.Max(0, Math.Min(step, BiomeOrder.Count - 1))];
	}
}

namespace LegendaryLoot.Data
{
	/// <summary>vendors.yml: coins, sell prices and buy multipliers.</summary>
	public class VendorTables
	{
		public int CoinStackSize = 999999;
		public bool CoinsWeightless = true;

		public Dictionary<string, float> TierSellPrice = new Dictionary<string, float>
		{
			{ "Uncommon", 5 }, { "Magic", 12 }, { "Rare", 30 }, { "Epic", 50 }, { "Legendary", 100 }, { "Unique", 400 },
		};
		public List<float> BiomeSellMultiplier = new List<float> { 1, 1.25f, 1.5f, 1.75f, 2, 2.25f, 2.5f, 3 };
		public float CraftedSellShare = 0.25f;
		public List<string> ConfirmTiers = new List<string> { "Legendary", "Unique" };

		public List<float> BiomeBuyMultiplier = new List<float> { 1, 1.5f, 2.5f, 4, 6, 8, 11, 15 };
		public Dictionary<string, StallDef> Stalls = new Dictionary<string, StallDef>();

		public Dictionary<string, ShopDef> Shops = new Dictionary<string, ShopDef>();
		public Dictionary<string, string> TypeNames = new Dictionary<string, string>();
		public Dictionary<string, string> BossKeys = new Dictionary<string, string>();
		public float ChosenTierPrice = 4f;
		public float MysteryPrice = 40f;
		public float MysteryUniqueChance = 1f;
		public float MysteryNextBiomeChance = 2f;
		public int RerollCost = 50;
		public int RerollCostStep = 10;

		/// <summary>Cost of the next reroll after n rerolls today: 50, 60, 80, 110, 150...</summary>
		public int RerollPrice(int n) => RerollCost + RerollCostStep * n * (n + 1) / 2;
		public int SpecialsPerVendor = 2;
		public float SpecialPrice = 10f;
		public int SpecialTierBump = 1;
		public float UniqueGamblePrice = 2500f;
		public string UniqueGambleFrom = "AshLands";

		public string TypeName(string type) =>
			TypeNames != null && TypeNames.TryGetValue(type, out string n) && !string.IsNullOrEmpty(n) ? n : type;

		[NonSerialized] private HashSet<string> _confirm;

		public void Prepare()
		{
			TierSellPrice = TierSellPrice ?? new Dictionary<string, float>();
			BiomeSellMultiplier = BiomeSellMultiplier ?? new List<float>();
			BiomeBuyMultiplier = BiomeBuyMultiplier ?? new List<float>();
			Shops = Shops ?? new Dictionary<string, ShopDef>();
			TypeNames = TypeNames ?? new Dictionary<string, string>();
			BossKeys = BossKeys ?? new Dictionary<string, string>();
			Stalls = Stalls ?? new Dictionary<string, StallDef>();
			_confirm = new HashSet<string>(ConfirmTiers ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
		}

		public bool NeedsConfirm(Tier tier) => _confirm != null && _confirm.Contains(tier.ToString());

		public float TierPrice(Tier tier)
		{
			if (TierSellPrice == null) return 0f;
			foreach (KeyValuePair<string, float> kv in TierSellPrice)
				if (string.Equals(kv.Key, tier.ToString(), StringComparison.OrdinalIgnoreCase)) return kv.Value;
			return 0f;
		}

		public static float AtStep(List<float> list, int step)
		{
			if (list == null || list.Count == 0) return 1f;
			return list[Math.Max(0, Math.Min(step, list.Count - 1))];
		}
	}

	public class StallSell
	{
		public float Price = 10f;
		public int Stack = 1;
	}

	public class StallDef
	{
		public string Category = "Misc"; // old-style piece category (kept for other mods)
		public List<string> Tags = new List<string> { "Misc", "Decor" }; // build menu tabs (Piece usage tags)
		public string IconFile; // a PNG in the mod's textures folder for the build menu icon
		public string Name = "Stall", Description = "", Blueprint, Icon, Npc, Hat, Voice, Greeting = "";
		public List<string> Talk = new List<string>();
		public Dictionary<string, int> Requirements = new Dictionary<string, int>();
		public List<float> NpcPosition = new List<float> { 0, 0, 0 };
		public float NpcRotation = 180f;
		public float NpcScale = 1f;
		public List<float> HatPosition = new List<float> { 0, 0, 0 };
		public List<float> HatRotation = new List<float> { 0, 0, 0 };
		public float HatScale = 1f;
		public float HatLight = 1f; // multiplies the brightness of any light the hat gives off
		public Dictionary<string, StallSell> Sells = new Dictionary<string, StallSell>();
	}

	public class ShopDef
	{
		public string Label = "gear";
		public List<string> Sells = new List<string>();
	}
}

namespace LegendaryLoot.Data
{
	/// <summary>quests.yml: the QUESTS tab's bounties, delves and trophy quests.</summary>
	public class QuestFile
	{
		public int OffersPerVendor = 3;
		public int MaxActivePerVendor = 3;
		public float ApproachDistance = 70f;
		public float PinOffset = 40f;
		public float AreaRadius = 60f;
		public float SearchMin = 400f;
		public float SearchMax = 1500f;
		public bool RefillDelves = true;   // a delve into a cleared dungeon brings its creatures back
		public float BountyCoins = 60f;
		public float DelveCoins = 60f;
		public float TrophyCoins = 40f;
		public List<HuntDef> Hunts = new List<HuntDef>();
		public List<CampDef> Camps = new List<CampDef>();
		public List<DelveDef> Delves = new List<DelveDef>();
		public List<TrophyDef> Trophies = new List<TrophyDef>();
		public List<DifficultyDef> Difficulties = new List<DifficultyDef>();

		public void Prepare()
		{
			if (Difficulties == null || Difficulties.Count == 0)
				Difficulties = new List<DifficultyDef>
				{
					new DifficultyDef { Name = "Normal", Color = "#7CFC7C", MinStars = 0, MaxStars = 1, Coins = 1f },
					new DifficultyDef { Name = "Hard", Color = "#FFA040", MinStars = 1, MaxStars = 3, Coins = 1.5f, BossBump = true },
					new DifficultyDef { Name = "Very Hard", Color = "#FF5050", MinStars = 3, MaxStars = 5, Coins = 2.5f, TierBump = 1, BossBump = true },
				};
			Hunts = Hunts ?? new List<HuntDef>();
			Camps = Camps ?? new List<CampDef>();
			Delves = Delves ?? new List<DelveDef>();
			Trophies = Trophies ?? new List<TrophyDef>();
			foreach (CampDef c in Camps) { c.Locations = c.Locations ?? new List<string>(); c.Creatures = c.Creatures ?? new List<string>(); }
			foreach (DelveDef d in Delves) d.Locations = d.Locations ?? new List<string>();
		}
	}

	public class DifficultyDef
	{
		public string Name = "Normal", Color = "#FFFFFF";
		public int MinStars, MaxStars = 1;
		public float Coins = 1f;
		public int TierBump;
		public bool BossBump;
	}

	public class HuntDef
	{
		public string Title, Description = "", Creature, Biome = "Meadows";
		public int Stars = 3;
		public bool Water;
	}

	public class CampDef
	{
		public string Title, Description = "", Biome = "Meadows";
		public List<string> Locations = new List<string>();
		public List<string> Creatures = new List<string>();
		public int Count = 5;
		public int Stars = 1;
	}

	public class DelveDef
	{
		public string Title, Description = "", Biome = "BlackForest", Found = "You found it!";
		public string Chest = "TreasureChest_forestcrypt"; // the quest chest placed in the deepest room
		public List<string> Locations = new List<string>();
	}

	public class TrophyDef
	{
		public string Title, Description = "", Trophy, Biome = "Meadows";
		public int Count = 1;
	}
}

namespace LegendaryLoot.Data
{
	/// <summary>A looping effect at an affixed enemy's feet.</summary>
	public class AuraDef
	{
		public string Prefab;
		public float Interval = 0.5f; // seconds between repeats
		public float Scale = 1f;      // size (also grows with the enemy)
		public bool Follow;           // moves with the enemy instead of staying where it started
		public float Height;          // meters above the feet
	}

	/// <summary>enemies.yml: star rolls and enemy affixes.</summary>
	public class EnemyFile
	{
		public bool Enabled = true;
		public float StarChance = 20f;
		public float StarChancePerBoss = 2f;
		public int MaxStars = 5;
		public bool UseBiomeCaps = true;
		public Dictionary<string, int> BiomeCaps = new Dictionary<string, int>();
		public float SizePerStar = 0.1f;
		public int SizeFromStars = 3;
		public float EffectChance = 100f;
		public int InfusionMinStars = 3;
		public float InfusionChance = 50f;
		public List<string> Excluded = new List<string>();
		public Dictionary<string, Dictionary<string, float>> Effects = new Dictionary<string, Dictionary<string, float>>();
		public Dictionary<string, Dictionary<string, float>> Infusions = new Dictionary<string, Dictionary<string, float>>();
		public int AffixLootStars = 1;
		public Dictionary<string, AuraDef> Auras = new Dictionary<string, AuraDef>();
		public UniqueEnemySettings Uniques = new UniqueEnemySettings();
		public BossSettings Bosses = new BossSettings();
		public Dictionary<string, Dictionary<string, float>> BossAffixes = new Dictionary<string, Dictionary<string, float>>();

		[NonSerialized] public HashSet<string> ExcludedSet;

		public void Prepare()
		{
			BiomeCaps = BiomeCaps ?? new Dictionary<string, int>();
			Effects = Effects ?? new Dictionary<string, Dictionary<string, float>>();
			Infusions = Infusions ?? new Dictionary<string, Dictionary<string, float>>();
			Auras = Auras ?? new Dictionary<string, AuraDef>();
			Uniques = Uniques ?? new UniqueEnemySettings();
			Bosses = Bosses ?? new BossSettings();
			Bosses.Adds = Bosses.Adds ?? new Dictionary<string, List<string>>();
			Bosses.ExcludedAffixes = Bosses.ExcludedAffixes ?? new Dictionary<string, List<string>>();
			Bosses.ExcludedEffects = Bosses.ExcludedEffects ?? new List<string>();
			BossAffixes = BossAffixes ?? new Dictionary<string, Dictionary<string, float>>();
			ExcludedSet = new HashSet<string>(Excluded ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>A number from an effect or infusion entry (e.g. Quick's "speed").</summary>
		public float Get(string affix, string key, float fallback = 0f)
		{
			Dictionary<string, float> d = null;
			if (!Effects.TryGetValue(affix, out d) && !Infusions.TryGetValue(affix, out d)) BossAffixes.TryGetValue(affix, out d);
			return d != null && d.TryGetValue(key, out float v) ? v : fallback;
		}
	}

	/// <summary>Unique enemies (enemies.yml "uniques"): rare elite creatures with a gold name and glow.</summary>
	public class UniqueEnemySettings
	{
		public bool Enabled = true;
		public float MinMinutes = 20f;
		public float MaxMinutes = 40f;
		public float Range = 80f;
		public int DungeonMin = 1;
		public int DungeonMax = 3;
		public int MinStars = 2;
		public int Drops = 2;
		public float UniqueChanceMultiplier = 2f;
		public int LootStars = 1;
		public UniqueLook Look = new UniqueLook();
		public AuraDef Aura = new AuraDef { Prefab = "fx_Potion_fireresist", Interval = 2f, Scale = 1f, Follow = true };
	}

	/// <summary>How unique enemies look: a gold, metallic coat over their own materials.</summary>
	public class UniqueLook
	{
		public bool Enabled = true;
		public string Style = "metal";   // metal: Standard metal shader; tint: gold tint over its own look
		public bool KeepTexture = false; // metal: keep the creature's painted texture under the gold
		public bool KeepDetail = false;  // metal: keep its bumpy surface detail (makes reflections blocky)
		public string Color = "#FFD15A";
		public float Tint = 0.65f;       // tint style: 0 = own colors, 1 = solid gold
		public float Metallic = 0.6f;
		public float Smoothness = 0.6f;
		public float Emission = 0.15f;   // a faint gold self-glow so it reads in caves
		public float Shimmer = 0.9f;     // metal: brightness of the moving gold bands (0 = off)
		public float ShimmerSpeed = 0.2f;
		public int ShimmerBands = 3;
		public string Texture = "uniquecoat.png"; // a seamless tile in the textures folder; replaces the shimmer bands
		public bool TextureGold = true;  // tint the tile gold (best for grey images); false = its own colors
		public float TextureTiling = 2f; // repeats across the creature
		public float TextureGlow = 0.6f; // how much the tile's bright parts glow
		public float ScrollX = 0.03f;    // tiles per second
		public float ScrollY = 0.08f;
		public bool Overlay = true;        // draw the tile as a glowing layer over the body (works in any light)
		public float OverlayStrength = 1f; // brightness of that layer
		public float OverlayCutoff = 0.35f; // tile brightness below this is see-through (only the bright lines glow)
	}

	/// <summary>Bosses (enemies.yml "bosses"): one boss affix each, plus one normal affix per star.</summary>
	public class BossSettings
	{
		public bool Enabled = true;
		public int MaxStars = 2;             // config up to 5
		public float StarChance = 25f;       // percent for each extra star
		public float HealthPerStar = 10f;    // percent more health per star (normal creatures: 100)
		public float DamagePerStar = 5f;     // percent more damage per star (normal creatures: 50)
		public float TwinHealth = 50f;       // Twin bosses: each has this percent of the (starred) boss's max health
		public float TwinDamage = 55f;       // ...and deals this percent of its damage
		public int BossAffixes = 1;
		public List<string> ExcludedEffects = new List<string>();                              // normal affixes bosses never get
		public Dictionary<string, List<string>> ExcludedAffixes = new Dictionary<string, List<string>>(); // per boss, e.g. Dragon: [Black Hole]
		public Dictionary<string, List<string>> Adds = new Dictionary<string, List<string>>();            // Summoner: what each boss calls
	}

	/// <summary>events.yml: world events (the event director and each event type).</summary>
	public class EventFile
	{
		public bool Enabled = true;
		public float CheckMinutes = 10f;         // how often the server tops up events
		public float SpawnMin = 150f;            // distance from a player
		public float SpawnMax = 600f;
		public int PerPlayer = 2;                // events near each player (within NearRange)
		public float NearRange = 800f;
		public int CapPerPlayer = 4;             // server cap: per online player...
		public int CapMax = 16;                  // ...and in total
		public float Spacing = 250f;             // events stay this far apart
		public float BaseDistance = 50f;         // never within this of player buildings
		public float ActivateRange = 60f;        // the event starts when a player comes this close
		public float LifetimeMinutes = 60f;      // unfinished events fade after this
		public float CooldownMinutes = 45f;      // a finished or faded spot can't host another event for this long
		public float CooldownRadius = 150f;
		public float CircleRadius = 80f;         // the map circle (centered near, not on, the event)
		public float CircleOffset = 40f;
		public float LeaveRange = 120f;          // an active event fails when nobody is this close...
		public float LeaveSeconds = 90f;         // ...for this long
		public string Chest = "TreasureChest_meadows";
		public int GearRolls = 2;
		public float Gold = 10f;                 // x the biome's sell multiplier
		public Dictionary<string, EventType> Types = new Dictionary<string, EventType>();
		public Dictionary<string, BiomeCreatures> Biomes = new Dictionary<string, BiomeCreatures>();
		public Dictionary<string, string> Bosses = new Dictionary<string, string>(); // Boss event: each biome's boss

		public void Prepare()
		{
			Types = Types ?? new Dictionary<string, EventType>();
			Biomes = Biomes ?? new Dictionary<string, BiomeCreatures>();
			Bosses = Bosses ?? new Dictionary<string, string>();
			foreach (BiomeCreatures b in Biomes.Values)
			{
				if (b == null) continue;
				b.Creatures = b.Creatures ?? new List<string>();
				b.Champions = b.Champions ?? new List<string>();
			}
		}
	}

	public class EventType
	{
		public float Weight = 1f;
		public string Name = "";
		public string Start = "";                // message when it begins
		public List<int> Waves = new List<int>(); // Horde/Ambush: creatures in each wave
		public int Escorts = 3;                  // Champion Hunt
		public float PerExtraPlayer = 1f;        // more creatures per wave for each extra player taking part
		public float TriggerRange = 25f;         // Ambush: springs when you step this close
		public int GearRolls = -1;               // reward gear rolls (-1: the file's gearRolls)
		public float UniquePercent;              // a fixed base Unique chance for its chest (0: normal)
		public bool BossLoot;                    // its chest rolls like boss loot (tier bumps)
		public float HealthPercent = 100f;       // Boss event: the boss's health...
		public float DamagePercent = 100f;       // ...and damage, as a percent of the real boss
		public int TiersLower = 2;               // Boss event: may be a boss up to this many biomes below the event's
		public float GoldMultiplier = 1f;
		public float Minutes = 5f;               // Shrine: how long its buff lasts
		public List<ShrineBuff> Buffs = new List<ShrineBuff>(); // Shrine: one is picked per shrine
		public Dictionary<string, float> ShrineHealth = new Dictionary<string, float>(); // Defend: the shrine's health in each biome
		public float WaveDistance = 30f;         // Defend: raiders appear this far out
	}

	public class ShrineBuff
	{
		public string Name = "", Stat = "", Icon = "", Color = "#FFFFFF", Fx = "", Text = "";
		public float Value;
	}

	public class BiomeCreatures
	{
		public List<string> Creatures = new List<string>();
		public List<string> Champions = new List<string>();
	}
}
