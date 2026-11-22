using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using ServerSync;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LegendaryLoot.Data
{
	/// <summary>
	/// Loads the data files (affixes.yml, uniques.yml, loot.yml) from BepInEx/config/LegendaryLootARPG/.
	/// Missing files are written from the defaults built into the mod. On a server, the files' text is synced
	/// to every player with ServerSync, so everyone rolls from the same tables. Editing a file on the server
	/// reloads it live.
	/// </summary>
	public static class DataStore
	{
		public static readonly string Folder = Path.Combine(Paths.ConfigPath, "LegendaryLootARPG");

		public static List<AffixDef> Affixes = new List<AffixDef>();
		public static List<UniqueDef> Uniques = new List<UniqueDef>();
		public static LootTables Loot = new LootTables();
		public static VendorTables Vendors = new VendorTables();
		public static QuestFile Quests = new QuestFile();
		public static EventFile Events = new EventFile();
		public static EnemyFile Enemies = new EnemyFile();

		public static readonly Dictionary<string, AffixDef> AffixById = new Dictionary<string, AffixDef>(StringComparer.OrdinalIgnoreCase);
		public static readonly Dictionary<string, UniqueDef> UniqueById = new Dictionary<string, UniqueDef>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Raised after any data file is (re)loaded, so caches like the item pool can rebuild.</summary>
		public static event Action Reloaded;

		private static readonly string[] FileNames = { "affixes.yml", "uniques.yml", "loot.yml", "powers.yml", "vendors.yml", "quests.yml", "enemies.yml", "events.yml" };
		private static readonly Dictionary<string, CustomSyncedValue<string>> Synced = new Dictionary<string, CustomSyncedValue<string>>();
		private static FileSystemWatcher _watcher;
		private static readonly ConcurrentQueue<string> PendingFiles = new ConcurrentQueue<string>();

		private static readonly IDeserializer Yaml = new DeserializerBuilder()
			.WithNamingConvention(CamelCaseNamingConvention.Instance)
			.IgnoreUnmatchedProperties()
			.Build();

		public static void Init(Plugin plugin)
		{
			Directory.CreateDirectory(Folder);
			foreach (string file in FileNames)
			{
				string path = Path.Combine(Folder, file);
				UpdateFromDefault(file, path);

				var value = new CustomSyncedValue<string>(Plugin.ConfigSync, "data:" + file, File.ReadAllText(path));
				string captured = file;
				value.ValueChanged += () => Parse(captured, value.Value);
				Synced[file] = value;
				Parse(file, value.Value);
			}

			_watcher = new FileSystemWatcher(Folder, "*.yml")
			{
				IncludeSubdirectories = false,
				NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
				EnableRaisingEvents = true,
			};
			_watcher.Changed += OnFileChanged;
			_watcher.Created += OnFileChanged;
		}

		private static string StampPath => Path.Combine(Folder, ".defaults");

		/// <summary>
		/// Keeps the config folder's copy of each data file up to date with mod updates, without losing edits:
		///  - missing: written from the built-in version.
		///  - unedited (matches the built-in version this mod last wrote): replaced by the new built-in version.
		///  - written by a version before this check existed: replaced, with the old copy saved as .bak.
		///  - edited: kept as it is (powers.yml and loot.yml still pick up new built-in entries on load).
		/// </summary>
		private static void UpdateFromDefault(string file, string path)
		{
			string def = ReadDefault(file);
			if (string.IsNullOrEmpty(def)) return;
			Dictionary<string, string> stamps = ReadStamps();
			string defHash = Hash(def);
			try
			{
				if (!File.Exists(path))
				{
					File.WriteAllText(path, def);
				}
				else
				{
					string cur = File.ReadAllText(path);
					string curHash = Hash(cur);
					if (curHash != defHash)
					{
						if (stamps.TryGetValue(file, out string written) && written == curHash)
						{
							File.WriteAllText(path, def);
							Plugin.Log.LogInfo($"{file} updated to this version's built-in copy.");
						}
						else if (!stamps.ContainsKey(file))
						{
							File.Copy(path, path + ".bak", true);
							File.WriteAllText(path, def);
							Plugin.Log.LogInfo($"{file} updated to this version's built-in copy. Your previous copy is saved as {file}.bak.");
						}
						else
						{
							Plugin.Log.LogInfo($"{file} has your own edits, so it was kept. Delete it to get this version's built-in copy.");
							return; // keep the old stamp: an edited file stays "edited"
						}
					}
				}
				stamps[file] = defHash;
				WriteStamps(stamps);
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning($"Couldn't update {file}: {ex.Message}");
			}
		}

		private static Dictionary<string, string> ReadStamps()
		{
			var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				if (!File.Exists(StampPath)) return d;
				foreach (string line in File.ReadAllLines(StampPath))
				{
					int eq = line.IndexOf('=');
					if (eq > 0) d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
				}
			}
			catch (Exception) { }
			return d;
		}

		private static void WriteStamps(Dictionary<string, string> stamps)
		{
			var lines = new List<string> { "# Which built-in version of each data file was last written here. Don't edit." };
			foreach (KeyValuePair<string, string> kv in stamps) lines.Add(kv.Key + "=" + kv.Value);
			File.WriteAllLines(StampPath, lines.ToArray());
		}

		private static string Hash(string text)
		{
			// Line endings can change if a file is opened and saved without edits; ignore them.
			string norm = text.Replace("\r\n", "\n").TrimEnd();
			using (var sha = System.Security.Cryptography.SHA1.Create())
				return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(norm))).Replace("-", "");
		}

		// The file watcher runs on a background thread, so it only queues the file name.
		// Plugin.Update hands it to ProcessPending on the main thread.
		private static void OnFileChanged(object sender, FileSystemEventArgs e) => PendingFiles.Enqueue(e.FullPath);

		public static void ProcessPending()
		{
			while (PendingFiles.TryDequeue(out string path)) Reload(path);
		}

		private static void Reload(string fullPath)
		{
			string file = Path.GetFileName(fullPath);
			if (!Synced.TryGetValue(file, out CustomSyncedValue<string> value)) return;
			// Only the server (or a single-player game) is the source of truth. Clients get the server's copy.
			if (!Plugin.ConfigSync.IsSourceOfTruth) return;
			try
			{
				string text = File.ReadAllText(fullPath);
				if (text == value.Value) return;
				value.AssignLocalValue(text);
				Plugin.Log.LogInfo($"Reloaded {file}.");
			}
			catch (IOException)
			{
				// The editor still has the file open; the next change event will pick it up.
			}
		}

		private static void Parse(string file, string text)
		{
			try
			{
				switch (file)
				{
					case "affixes.yml":
						Affixes = Yaml.Deserialize<List<AffixDef>>(text) ?? new List<AffixDef>();
						AffixById.Clear();
						foreach (AffixDef a in Affixes)
							if (!string.IsNullOrEmpty(a.Id)) AffixById[a.Id] = a;
						break;
					case "uniques.yml":
						Uniques = Yaml.Deserialize<List<UniqueDef>>(text) ?? new List<UniqueDef>();
						UniqueById.Clear();
						foreach (UniqueDef u in Uniques)
							if (!string.IsNullOrEmpty(u.Id)) UniqueById[u.Id] = u;
						break;
					case "powers.yml":
					{
						Powers.PowersFile powers = Yaml.Deserialize<Powers.PowersFile>(text) ?? new Powers.PowersFile();
						MergeDefaultPowers(powers);
						Powers.PowerStore.Load(powers);
						break;
					}
					case "enemies.yml":
						Enemies = Yaml.Deserialize<EnemyFile>(text) ?? new EnemyFile();
						Enemies.Prepare();
						break;
					case "events.yml":
						Events = Yaml.Deserialize<EventFile>(text) ?? new EventFile();
						Events.Prepare();
						break;
					case "quests.yml":
						Quests = Yaml.Deserialize<QuestFile>(text) ?? new QuestFile();
						Quests.Prepare();
						break;
					case "vendors.yml":
						Vendors = Yaml.Deserialize<VendorTables>(text) ?? new VendorTables();
						Vendors.Prepare();
						break;
					case "loot.yml":
						Loot = Yaml.Deserialize<LootTables>(text) ?? new LootTables();
						MergeDefaults(Loot);
						Loot.Prepare();
						break;
				}
				Plugin.Debug($"Parsed {file}.");
				Reloaded?.Invoke();
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError($"Could not read {file}: {ex.Message}. Keeping the previous version.");
			}
		}

		private static Powers.PowersFile _defaultPowers;

		/// <summary>
		/// Adds powers the mod's built-in powers.yml has but this copy doesn't, so an older config copy picks up
		/// newly coded affixes and Uniques. Entries in the config file always win; to switch a power off, keep its
		/// name with an empty list (e.g. "echo: []").
		/// </summary>
		private static void MergeDefaultPowers(Powers.PowersFile powers)
		{
			try
			{
				if (_defaultPowers == null) _defaultPowers = Yaml.Deserialize<Powers.PowersFile>(ReadDefault("powers.yml")) ?? new Powers.PowersFile();
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError($"Built-in powers.yml could not be read: {ex.Message}");
				return;
			}
			powers.Affixes = powers.Affixes ?? new Dictionary<string, List<Powers.PowerDef>>();
			powers.Uniques = powers.Uniques ?? new Dictionary<string, List<Powers.PowerDef>>();
			int added = AddMissing(_defaultPowers.Affixes, powers.Affixes) + AddMissing(_defaultPowers.Uniques, powers.Uniques);
			if (added > 0) Plugin.Log.LogInfo($"powers.yml: added {added} built-in powers your copy didn't have yet.");
		}

		private static int AddMissing(Dictionary<string, List<Powers.PowerDef>> from, Dictionary<string, List<Powers.PowerDef>> to)
		{
			if (from == null) return 0;
			var have = new HashSet<string>(to.Keys, StringComparer.OrdinalIgnoreCase);
			int n = 0;
			foreach (KeyValuePair<string, List<Powers.PowerDef>> kv in from)
			{
				if (have.Contains(kv.Key)) continue;
				to[kv.Key] = kv.Value;
				n++;
			}
			return n;
		}

		/// <summary>
		/// Fills in list entries the mod's built-in loot.yml has but this copy doesn't (creature, material and item
		/// biomes, excluded items). Older config copies pick up new defaults, like Deep North items, automatically;
		/// anything set in the config file still wins.
		/// </summary>
		private static void MergeDefaults(LootTables loot)
		{
			LootTables defaults;
			try { defaults = Yaml.Deserialize<LootTables>(ReadDefault("loot.yml")); }
			catch { return; }
			if (defaults == null) return;
			loot.CreatureBiomes = Merge(loot.CreatureBiomes, defaults.CreatureBiomes);
			loot.MaterialBiomes = Merge(loot.MaterialBiomes, defaults.MaterialBiomes);
			loot.ItemBiomes = Merge(loot.ItemBiomes, defaults.ItemBiomes);
			loot.ExcludedItems = loot.ExcludedItems ?? new List<string>();
			if (defaults.ExcludedItems != null)
				foreach (string item in defaults.ExcludedItems)
					if (!loot.ExcludedItems.Contains(item)) loot.ExcludedItems.Add(item);
		}

		private static Dictionary<string, string> Merge(Dictionary<string, string> mine, Dictionary<string, string> defaults)
		{
			mine = mine ?? new Dictionary<string, string>();
			if (defaults != null)
				foreach (KeyValuePair<string, string> kv in defaults)
					if (!mine.ContainsKey(kv.Key)) mine[kv.Key] = kv.Value;
			return mine;
		}

		private static string ReadDefault(string file)
		{
			Assembly asm = Assembly.GetExecutingAssembly();
			using (Stream s = asm.GetManifestResourceStream("LegendaryLoot.Defaults." + file))
			{
				if (s == null) return "";
				using (var reader = new StreamReader(s)) return reader.ReadToEnd();
			}
		}
	}
}
