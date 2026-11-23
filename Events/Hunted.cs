using System;
using System.Collections.Generic;
using HarmonyLib;
using LegendaryLoot.Data;
using LegendaryLoot.Enemies;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LegendaryLoot.Events
{
	/// <summary>
	/// Hunted (events.yml hunted): like vanilla's "You are being hunted", but one wave and done. Each player's own game
	/// keeps a hidden clock: every few minutes (6) there's a small chance (10%) that something comes for them. At a base
	/// (a campfire, workbench or other player building nearby), indoors, at sea, asleep or dead, the roll just passes.
	/// Nothing on screen says when the next one could come.
	/// When it triggers: half the time 2-5 of the biome's normal creatures, half the time one Apex Predator from the
	/// biome's apex list (sometimes from the next biome), with +2 stars and better loot. Kill them all for a reward chest.
	/// </summary>
	public static class Hunted
	{
		public const string KeyApex = "LLA_Apex";      // on an Apex Predator
		public const string KeyHunt = "LLA_Hunt";      // on every hunter: the hunt's number in this game
		public const string KeyStars = "LLA_HuntStars"; // fixed stars + 1 (0: rolled)

		private static HuntedSettings S => DataStore.Events.Hunted ?? new HuntedSettings();

		private static GameObject _driver;
		private static float _nextRoll = -1f;
		private static HuntRun _run;
		private static int _nextId = 1;

		[HarmonyPatch(typeof(Game), nameof(Game.Start))]
		private static class Patch_Start
		{
			private static void Postfix()
			{
				_nextRoll = -1f;
				_run = null;
				if (_driver != null) return;
				_driver = new GameObject("LLA_Hunted");
				UnityEngine.Object.DontDestroyOnLoad(_driver);
				_driver.AddComponent<Driver>();
			}
		}

		private class Driver : MonoBehaviour
		{
			private float _next;

			private void Update()
			{
				if (Time.time < _next) return;
				_next = Time.time + 1f;
				try { Tick(); }
				catch (Exception ex) { Plugin.Log.LogWarning($"Hunted: {ex}"); }
			}
		}

		private static void Tick()
		{
			Player p = Player.m_localPlayer;
			if (p == null || ZNetScene.instance == null || WorldGenerator.instance == null) { _run = null; return; }
			if (_run != null)
			{
				if (_run.Update(p)) _run = null;
				return;
			}
			if (!DataStore.Events.Enabled || !S.Enabled) return;
			if (_nextRoll < 0f) { _nextRoll = Time.time + Mathf.Max(0.5f, S.Minutes) * 60f; return; }
			if (Time.time < _nextRoll) return;
			_nextRoll = Time.time + Mathf.Max(0.5f, S.Minutes) * 60f;   // the clock restarts whatever happens
			if (!CanHunt(p, out string why)) { Plugin.Debug($"[Hunted] Roll skipped: {why}."); return; }
			if (Random.value * 100f >= S.Chance) return;
			Start(p, null);
		}

		/// <summary>Not at a base, indoors, at sea, asleep, dead, or in the middle of another event.</summary>
		private static bool CanHunt(Player p, out string why)
		{
			why = null;
			Vector3 pos = p.transform.position;
			if (p.IsDead() || p.IsTeleporting() || p.InBed() || p.IsSleeping() || p.InDebugFlyMode()) why = "busy";
			else if (p.InInterior()) why = "indoors";
			else if (p.IsAttachedToShip() || p.GetControlledShip() != null || p.IsSwimming()) why = "at sea";
			else if (AtBase(pos)) why = "at a base";
			else if (RandEventSystem.HaveActiveEvent()) why = "a raid is on";
			else if (BiomeStep(pos) < 0) why = "no biome";
			return why == null;
		}

		private static bool AtBase(Vector3 pos)
		{
			float r = Mathf.Max(0f, S.BaseDistance);
			if (EffectArea.IsPointInsideArea(pos, EffectArea.Type.PlayerBase, r) != null) return true;
			if (EffectArea.IsPointInsideArea(pos, EffectArea.Type.Fire, r) != null) return true;
			var pieces = new List<Piece>();
			Piece.GetAllPiecesInRadius(pos, r, pieces);
			foreach (Piece piece in pieces) if (piece != null && piece.IsPlacedByPlayer()) return true;
			return false;
		}

		private static string BiomeName(Vector3 pos) => WorldGenerator.instance.GetBiome(pos).ToString();

		private static int BiomeStep(Vector3 pos)
		{
			string b = BiomeName(pos);
			if (string.Equals(b, "Ocean", StringComparison.OrdinalIgnoreCase)) return -1;
			int step = DataStore.Loot.BiomeStep(b);
			return step;
		}

		/// <summary>Starts a hunt now. kind: null (roll), "normal" or "apex".</summary>
		private static string Start(Player p, string kind)
		{
			Vector3 pos = p.transform.position;
			int step = Mathf.Max(0, BiomeStep(pos));
			string biome = DataStore.Loot.BiomeAt(step) ?? BiomeName(pos);

			List<HuntPick> predators = PicksFor(S.Predators, biome);
			bool apex = kind == "apex" || (kind == null && predators.Count > 0 && Random.value * 100f < S.ApexChance);
			if (apex)
			{
				// Sometimes the predator comes from the next biome up (not from biomes listed in noNextZone).
				bool noNext = S.NoNextZone.Exists(n => string.Equals(n, biome, StringComparison.OrdinalIgnoreCase));
				if (!noNext && Random.value * 100f < S.NextZoneChance)
					for (int s = step + 1; s < DataStore.Loot.BiomeOrder.Count; s++)
					{
						List<HuntPick> next = PicksFor(S.Predators, DataStore.Loot.BiomeOrder[s]);
						if (next.Count > 0) { predators = next; break; }
					}
				for (int s = step - 1; s >= 0 && predators.Count == 0; s--) predators = PicksFor(S.Predators, DataStore.Loot.BiomeOrder[s]);
				if (predators.Count == 0) return "No apex predators listed for this biome or below.";
			}

			var run = new HuntRun { Id = _nextId++, Hunted = p, Step = step, Biome = biome, Apex = apex, Started = Time.time, LastSeen = Time.time };
			if (apex)
			{
				HuntPick pick = Pick(predators);
				if (pick == null || pick.Creatures.Count == 0) return "Empty predator entry.";
				string name = pick.Creatures[Random.Range(0, pick.Creatures.Count)];
				int stars = pick.MinStars >= 0 ? Random.Range(pick.MinStars, Mathf.Max(pick.MinStars, pick.MaxStars) + 1) : -1;
				Character c = run.Spawn(name, pos, true, stars);
				if (c == null) return $"Couldn't spawn {name}.";
				foreach (string w in pick.With) run.Spawn(w, c.transform.position, false, -1, 3f, 6f);
				p.Message(MessageHud.MessageType.Center, S.ApexStart);
				if (!string.IsNullOrEmpty(S.ApexSubtitle)) p.Message(MessageHud.MessageType.TopLeft, S.ApexSubtitle);
				Plugin.Debug($"[Hunted] Apex {name}{(stars >= 0 ? $" ({stars} stars)" : "")} hunts {p.GetPlayerName()} in the {biome}.");
			}
			else
			{
				HuntPick pack = Pick(PicksFor(S.Packs, biome));
				if (pack != null && pack.Creatures.Count > 0)
				{
					int count = Random.Range(Mathf.Max(1, pack.Min), Mathf.Max(pack.Min, pack.Max) + 1);
					for (int i = 0; i < count; i++)
					{
						int stars = pack.MinStars >= 0 ? Random.Range(pack.MinStars, Mathf.Max(pack.MinStars, pack.MaxStars) + 1) : -1;
						run.Spawn(pack.Creatures[Random.Range(0, pack.Creatures.Count)], pos, false, stars);
					}
				}
				else
				{
					BiomeCreatures bc = Creatures(biome);
					if (bc == null || bc.Creatures.Count == 0) return $"No creatures listed for the {biome}.";
					int count = Random.Range(Mathf.Max(1, S.MinWave), Mathf.Max(S.MinWave, S.MaxWave) + 1);
					for (int i = 0; i < count; i++) run.Spawn(bc.Creatures[Random.Range(0, bc.Creatures.Count)], pos, false);
				}
				if (run.Count == 0) return "Nothing could be spawned.";
				p.Message(MessageHud.MessageType.Center, S.Start);
				Plugin.Debug($"[Hunted] {run.Count} creatures hunt {p.GetPlayerName()} in the {biome}.");
			}
			_run = run;
			return apex ? "An Apex Predator is coming." : "You are being hunted.";
		}

		private static List<HuntPick> PicksFor(Dictionary<string, List<HuntPick>> table, string biome)
		{
			var list = new List<HuntPick>();
			if (biome != null && table != null)
				foreach (var kv in table)
					if (string.Equals(kv.Key, biome, StringComparison.OrdinalIgnoreCase) && kv.Value != null)
						foreach (HuntPick h in kv.Value) if (h != null && h.Weight > 0f && h.Creatures.Count > 0) list.Add(h);
			return list;
		}

		private static HuntPick Pick(List<HuntPick> list)
		{
			float total = 0f;
			foreach (HuntPick h in list) total += h.Weight;
			if (total <= 0f) return null;
			float r = Random.value * total;
			foreach (HuntPick h in list) { r -= h.Weight; if (r <= 0f) return h; }
			return list[list.Count - 1];
		}

		private static BiomeCreatures Creatures(string biome)
		{
			foreach (var kv in DataStore.Events.Biomes)
				if (string.Equals(kv.Key, biome, StringComparison.OrdinalIgnoreCase)) return kv.Value;
			return null;
		}

		/// <summary>lla-hunted [apex|normal]: start a hunt now (skips the clock and the base check).</summary>
		public static string Debug(string kind)
		{
			Player p = Player.m_localPlayer;
			if (p == null) return "";
			if (_run != null) return "A hunt is already on.";
			if (kind != null) kind = kind.ToLowerInvariant();
			if (kind != null && kind != "apex" && kind != "normal") return "Use: lla-hunted [apex|normal]";
			return Start(p, kind);
		}

		/// <summary>One hunt in this game: its creatures, who fought, and the end.</summary>
		private class HuntRun
		{
			public int Id, Step;
			public string Biome;
			public bool Apex;
			public Player Hunted;
			public float Started, LastSeen;
			private readonly Dictionary<ZDOID, int> _tracked = new Dictionary<ZDOID, int>();
			private readonly Dictionary<long, string> _players = new Dictionary<long, string>();
			private int _maxStars;
			public int Count => _tracked.Count;

			public Character Spawn(string prefabName, Vector3 around, bool apex, int stars = -1, float min = -1f, float max = -1f)
			{
				GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
				if (prefab == null) { Plugin.Log.LogWarning($"Hunted: no creature called {prefabName}."); return null; }
				if (min < 0f) min = S.SpawnMin;
				if (max < 0f) max = Mathf.Max(min, S.SpawnMax);
				Vector3 pos = around;
				for (int tries = 0; tries < 12; tries++)
				{
					float ang = Random.value * Mathf.PI * 2f, dist = Random.Range(min, max);
					pos = around + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist;
					if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(pos, out float h, 1000)) pos.y = h;
					if (ZoneSystem.instance == null || pos.y > ZoneSystem.instance.m_waterLevel + 0.5f) break;   // on land
				}
				pos.y += 0.5f;
				GameObject go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
				Character c = go.GetComponent<Character>();
				if (c == null || c.m_nview == null || !c.m_nview.IsValid()) return null;
				ZDO zdo = c.m_nview.GetZDO();
				zdo.Set(KeyHunt, Id);
				if (apex) zdo.Set(KeyApex, true);   // before its Start: the star roll adds the bonus
				if (stars >= 0) { zdo.Set(KeyStars, stars + 1); c.SetLevel(stars + 1); }   // fixed stars
				BaseAI ai = go.GetComponent<BaseAI>();
				if (ai != null) { ai.SetHuntPlayer(true); ai.SetAlerted(true); }
				MonsterAI mai = go.GetComponent<MonsterAI>();
				if (mai != null && Hunted != null) mai.m_targetCreature = Hunted;
				_tracked[zdo.m_uid] = 0;
				return c;
			}

			/// <summary>True when the hunt is over.</summary>
			public bool Update(Player p)
			{
				if (p != Hunted || p.IsDead()) { End(false, S.FailText); return true; }
				var near = new List<Player>();
				Player.GetPlayersInRange(p.transform.position, 60f, near);
				foreach (Player o in near) if (o != null && !o.IsDead()) _players[o.GetPlayerID()] = o.GetPlayerName();

				int alive = 0;
				bool close = false;
				foreach (ZDOID id in new List<ZDOID>(_tracked.Keys))
				{
					GameObject go = ZNetScene.instance.FindInstance(id);
					Character c = go != null ? go.GetComponent<Character>() : null;
					bool dead;
					if (c != null)
					{
						dead = c.IsDead() || c.GetHealth() <= 0f;
						if (!dead)
						{
							_tracked[id] = Mathf.Max(0, c.GetLevel() - 1);
							if (WorldEvents.Flat(c.transform.position, p.transform.position) < S.EscapeRange) close = true;
						}
					}
					else dead = ZDOMan.instance == null || ZDOMan.instance.GetZDO(id) == null;
					if (dead) { _maxStars = Mathf.Max(_maxStars, _tracked[id]); _tracked.Remove(id); }
					else alive++;
				}
				if (alive == 0) { Win(p); return true; }
				if (close) LastSeen = Time.time;
				if (Time.time - LastSeen > S.EscapeSeconds) { End(false, S.EscapeText); return true; }
				if (Time.time - Started > S.TimeoutMinutes * 60f) { End(false, S.EscapeText); return true; }
				return false;
			}

			private void Win(Player p)
			{
				var ev = new WorldEvent { Id = -Id, Type = "Hunted", Pos = p.transform.position, Circle = p.transform.position, Step = Step, Biome = Biome };
				int i = 0, n = Mathf.Max(1, _players.Count);
				Vector3 center = p.transform.position + p.transform.forward * 3f;
				if (_players.Count == 0) _players[p.GetPlayerID()] = p.GetPlayerName();
				foreach (var kv in _players)
				{
					float ang = i++ * Mathf.PI * 2f / n;
					Vector3 at = center + (n > 1 ? new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 2.5f : Vector3.zero);
					WorldEvents.RewardChest(ev, at, kv.Key, kv.Value, _maxStars);
				}
				p.Message(MessageHud.MessageType.Center, Apex ? S.ApexWinText : S.WinText);
				Plugin.Debug($"[Hunted] Won ({(Apex ? "apex" : "pack")}); highest star defeated {_maxStars}, {_players.Count} players.");
			}

			private void End(bool won, string text)
			{
				if (Hunted != null && !Hunted.IsDead() && !string.IsNullOrEmpty(text)) Hunted.Message(MessageHud.MessageType.Center, text);
				Plugin.Debug($"[Hunted] Ended without a win.");
			}
		}
	}
}
