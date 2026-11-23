using System.Collections.Generic;
using System;
using HarmonyLib;
using LegendaryLoot.Core;
using LegendaryLoot.Data;
using LegendaryLoot.Loot;
using UnityEngine;

namespace LegendaryLoot
{
	/// <summary>
	/// Testing commands (need devcommands, like other cheats). Hyphens and lower case only, so they can be typed
	/// on the Steam Deck's on-screen keyboard (which can't type an underscore in game):
	/// Put a number first to repeat a command (lla-drop 20, lla-tier 10 rare, lla-unique 5 thunderstroke).
	///   lla-drop [biome] [stars]   - roll one guaranteed drop as if you killed a creature here
	///   lla-drop boss [biome]      - roll a boss drop
	///   lla-tier &lt;tier&gt;            - spawn a random item of that tier (Uncommon..Legendary, Unique)
	///   lla-affix &lt;affix&gt; [tier]   - spawn an item that has this affix (default Legendary)
	///   lla-kill100 [biome] [stars] - roll 100 kills and print how many drops of each tier came out
	/// </summary>
	[HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
	public static class Commands
	{
		private static void Postfix()
		{
			Register("lla-drop", "[biome|boss] [stars] - Legendary Loot: roll a guaranteed drop here", (a, t) =>
			{
				Player p = Player.m_localPlayer;
				if (p == null) return;
				bool boss = a.Length > 1 && a[1].Equals("boss", StringComparison.OrdinalIgnoreCase);
				int argBase = boss ? 2 : 1;
				int step = ParseBiome(a, argBase, p.transform.position);
				int stars = a.Length > argBase + 1 && int.TryParse(a[argBase + 1], out int s) ? Mathf.Clamp(s, 0, 5) : 0;
				var ctx = new LootRoller.Context
				{
					BiomeStep = step, Stars = stars, Boss = boss, Guaranteed = true,
					UniqueFind = Effects.PlayerStats.Get(p, "Luck"), Source = "lla-drop",
				};
				LootRoller.Result r = LootRoller.Roll(ctx);
				if (r == null) { Say(t, "Nothing fit (check the log)."); return; }
				SpawnOne(r, p);
				Say(t, $"Dropped {Tiers.GetLabel(r.Magic.Tier)} {r.Prefab.name} ({DataStore.Loot.BiomeAt(step)}, {stars} stars).");
			});

			Register("lla-tier", "<Uncommon|Magic|Rare|Epic|Legendary|Unique> [biome] - Legendary Loot: spawn an item of a tier", (a, t) =>
			{
				Player p = Player.m_localPlayer;
				if (p == null || a.Length < 2 || !Enum.TryParse(a[1], true, out Tier tier) || tier == Tier.None)
				{
					Say(t, "Usage: lla-tier <Uncommon|Magic|Rare|Epic|Legendary|Unique> [biome]");
					return;
				}
				int step = ParseBiome(a, 2, p.transform.position);
				for (int attempt = 0; attempt < 50; attempt++)
				{
					var pool = ItemPool.Get(step, UnityEngine.Random.value < 0.5f);
					if (pool == null || pool.Count == 0) continue;
					ItemPool.Entry e = pool[UnityEngine.Random.Range(0, pool.Count)];
					var temp = new ItemDrop.ItemData { m_shared = e.Shared };
					UniqueDef u = tier == Tier.Unique ? AffixRoller.PickUnique(e.Shared, step) : null;
					if (tier == Tier.Unique && u == null) continue;
					MagicItem magic = AffixRoller.Roll(temp, tier, u);
					if (magic == null) continue;
					SpawnOne(new LootRoller.Result { Prefab = e.Prefab, Magic = magic }, p);
					Say(t, $"Spawned {Tiers.GetLabel(tier)} {e.Prefab.name}.");
					return;
				}
				Say(t, tier == Tier.Unique
					? "No Unique fits here. Turn on 'Unfinished Uniques (testing)' in the config to test Uniques before their powers are coded."
					: "Couldn't find an item that fits.");
			});

			Register("lla-unique", "<UniqueId> [item] - Legendary Loot: spawn a specific Unique on a random item it fits (or one whose name contains [item])", (a, t) =>
			{
				Player p = Player.m_localPlayer;
				UniqueDef u = null;
				if (p == null || a.Length < 2 || (u = FindUnique(a[1])) == null)
				{
					Say(t, "Usage: lla-unique <UniqueId> [item], e.g. lla-unique jarlslayer, or lla-unique thunderstroke spear. IDs are the names without spaces or punctuation; capitals don't matter.");
					return;
				}
				// Pick at random among every item that fits and could really drop with it (optionally only items whose
				// name contains the second word, e.g. "lla-unique thunderstroke spear").
				string filter = a.Length >= 3 ? a[2] : null;
				int minStep = string.IsNullOrEmpty(u.MinBiome) || u.MinBiome == "Any" ? 0 : Mathf.Max(0, DataStore.Loot.BiomeStep(u.MinBiome));
				var fits = new List<ItemPool.Entry>();
				var seen = new HashSet<string>();
				for (int step = DataStore.Loot.BiomeOrder.Count - 1; step >= 0; step--)
				{
					foreach (bool weapons in new[] { true, false })
					{
						var pool = ItemPool.Get(step, weapons);
						if (pool == null) continue;
						foreach (ItemPool.Entry e in pool)
						{
							if (!seen.Add(e.Prefab.name)) continue;
							if (!AffixRoller.Fits(u.AllowedOn, u.AllowedSkills, u.RequiredSkills, e.Category, ItemClassifier.SkillName(e.Shared.m_skillType)) || !u.AllowsItem(e.Shared)) continue;
							if (e.BiomeStep < minStep) continue; // same rule as real drops: only items from its biome or later
							if (filter != null && e.Prefab.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
							    && Localization.instance.Localize(e.Shared.m_name).IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
							fits.Add(e);
						}
					}
				}
				while (fits.Count > 0)
				{
					int i = UnityEngine.Random.Range(0, fits.Count);
					ItemPool.Entry e = fits[i];
					fits.RemoveAt(i);
					var temp = new ItemDrop.ItemData { m_shared = e.Shared };
					MagicItem magic = AffixRoller.Roll(temp, Tier.Unique, u);
					if (magic == null) continue;
					SpawnOne(new LootRoller.Result { Prefab = e.Prefab, Magic = magic }, p);
					bool works = Effects.UniqueEffects.IsImplemented(u.Id) && Powers.PowerStore.HasUnique(u.Id);
					Say(t, $"Spawned {u.Name} on {e.Prefab.name}." + (works ? "" : " Its power isn't coded yet, so it does nothing."));
					return;
				}
				if (filter != null) { Say(t, $"No item matching '{filter}' fits {u.Name}."); return; }
				Say(t, $"No item in the loot pool fits {u.Name}.");
			});

			Register("lla-affix", "<affix> [tier] - Legendary Loot: spawn an item that has this affix (default tier Legendary)", (a, t) =>
			{
				Player p = Player.m_localPlayer;
				AffixDef def = a.Length >= 2 ? FindAffix(a[1]) : null;
				if (p == null || def == null)
				{
					Say(t, "Usage: lla-affix <affix> [tier], e.g. lla-affix echo or lla-affix explosivearrows rare. " +
					                       "Use the affix ID or its name; capitals, spaces and punctuation don't matter.");
					return;
				}
				Tier tier = Tier.Legendary;
				if (a.Length >= 3 && (!Enum.TryParse(a[2], true, out tier) || tier == Tier.None || tier == Tier.Unique))
				{
					Say(t, "Tier must be Uncommon, Magic, Rare, Epic or Legendary.");
					return;
				}
				if (def.RangeFor(tier) == null)
				{
					Say(t, $"{def.Id} can't roll at {tier}.");
					return;
				}
				for (int step = DataStore.Loot.BiomeOrder.Count - 1; step >= 0; step--)
				{
					foreach (bool weapons in new[] { true, false })
					{
						var pool = ItemPool.Get(step, weapons);
						if (pool == null) continue;
						foreach (ItemPool.Entry e in pool)
						{
							if (!AffixRoller.Fits(def.AllowedOn, def.AllowedSkills, def.RequiredSkills, e.Category, ItemClassifier.SkillName(e.Shared.m_skillType))) continue;
							var temp = new ItemDrop.ItemData { m_shared = e.Shared };
							MagicItem magic = AffixRoller.Roll(temp, tier) ?? new MagicItem { Tier = tier };
							// Make sure the asked-for affix is on it: replace the last rolled affix if it wasn't picked.
							bool has = magic.Affixes.Exists(a => string.Equals(a.Id, def.Id, StringComparison.OrdinalIgnoreCase));
							if (!has)
							{
								var forced = new RolledAffix(def.Id, AffixRoller.RollValue(def, tier));
								if (magic.Affixes.Count > 0) magic.Affixes[magic.Affixes.Count - 1] = forced;
								else magic.Affixes.Add(forced);
							}
							SpawnOne(new LootRoller.Result { Prefab = e.Prefab, Magic = magic }, p);
							bool works = Effects.AffixEffects.IsImplemented(def.Id);
							Say(t, $"Spawned {Tiers.GetLabel(tier)} {e.Prefab.name} with {def.Id}." + (works ? "" : " That affix isn't coded yet, so it does nothing."));
							return;
						}
					}
				}
				Say(t, $"No item in the loot pool can carry {def.Id}.");
			});

			// Numbers in order: stars, how many, random affixes. Words: the creature, and any affixes by name. Any order.
			new Terminal.ConsoleCommand("lla-enemy", "<stars> [count] [random affixes] <creature> [affix...] - Legendary Loot: spawn enemies, " +
			                                         "e.g. lla-enemy 4 3 greydwarf fire-infused (three 4-star Fire-infused Greydwarfs) or lla-enemy 4 3 2 greydwarf (three with 2 random affixes)", args =>
			{
				Terminal t = args.Context;
				Player p = Player.m_localPlayer;
				if (p == null) return;
				var numbers = new List<int>();
				var words = new List<string>();
				for (int i = 1; i < args.Args.Length; i++)
				{
					if (int.TryParse(args.Args[i], out int n)) numbers.Add(n);
					else words.Add(args.Args[i]);
				}
				GameObject prefab = null;
				var affixes = new List<string>();
				foreach (string w in words)
				{
					string found = null;
					foreach (string k in DataStore.Enemies.Effects.Keys) if (Normalize(k) == Normalize(w)) found = k;
					foreach (string k in DataStore.Enemies.Infusions.Keys) if (Normalize(k) == Normalize(w)) found = k;
					if (found != null) { affixes.Add(found); continue; }
					if (prefab == null)
						foreach (GameObject go in ZNetScene.instance.m_prefabs)
							if (go != null && go.GetComponent<Character>() != null && Normalize(go.name) == Normalize(w)) { prefab = go; break; }
					if (prefab == null) { t.AddString($"'{w}' isn't a creature or an enemy affix."); return; }
				}
				if (prefab == null || numbers.Count == 0)
				{
					t.AddString("Usage: lla-enemy <stars> [count] [random affixes] <creature> [affix...], e.g. lla-enemy 4 3 greydwarf fire-infused, or lla-enemy 4 3 2 greydwarf");
					return;
				}
				int stars = Mathf.Clamp(numbers[0], 0, 10);
				int count = numbers.Count > 1 ? Mathf.Clamp(numbers[1], 1, 30) : 1;
				int random = numbers.Count > 2 ? Mathf.Clamp(numbers[2], 0, 6) : 0;
				var names = new List<string>();
				for (int i = 0; i < count; i++)
				{
					var these = new List<string>(affixes);
					// Random affixes: any effect or infusion not already on it (one infusion at most, like natural rolls).
					var pool = new List<string>(DataStore.Enemies.Effects.Keys);
					bool hasInfusion = these.Exists(x => DataStore.Enemies.Infusions.ContainsKey(x));
					if (!hasInfusion) pool.AddRange(DataStore.Enemies.Infusions.Keys);
					pool.RemoveAll(these.Contains);
					for (int k = 0; k < random && pool.Count > 0; k++)
					{
						string pick = pool[UnityEngine.Random.Range(0, pool.Count)];
						these.Add(pick);
						pool.Remove(pick);
						if (DataStore.Enemies.Infusions.ContainsKey(pick)) pool.RemoveAll(x => DataStore.Enemies.Infusions.ContainsKey(x));
					}
					float ang = i * Mathf.PI * 2f / count;
					Vector3 at = p.transform.position + p.transform.forward * 5f + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (count > 1 ? 2f : 0f) + Vector3.up;
					GameObject spawned = UnityEngine.Object.Instantiate(prefab, at, Quaternion.identity);
					Character c = spawned.GetComponent<Character>();
					c.SetLevel(stars + 1);
					ZDO zdo = c.m_nview.GetZDO();
					zdo.Set(Enemies.EnemyScaling.KeyStars, stars + 1); // already rolled: keep these
					zdo.Set(Enemies.EnemyScaling.KeyAffixes, string.Join(",", these));
					names.Add(these.Count > 0 ? string.Join(" ", these) : "plain");
				}
				t.AddString($"Spawned {count} {stars}-star {prefab.name}: {string.Join("; ", names)}.");
			}, isCheat: true);

			new Terminal.ConsoleCommand("lla-uniqueenemy", "<creature> [stars] [boss affix] [affix] | timer [seconds] - Legendary Loot: spawn a unique enemy, " +
			                                          "e.g. lla-uniqueenemy greydwarf 2 twin quick; lla-uniqueenemy timer shows or sets the overworld timer", args =>
			{
				Terminal t = args.Context;
				Player p = Player.m_localPlayer;
				if (p == null) return;
				if (args.Args.Length > 1 && args.Args[1].Equals("timer", System.StringComparison.OrdinalIgnoreCase))
				{
					if (args.Args.Length > 2 && float.TryParse(args.Args[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sec))
						Enemies.UniqueEnemies.SetTimer(sec);
					t.AddString(Enemies.UniqueEnemies.Ready
						? "Unique enemy ready: the next creature that spawns naturally within range becomes one."
						: $"Next unique enemy in {Enemies.UniqueEnemies.MinutesLeft:0.0} minutes outside bases and dungeons" +
						  (Enemies.UniqueEnemies.Paused(p) ? " (paused here)." : "."));
					return;
				}
				GameObject prefab = null;
				int stars = -1;
				string boss = null, normal = null;
				for (int i = 1; i < args.Args.Length; i++)
				{
					string w = args.Args[i];
					if (int.TryParse(w, out int n)) { stars = Mathf.Clamp(n, 0, 10); continue; }
					string found = null;
					foreach (string k in DataStore.Enemies.BossAffixes.Keys) if (Normalize(k) == Normalize(w)) found = k;
					if (found != null) { boss = found; continue; }
					foreach (string k in DataStore.Enemies.Effects.Keys) if (Normalize(k) == Normalize(w)) found = k;
					foreach (string k in DataStore.Enemies.Infusions.Keys) if (Normalize(k) == Normalize(w)) found = k;
					if (found != null) { normal = found; continue; }
					if (prefab == null)
						foreach (GameObject go in ZNetScene.instance.m_prefabs)
							if (go != null && go.GetComponent<Character>() != null && Normalize(go.name) == Normalize(w)) { prefab = go; break; }
					if (prefab == null) { t.AddString($"'{w}' isn't a creature or an enemy affix."); return; }
				}
				if (prefab == null)
				{
					t.AddString("Usage: lla-uniqueenemy <creature> [stars] [boss affix] [affix], e.g. lla-uniqueenemy greydwarf 2 twin quick; or lla-uniqueenemy timer [seconds]");
					return;
				}
				t.AddString(Enemies.UniqueEnemies.DebugSpawn(prefab, stars, boss, normal));
			}, isCheat: true);

			new Terminal.ConsoleCommand("lla-boss", "<boss> [stars] [affix...] - Legendary Loot: spawn a boss with stars and affixes, " +
			                                       "e.g. lla-boss eikthyr 2 twin quick fire-infused (no stars or affixes: it rolls like a natural one)", args =>
			{
				Terminal t = args.Context;
				Player p = Player.m_localPlayer;
				if (p == null) return;
				GameObject prefab = null;
				int stars = -1;
				var affixes = new List<string>();
				for (int i = 1; i < args.Args.Length; i++)
				{
					string w = args.Args[i];
					if (int.TryParse(w, out int n)) { stars = Mathf.Clamp(n, 0, 5); continue; }
					string found = null;
					foreach (string k in DataStore.Enemies.BossAffixes.Keys) if (Normalize(k) == Normalize(w)) found = k;
					foreach (string k in DataStore.Enemies.Effects.Keys) if (Normalize(k) == Normalize(w)) found = k;
					foreach (string k in DataStore.Enemies.Infusions.Keys) if (Normalize(k) == Normalize(w)) found = k;
					if (found != null) { affixes.Add(found); continue; }
					if (prefab == null)
						foreach (GameObject go in ZNetScene.instance.m_prefabs)
						{
							Character ch = go != null ? go.GetComponent<Character>() : null;
							if (ch != null && ch.m_boss && Normalize(go.name) == Normalize(w)) { prefab = go; break; }
						}
					if (prefab == null) { t.AddString($"'{w}' isn't a boss or an enemy affix (bosses: Eikthyr, gd_king, Bonemass, Dragon, GoblinKing, SeekerQueen, Fader)."); return; }
				}
				if (prefab == null) { t.AddString("Usage: lla-boss <boss> [stars] [affix...], e.g. lla-boss eikthyr 2 twin quick fire-infused"); return; }
				Vector3 at = p.transform.position + p.transform.forward * 12f + Vector3.up;
				GameObject spawned = UnityEngine.Object.Instantiate(prefab, at, Quaternion.identity);
				Character c = spawned.GetComponent<Character>();
				if (stars < 0 && affixes.Count == 0) { t.AddString($"Spawned {prefab.name} (rolled like a natural one)."); return; }
				ZDO zdo = c.m_nview.GetZDO();
				zdo.Set(Enemies.EnemyScaling.KeyStars, Mathf.Max(0, stars) + 1);
				zdo.Set(Enemies.EnemyScaling.KeyAffixes, string.Join(",", affixes));
				zdo.Set(Enemies.EnemyScaling.KeyBoss, true);
				Enemies.BossScaling.ApplyHealth(c);
				t.AddString($"Spawned a {Mathf.Max(0, stars)}-star {prefab.name}: {(affixes.Count > 0 ? string.Join(" ", affixes) : "no affixes")}.");
			}, isCheat: true);

			new Terminal.ConsoleCommand("lla-locations", "[text] - Legendary Loot: list this world's location names (dungeons, camps...) containing the text", args =>
			{
				string filter = args.Args.Length > 1 ? args.Args[1] : "";
				string list = Vendors.QuestBoard.ListLocations(filter);
				foreach (string line in Chunks(list, 900)) args.Context.AddString(line);
				Plugin.Log.LogInfo("lla-locations " + filter + ": " + list);
			}, isCheat: true);

			new Terminal.ConsoleCommand("lla-delve", "- Legendary Loot: inside a dungeon, place a delve chest at the deepest spot and say what it found", args =>
			{
				Player p = Player.m_localPlayer;
				if (p == null) return;
				if (!p.InInterior()) { args.Context.AddString("Go inside a dungeon first."); return; }
				bool ok = Vendors.QuestBoard.PlaceChest(p, "TreasureChest_forestcrypt", "test", out GameObject chest, out string report);
				if (ok) LightShaft.Mark(chest, Tier.Legendary);
				args.Context.AddString((ok ? "Placed: " : "Failed: ") + report);
				Plugin.Log.LogInfo("lla-delve: " + report);
			}, isCheat: true);

			new Terminal.ConsoleCommand("lla-event", "[Horde|Champion|Ambush|Shrine|Defend|Boss] - Legendary Loot: list world events, or place one 30 m ahead (host or single player)", args =>
			{
				if (args.Args.Length < 2) { args.Context.AddString(Events.WorldEvents.DebugList()); return; }
				args.Context.AddString(Events.WorldEvents.DebugSpawn(args.Args[1]));
			}, isCheat: true);

			new Terminal.ConsoleCommand("lla-hunted", "[apex|normal] - Legendary Loot: start a Hunted event on you now", args =>
			{
				args.Context.AddString(Events.Hunted.Debug(args.Args.Length > 1 ? args.Args[1] : null));
			}, isCheat: true);

			new Terminal.ConsoleCommand("lla-kill100", "[biome] [stars] - Legendary Loot: simulate 100 kills and count the drops", args =>
			{
				Player p = Player.m_localPlayer;
				if (p == null) return;
				int step = ParseBiome(args.Args, 1, p.transform.position);
				int stars = args.Args.Length > 2 && int.TryParse(args.Args[2], out int s) ? Mathf.Clamp(s, 0, 5) : 0;
				var counts = new int[7];
				int drops = 0;
				for (int i = 0; i < 100; i++)
				{
					var r = LootRoller.Roll(new LootRoller.Context { BiomeStep = step, Stars = stars, Source = "kill100" });
					if (r == null) continue;
					drops++;
					counts[(int)r.Magic.Tier]++;
				}
				args.Context.AddString($"{DataStore.Loot.BiomeAt(step)}, {stars} stars: {drops} drops from 100 kills. " +
				                       $"Green {counts[1]}, Blue {counts[2]}, Yellow {counts[3]}, Purple {counts[4]}, Orange {counts[5]}, Unique {counts[6]}");
			}, isCheat: true);
		}

		/// <summary>Finds an affix by id or name, ignoring capitals, spaces and punctuation ("explosive-shot" works).</summary>
		private static AffixDef FindAffix(string text)
		{
			string want = Normalize(text);
			if (want.Length == 0) return null;
			foreach (AffixDef a in DataStore.Affixes)
				if (Normalize(a.Id) == want) return a;
			foreach (AffixDef a in DataStore.Affixes)
			{
				// Names carry their value, e.g. "Explosive Shot +X%": compare without the "+X%".
				string name = Normalize((a.Name ?? "").Replace("+X%", "").Replace("X%", "").Replace("+X", ""));
				if (name == want) return a;
			}
			return null;
		}

		/// <summary>Finds a Unique by id, ignoring capitals, spaces, hyphens and apostrophes ("jarl-slayer" works).</summary>
		private static UniqueDef FindUnique(string text)
		{
			string want = Normalize(text);
			foreach (var pair in DataStore.UniqueById)
				if (Normalize(pair.Key) == want || Normalize(pair.Value.Name) == want) return pair.Value;
			return null;
		}

		private static IEnumerable<string> Chunks(string text, int size)
		{
			for (int i = 0; i < text.Length; i += size) yield return text.Substring(i, Math.Min(size, text.Length - i));
		}

		private static string Normalize(string s)
		{
			if (string.IsNullOrEmpty(s)) return "";
			var sb = new System.Text.StringBuilder();
			foreach (char c in s) if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
			return sb.ToString();
		}

		// Repeat counts -----------------------------------------------------------------------------------

		private const int MaxCount = 100;
		private static bool _quiet;
		private static string _lastMessage;
		private static int _spawned;
		private static readonly Dictionary<Tier, int> Tally = new Dictionary<Tier, int>();

		/// <summary>
		/// Registers a command that can be repeated: a number as the first word runs it that many times
		/// (lla-drop 20, lla-tier 10 rare). Repeats print one summary instead of a line per item.
		/// </summary>
		private static void Register(string name, string description, Action<string[], Terminal> body)
		{
			new Terminal.ConsoleCommand(name, "[count] " + description, args =>
			{
				string[] a = args.Args;
				int n = 1;
				if (a.Length > 1 && int.TryParse(a[1], out int c) && c > 0)
				{
					n = Mathf.Min(c, MaxCount);
					var list = new List<string>(a);
					list.RemoveAt(1);
					a = list.ToArray();
				}
				_quiet = n > 1;
				_lastMessage = null;
				_spawned = 0;
				Tally.Clear();
				try
				{
					for (int i = 0; i < n; i++) body(a, args.Context);
				}
				finally
				{
					_quiet = false;
				}
				if (n <= 1) return;
				if (_spawned == 0) { args.Context.AddString(_lastMessage ?? "Nothing spawned."); return; }
				var parts = new List<string>();
				foreach (KeyValuePair<Tier, int> kv in Tally) parts.Add($"{kv.Value} {Tiers.GetLabel(kv.Key)}");
				args.Context.AddString($"Spawned {_spawned} of {n}: {string.Join(", ", parts)}.");
			}, isCheat: true);
		}

		private static void Say(Terminal t, string message)
		{
			_lastMessage = message;
			if (!_quiet) t.AddString(message);
		}

		/// <summary>Drops an item in front of the player, scattered a little so many drops don't pile into one spot.</summary>
		private static void SpawnOne(LootRoller.Result r, Player p)
		{
			Vector2 jitter = _quiet ? UnityEngine.Random.insideUnitCircle * 2f : Vector2.zero;
			Vector3 pos = p.transform.position + p.transform.forward * 2.5f + new Vector3(jitter.x, 1f, jitter.y);
			DropPatches.Spawn(r, pos, p);
			_spawned++;
			Tally.TryGetValue(r.Magic.Tier, out int c);
			Tally[r.Magic.Tier] = c + 1;
		}

		private static int ParseBiome(string[] args, int index, Vector3 position)
		{
			if (args.Length > index)
			{
				int step = DataStore.Loot.BiomeStep(args[index]);
				if (step >= 0) return step;
			}
			return DropPatches.GetCreatureBiomeStep("", position);
		}
	}
}
