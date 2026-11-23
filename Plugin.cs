using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LegendaryLoot.Data;
using ServerSync;

namespace LegendaryLoot
{
	[BepInPlugin(ModGuid, ModName, ModVersion)]
	public class Plugin : BaseUnityPlugin
	{
		public const string ModGuid = "BhimaJenkins.LegendaryLootARPG";
		public const string ModName = "Legendary Loot and ARPG";
		public const string ModVersion = "0.0.58";

		public static Plugin Instance;
		public static ManualLogSource Log;

		/// <summary>Syncs config values and data files from the server to every player.</summary>
		public static readonly ConfigSync ConfigSync = new ConfigSync(ModGuid)
		{
			DisplayName = ModName,
			CurrentVersion = ModVersion,
			MinimumRequiredVersion = ModVersion,
			ModRequired = true,
		};

		// General
		public static ConfigEntry<bool> ServerConfigLocked;
		public static ConfigEntry<bool> DebugLogging;

		// Loot
		public static ConfigEntry<float> DropMultiplier;
		public static ConfigEntry<UniqueChancePreset> UniqueChance;
		public static ConfigEntry<float> UniqueDropRate;
		public static ConfigEntry<float> WeaponShare;
		public static ConfigEntry<float> PersonalLootMinutes;
		public static ConfigEntry<float> BossLootRange;
		public static ConfigEntry<bool> CraftedItemsRoll;
		public static ConfigEntry<bool> UnfinishedUniques;
		public static bool AllUniquesForTesting => UnfinishedUniques != null && UnfinishedUniques.Value;

		// Vendors
		public static ConfigEntry<bool> ResetQuests;
		public static ConfigEntry<bool> BossHealthNumbers;
		public static ConfigEntry<bool> RecipeSearchEnabled;
		public static ConfigEntry<bool> AlwaysModKeyboard;
		public static ConfigEntry<float> HealthPerExtraPlayer;
		public static ConfigEntry<float> ExtraPlayerRange;

		// Visuals
		public static ConfigEntry<bool> ShowLightShafts;
		public static ConfigEntry<TooltipFont> TooltipFontChoice;
		public static ConfigEntry<bool> TooltipFrame;
		public static ConfigEntry<float> TooltipTextureStrength;
		public static ConfigEntry<float> TooltipMaxHeight;

		private Harmony _harmony;

		private void Awake()
		{
			Instance = this;
			Log = Logger;

			ServerConfigLocked = Bind("1 - General", "Lock configuration", true,
				"If on, only server admins can change synced settings, and every player uses the server's values.");
			ConfigSync.AddLockingConfigEntry(ServerConfigLocked);
			DebugLogging = config("1 - General", "Debug logging", false,
				"Writes every loot roll to the BepInEx log. Useful while testing.", synced: false);

			DropMultiplier = config("2 - Loot", "Drop chance multiplier", 1f,
				new ConfigDescription("Multiplies the chance that a kill drops gear. 1 = normal.", new AcceptableValueRange<float>(0.1f, 3f)));
			UniqueChance = config("2 - Loot", "Unique chance", UniqueChancePreset.Normal,
				"How often Uniques drop. Normal uses the biome table; Always makes every drop Unique (for testing).");
			UniqueDropRate = config("2 - Loot", "Unique drop rate", 1f,
				new ConfigDescription("Multiplies every Unique chance, 1x to 5x (after loot.yml's uniqueChanceScale, which halves them by default).",
					new AcceptableValueRange<float>(1f, 5f)));
			WeaponShare = config("2 - Loot", "Weapon share", 0.5f,
				new ConfigDescription("Share of drops that are weapons. The rest are armor and trinkets.", new AcceptableValueRange<float>(0f, 1f)));
			PersonalLootMinutes = config("2 - Loot", "Personal loot minutes", 5f,
				new ConfigDescription("Minutes a drop stays reserved for its owner before anyone can pick it up.", new AcceptableValueRange<float>(0f, 60f)));
			BossLootRange = config("2 - Loot", "Boss loot range", 50f,
				new ConfigDescription("Players within this many metres of a boss when it dies get their own boss drops.", new AcceptableValueRange<float>(10f, 200f)));
			CraftedItemsRoll = config("2 - Loot", "Crafted items roll affixes", true,
				"Crafted weapons, armor and trinkets roll a tier and affixes like a 0-star drop from the item's biome.");

			UnfinishedUniques = config("2 - Loot", "Unfinished Uniques (testing)", false,
				"Lets Uniques drop before their named power is coded, so you can test names, colors and tooltips. Their power does nothing yet.");

			ResetQuests = config("4 - Vendors", "Reset my quests", false,
				"Tick this to drop all your active quests (and their map pins) and bring back every quest offer for today. " +
				"It unticks itself. For testing, or a fresh start.", synced: false);
			ResetQuests.SettingChanged += (_, __) =>
			{
				if (!ResetQuests.Value) return;
				Vendors.QuestBoard.ResetAll();
				ResetQuests.Value = false;
			};

			ShowLightShafts = config("3 - Visuals", "Light shafts", true,
				"Show a beam of light in the tier's color above dropped gear.", synced: false);
			TooltipFontChoice = config("3 - Visuals", "Tooltip font", TooltipFont.Designed,
				"Fonts for item tooltips. Designed: Spectral SC and Faculty Glyphic (bundled, free fonts). Norse: Valheim's runic font. Vanilla: no change.", synced: false);
			TooltipFrame = config("3 - Visuals", "Tooltip frame", true,
				"Draw a worn bronze frame around item tooltips.", synced: false);
			TooltipTextureStrength = config("3 - Visuals", "Tooltip texture strength", 0.3f,
				new ConfigDescription("How much of the rocky texture shows in magic item tooltips. 0 = smooth color, 1 = full texture. " +
				                      "To use your own pattern, put tooltip_texture.png (greys) in the mod's textures folder.",
					new AcceptableValueRange<float>(0f, 1f)), synced: false);
			TooltipTextureStrength.SettingChanged += (_, __) => Weathering.ResetTooltipWash();
			TooltipMaxHeight = config("3 - Visuals", "Tooltip max height", 0.8f,
				new ConfigDescription("Tallest an item tooltip can be, as a share of the screen height. Longer tooltips scroll " +
				                      "with the mouse wheel or the right stick.", new AcceptableValueRange<float>(0.3f, 0.95f)), synced: false);

			RecipeSearchEnabled = config("3 - Visuals", "Recipe search", true,
				"A search box above the crafting list (controller: press the right stick to type; Steam's keyboard opens in Big Picture).", synced: false);
			AlwaysModKeyboard = config("3 - Visuals", "Always use mod keyboard", false,
				"Controller typing: off = Steam's keyboard in Steam Big Picture / SteamOS and the mod's own keyboard everywhere else; on = always the mod's.", synced: false);
			BossHealthNumbers = config("3 - Visuals", "Boss health numbers", true,
				"Show the boss's health as numbers (current / max) on the boss health bar.", synced: false);

			HealthPerExtraPlayer = config("5 - Enemies", "Health per extra player", 30f,
				new ConfigDescription("Percent more health creatures get for each additional player near them (bosses and unique enemies " +
				                      "included). 2 players: +30%, 3 players: +60%. 0 = off.", new AcceptableValueRange<float>(0f, 200f)));
			ExtraPlayerRange = config("5 - Enemies", "Extra player range", 50f,
				new ConfigDescription("Players within this many metres of a creature count for its extra health.", new AcceptableValueRange<float>(10f, 200f)));

			DataStore.Init(this);
			SoundLibrary.Load(this, DropSound.File, DropSound.UniqueFile, Powers.RavenSound.File, "thrashersound.ogg", "windfurysound.ogg", "throatcuttersound.ogg", "mistcloaksound.ogg");

			foreach (Data.StallDef stall in DataStore.Vendors.Stalls.Values)
				if (stall != null && !string.IsNullOrEmpty(stall.Voice)) SoundLibrary.LoadPrefix(this, stall.Voice);

			_harmony = new Harmony(ModGuid);
			_harmony.PatchAll();
			Log.LogInfo($"{ModName} {ModVersion} loaded.");
		}

		private void Update()
		{
			DataStore.ProcessPending();
		}

		private void OnDestroy()
		{
			_harmony?.UnpatchSelf();
		}

		private ConfigEntry<T> Bind<T>(string group, string name, T value, string description)
		{
			return Config.Bind(group, name, value, new ConfigDescription(description));
		}

		private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synced = true)
		{
			return config(group, name, value, new ConfigDescription(description), synced);
		}

		private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synced = true)
		{
			string suffix = synced ? " [Synced with server]" : " [Not synced with server]";
			var desc = new ConfigDescription(description.Description + suffix, description.AcceptableValues, description.Tags);
			ConfigEntry<T> entry = Config.Bind(group, name, value, desc);
			SyncedConfigEntry<T> syncedEntry = ConfigSync.AddConfigEntry(entry);
			syncedEntry.SynchronizedConfig = synced;
			return entry;
		}

		public static void Debug(string message)
		{
			if (DebugLogging != null && DebugLogging.Value) Log.LogInfo(message);
		}
	}

	public enum UniqueChancePreset
	{
		Off,
		Half,
		Normal,
		Triple,
		Ten,
		Always,
	}
}
