using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using LegendaryLoot.Data;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LegendaryLoot.Enemies
{
	/// <summary>
	/// Unique enemies (enemies.yml "uniques"): rare elite versions of normal creatures with a gold name and glow.
	///  - Overworld: each player has a hidden 20-40 minute timer that runs while they're outside bases and dungeons.
	///    When it runs out, the next creature that spawns naturally near them becomes a unique enemy.
	///  - Dungeons and caves: 1-3 of the creature spawners inside are picked when the dungeon first spawns; their
	///    creature is a unique enemy until it's killed.
	///  - At least 2 stars (capped by biome), one boss-list affix plus one normal effect or infusion.
	///  - Loot: 2 guaranteed drops, 2x Unique chance, +1 star (DropPatches).
	/// </summary>
	public static class UniqueEnemies
	{
		public const string NameColor = "#D4AF37";
		private static readonly Color GlowColor = new Color(1f, 0.78f, 0.25f);

		private const string KeySource = "LLA_USrc";   // on a dungeon unique: its spawner
		private const string KeyPick = "LLA_UPick";    // on a dungeon spawner: 0 undecided, 1 unique, 2 normal, 3 its unique was killed
		private const string KeySummon = "LLA_SumStage";
		private const string TimerKey = "LLA.uniqueTimer";
		private const string ReflectRpc = "LLA_Reflect";
		private const string PullRpc = "LLA_Pull";

		private static EnemyFile Data => DataStore.Enemies;
		private static UniqueEnemySettings S => DataStore.Enemies.Uniques;

		// ---------- rolling (the creature's owner) ----------

		public static void Roll(Character c, ZDO zdo)
		{
			Vector3 pos = c.transform.position;
			int cap = Mathf.Max(S.MinStars, Data.UseBiomeCaps ? EnemyScaling.Cap(pos) : Data.MaxStars);
			float chance = (Data.StarChance + Data.StarChancePerBoss * EnemyScaling.BossesDefeated()) / 100f;
			int stars = S.MinStars;
			while (stars < cap && Random.value < chance) stars++;

			int step = EnemyScaling.BiomeStepAt(pos);
			var affixes = new List<string>();
			string boss = EnemyScaling.Pick(Data.BossAffixes, stars, step);
			if (boss != null) affixes.Add(boss);
			string normal = PickNormal(stars, step);
			if (normal != null) affixes.Add(normal);

			zdo.Set(EnemyScaling.KeyStars, stars + 1);
			zdo.Set(EnemyScaling.KeyAffixes, string.Join(",", affixes));
			c.SetLevel(stars + 1);
			Plugin.Debug($"[Enemies] Unique {Utils.GetPrefabName(c.gameObject)}: {stars} stars, {string.Join(" ", affixes)}");
		}

		/// <summary>One normal effect or infusion (infusions from infusionMinStars).</summary>
		internal static string PickNormal(int stars, int step)
		{
			var table = new Dictionary<string, Dictionary<string, float>>(Data.Effects);
			if (stars >= Data.InfusionMinStars)
				foreach (var kv in Data.Infusions) table[kv.Key] = kv.Value;
			return EnemyScaling.Pick(table, stars, step);
		}

		// ---------- looks (every game) ----------

		public static void Look(Character c, AffixRuntime r)
		{
			var glow = new GameObject("LLA_UniqueGlow");
			glow.transform.SetParent(c.transform, false);
			glow.transform.position = c.GetCenterPoint() + Vector3.up * 0.5f;
			Light light = glow.AddComponent<Light>();
			light.type = LightType.Point;
			light.color = GlowColor;
			light.range = 4.5f * Mathf.Max(1f, r.SizeMultiplier);
			light.intensity = 2f;
			light.shadows = LightShadows.None;
			AuraDef aura = S.Aura;
			if (aura != null && !string.IsNullOrEmpty(aura.Prefab) && !aura.Prefab.Equals("none", StringComparison.OrdinalIgnoreCase)) r.Auras.Add(aura);
			UniqueRuntime u = c.gameObject.GetComponent<UniqueRuntime>() ?? c.gameObject.AddComponent<UniqueRuntime>();
			u.Light = light;
			u.Runtime = r;
		}

		// ---------- combat ----------

		/// <summary>Its hits on you: Enraged (+physical), Elementalist (+elemental), Twin (less damage).</summary>
		public static void ScaleOutgoing(AffixRuntime from, HitData hit)
		{
			if (from.Has("Enraged"))
			{
				float m = 1f + Data.Get("Enraged", "damage", 20f) / 100f;
				hit.m_damage.m_damage *= m; hit.m_damage.m_blunt *= m; hit.m_damage.m_slash *= m;
				hit.m_damage.m_pierce *= m; hit.m_damage.m_chop *= m; hit.m_damage.m_pickaxe *= m;
			}
			if (from.Has("Elementalist"))
			{
				float m = 1f + Data.Get("Elementalist", "damage", 20f) / 100f;
				hit.m_damage.m_fire *= m; hit.m_damage.m_frost *= m; hit.m_damage.m_lightning *= m;
				hit.m_damage.m_poison *= m; hit.m_damage.m_spirit *= m;
			}
			if (from.Boss && from.Stars > 0)
				hit.ApplyModifier(BossScaling.DamageMultiplier(from.Stars));
			if (from.EventBoss)
				hit.ApplyModifier(EventBossPercent(false) / 100f);
			if (from.Twin)
			{
				// Bosses: both twins deal bosses.twinDamage percent. Unique enemies: Twin's damage setting.
				if (from.Boss) hit.ApplyModifier(Mathf.Max(0f, Data.Bosses.TwinDamage / 100f));
				else hit.ApplyModifier(Mathf.Max(0f, 1f + Data.Get("Twin", "damage", -25f) / 100f));
			}
		}

		/// <summary>On the hurt creature's owner, after the damage: Reflective, Mending, Summoner.</summary>
		public static void OnHurt(Character c, HitData hit, float before)
		{
			AffixRuntime r = EnemyScaling.Of(c);
			if (r == null || r.Affixes.Count == 0 || c.m_nview == null || !c.m_nview.IsValid() || !c.m_nview.IsOwner()) return;
			float dealt = before - c.GetHealth();
			if (dealt <= 0f) return;

			if (r.Has("Reflective") && hit.GetAttacker() is Player attacker && !attacker.IsDead()
			    && Random.value * 100f < Data.Get("Reflective", "chance", 20f))
			{
				float back = dealt * Data.Get("Reflective", "percent", 30f) / 100f;
				ZRoutedRpc.instance.InvokeRoutedRPC(attacker.GetOwner(), ReflectRpc, back);
			}

			if (c.IsDead() || c.GetHealth() <= 0f) return;

			if (r.Has("Mending"))
			{
				UniqueRuntime u = c.GetComponent<UniqueRuntime>();
				if (u != null) u.TryMend();
			}

			if (r.Has("Summoner"))
			{
				ZDO zdo = c.m_nview.GetZDO();
				float step = Mathf.Clamp(Data.Get("Summoner", "step", 33f), 5f, 95f) / 100f;
				int stage = zdo.GetInt(KeySummon);
				float lost = 1f - c.GetHealth() / Mathf.Max(1f, c.GetMaxHealth());
				while (lost >= step * (stage + 1) && step * (stage + 1) < 1f)
				{
					stage++;
					zdo.Set(KeySummon, stage);
					Summon(c, r);
				}
			}
		}

		private static void Summon(Character c, AffixRuntime r)
		{
			// Bosses call their listed adds (enemies.yml bosses.adds); anything else calls copies of itself.
			List<string> adds = null;
			if (c.IsBoss() && !Data.Bosses.Adds.TryGetValue(Utils.GetPrefabName(c.gameObject), out adds)) return;
			GameObject self = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(Utils.GetPrefabName(c.gameObject)) : null;
			int count = Mathf.Clamp((int)Data.Get("Summoner", "count", 2f), 1, 10);
			int stars = Mathf.Clamp((int)Data.Get("Summoner", "addStars", 1f), 0, 5);
			for (int i = 0; i < count; i++)
			{
				float ang = (i + Random.value * 0.5f) * Mathf.PI * 2f / count;
				Vector3 at = c.transform.position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (2f + c.GetRadius()) + Vector3.up * 0.5f;
				GameObject prefab = self;
				if (adds != null)
				{
					if (adds.Count == 0) return;
					prefab = ZNetScene.instance.GetPrefab(adds[Random.Range(0, adds.Count)]);
				}
				if (prefab == null) { Plugin.Log.LogWarning($"Summoner: no creature to call for {c.m_name}."); continue; }
				GameObject go = UnityEngine.Object.Instantiate(prefab, at, c.transform.rotation);
				Character add = go.GetComponent<Character>();
				if (add == null || add.m_nview == null || !add.m_nview.IsValid()) continue;
				ZDO zdo = add.m_nview.GetZDO();
				zdo.Set(EnemyScaling.KeyStars, stars + 1);
				zdo.Set(EnemyScaling.KeyAffixes, "");
				zdo.Set(EnemyScaling.KeyNoLoot, true);
				add.SetLevel(stars + 1);
				BaseAI ai = add.GetComponent<BaseAI>();
				if (ai != null) { ai.SetAlerted(true); ai.SetHuntPlayer(true); }
				SafeFx("fx_perfectdodge", at);
			}
			Plugin.Debug($"[Enemies] {c.m_name} summoned {count} {stars}-star adds.");
		}

		/// <summary>Twin: true while its partner is still alive (then it drops no gear).</summary>
		public static bool TwinAlive(Character c)
		{
			if (c == null || c.m_nview == null || !c.m_nview.IsValid()) return false;
			ZDOID id = c.m_nview.GetZDO().GetZDOID(EnemyScaling.KeyTwin);
			if (id == ZDOID.None) return false;
			GameObject go = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(id) : null;
			if (go != null)
			{
				Character partner = go.GetComponent<Character>();
				return partner != null && !partner.IsDead() && partner.GetHealth() > 0f;
			}
			return ZDOMan.instance != null && ZDOMan.instance.GetZDO(id) != null;
		}

		/// <summary>On the owner: a Twin that doesn't have its partner yet gets one, and both lose a quarter of their health.</summary>
		internal static void MakeTwin(Character c)
		{
			ZDO zdo = c.m_nview.GetZDO();
			if (zdo.GetZDOID(EnemyScaling.KeyTwin) != ZDOID.None) return;
			GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(Utils.GetPrefabName(c.gameObject)) : null;
			if (prefab == null) return;
			Vector3 at = c.transform.position + c.transform.right * (1.5f + c.GetRadius() * 2f) + Vector3.up * 0.3f;
			GameObject go = UnityEngine.Object.Instantiate(prefab, at, c.transform.rotation);
			Character twin = go.GetComponent<Character>();
			if (twin == null || twin.m_nview == null || !twin.m_nview.IsValid()) return;
			ZDO tz = twin.m_nview.GetZDO();
			tz.Set(EnemyScaling.KeyStars, zdo.GetInt(EnemyScaling.KeyStars));
			tz.Set(EnemyScaling.KeyAffixes, zdo.GetString(EnemyScaling.KeyAffixes));
			tz.Set(EnemyScaling.KeyUnique, zdo.GetBool(EnemyScaling.KeyUnique));
			tz.Set(EnemyScaling.KeyBoss, zdo.GetBool(EnemyScaling.KeyBoss));
			tz.Set(Events.WorldEvents.KeyEventBoss, zdo.GetBool(Events.WorldEvents.KeyEventBoss));
			if (zdo.GetBool(Events.WorldEvents.KeyEventBoss)) tz.Set(Events.WorldEvents.KeyEvent, zdo.GetInt(Events.WorldEvents.KeyEvent));
			tz.Set(EnemyScaling.KeyTwin, zdo.m_uid);
			tz.Set(EnemyScaling.KeyTwinCopy, true);
			zdo.Set(EnemyScaling.KeyTwin, tz.m_uid);
			if (!c.IsBoss()) twin.SetLevel(Mathf.Max(1, zdo.GetInt(EnemyScaling.KeyStars)));
			twin.SetupMaxHealth();
			twin.SetHealth(twin.GetMaxHealth());
			c.SetupMaxHealth();
			c.SetHealth(c.GetMaxHealth());
			Plugin.Debug($"[Enemies] Twin {c.m_name} arrived with its partner.");
		}

		internal static float EventBossPercent(bool health)
		{
			if (!DataStore.Events.Types.TryGetValue("Boss", out EventType t) || t == null) return health ? 50f : 75f;
			return Mathf.Max(1f, health ? t.HealthPercent : t.DamagePercent);
		}

		/// <summary>Twins have 25% less health; boss stars add a little.</summary>
		[HarmonyPatch(typeof(Character), nameof(Character.SetupMaxHealth))]
		private static class Patch_TwinHealth
		{
			private static void Postfix(Character __instance)
			{
				if (__instance.IsPlayer() || __instance.m_nview == null || !__instance.m_nview.IsValid() || !__instance.m_nview.IsOwner()) return;
				ZDO zdo = __instance.m_nview.GetZDO();
				float m = 1f;
				// Boss stars: a little more health each (bosses.healthPerStar).
				if (zdo.GetBool(EnemyScaling.KeyBoss)) m *= BossScaling.HealthMultiplier(Mathf.Max(0, zdo.GetInt(EnemyScaling.KeyStars) - 1));
				if (zdo.GetZDOID(EnemyScaling.KeyTwin) != ZDOID.None)
				{
					if (zdo.GetBool(EnemyScaling.KeyBoss)) m *= Mathf.Max(0.01f, Data.Bosses.TwinHealth / 100f);
					else m *= Mathf.Max(0.05f, 1f + Data.Get("Twin", "health", -25f) / 100f);
				}
				// A Boss event's boss: weaker than the real one (events.yml types.Boss.healthPercent).
				if (zdo.GetBool(Events.WorldEvents.KeyEventBoss)) m *= EventBossPercent(true) / 100f;
				// Multiplayer: more health for each extra player nearby (counted by AffixRuntime).
				int extra = zdo.GetInt(EnemyScaling.KeyPlayers);
				if (extra > 0) m *= 1f + Mathf.Max(0f, Plugin.HealthPerExtraPlayer.Value) / 100f * extra;
				if (!Mathf.Approximately(m, 1f)) __instance.SetMaxHealth(__instance.GetMaxHealth() * m);
			}
		}

		// ---------- Black Hole ----------

		private static int _solidMask;
		private static int SolidMask => _solidMask != 0 ? _solidMask
			: (_solidMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle"));

		/// <summary>On the owner: pull the nearest player in range (not already next to it) if the way is clear.</summary>
		internal static bool TryPull(Character c)
		{
			float range = Data.Get("Black Hole", "range", 20f);
			var players = new List<Player>();
			Player.GetPlayersInRange(c.transform.position, range, players);
			Player best = null;
			float bestDist = float.MaxValue;
			float melee = c.GetRadius() + 2f;
			foreach (Player p in players)
			{
				if (p == null || p.IsDead() || p.IsTeleporting()) continue;
				float d = Vector3.Distance(p.transform.position, c.transform.position);
				if (d <= melee + 1f || d >= bestDist) continue;
				if (!ClearPath(c.GetCenterPoint(), p.GetCenterPoint())) continue;
				best = p;
				bestDist = d;
			}
			if (best == null) return false;
			Vector3 dir = best.transform.position - c.transform.position;
			dir.y = 0f;
			dir = dir.sqrMagnitude > 0.01f ? dir.normalized : c.transform.forward;
			Vector3 dest = c.transform.position + dir * melee;
			if (Physics.Raycast(dest + Vector3.up * 3f, Vector3.down, out RaycastHit ground, 8f, SolidMask)) dest.y = ground.point.y + 0.1f;
			else dest.y = c.transform.position.y + 0.1f;
			ZRoutedRpc.instance.InvokeRoutedRPC(best.GetOwner(), PullRpc, dest);
			return true;
		}

		private static bool ClearPath(Vector3 from, Vector3 to)
		{
			if (Physics.Linecast(from, to, SolidMask)) return false;
			if (ZoneSystem.instance == null) return true;
			float len = Vector3.Distance(from, to);
			for (float t = 0f; t <= len; t += 1.5f)
			{
				Vector3 p = Vector3.Lerp(from, to, len > 0f ? t / len : 0f);
				if (ZoneSystem.instance.IsLava(p, false)) return false;
			}
			return true;
		}

		private static IEnumerator PullLocal(Vector3 dest)
		{
			Player p = Player.m_localPlayer;
			if (p == null || p.IsDead()) yield break;
			p.Message(MessageHud.MessageType.TopLeft, "You're pulled in!");
			if (ZNetScene.instance != null && ZNetScene.instance.GetPrefab("fx_perfectdodge") != null) Powers.Fx.Play("fx_perfectdodge", p.transform.position, 1f);
			Vector3 start = p.transform.position;
			const float duration = 0.35f;
			for (float t = 0f; t < duration; t += Time.deltaTime)
			{
				if (p == null || p.IsDead()) yield break;
				float k = t / duration;
				Vector3 pos = Vector3.Lerp(start, dest, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 1.2f;
				p.transform.position = pos;
				if (p.m_body != null) { p.m_body.position = pos; p.m_body.velocity = Vector3.zero; }
				yield return null;
			}
			if (p == null) yield break;
			p.transform.position = dest;
			if (p.m_body != null) { p.m_body.position = dest; p.m_body.velocity = Vector3.zero; }
		}

		private static void ReflectLocal(float amount)
		{
			Player p = Player.m_localPlayer;
			if (p == null || p.IsDead() || amount <= 0f) return;
			var hit = new HitData { m_point = p.GetCenterPoint(), m_hitType = HitData.HitType.Undefined };
			hit.m_damage.m_damage = amount;
			p.ApplyDamage(hit, true, false);
			p.Message(MessageHud.MessageType.TopLeft, "Damage reflected!");
		}

		[HarmonyPatch(typeof(Game), nameof(Game.Start))]
		private static class Patch_Register
		{
			private static void Postfix()
			{
				ZRoutedRpc.instance.Register<float>(ReflectRpc, (sender, amount) => ReflectLocal(amount));
				ZRoutedRpc.instance.Register<Vector3>(PullRpc, (sender, dest) => Plugin.Instance.StartCoroutine(PullLocal(dest)));
				_timerLoadedFor = null;
			}
		}

		// ---------- overworld timer (each player's own game) ----------

		private static float _left = -1f;
		private static bool _ready;
		private static float _saveAt;
		private static Player _timerLoadedFor;

		public static float MinutesLeft => _ready ? 0f : Mathf.Max(0f, _left) / 60f;
		public static bool Ready => _ready;

		public static bool Paused(Player p) =>
			p == null || p.IsDead() || p.InInterior() || p.IsTeleporting() || p.InBed()
			|| EffectArea.IsPointInsideArea(p.transform.position, EffectArea.Type.PlayerBase, 0f) != null;

		private static float NewTimer() => Random.Range(Mathf.Max(0.1f, S.MinMinutes), Mathf.Max(S.MinMinutes, S.MaxMinutes)) * 60f;

		[HarmonyPatch(typeof(Player), nameof(Player.Update))]
		private static class Patch_Timer
		{
			private static void Postfix(Player __instance)
			{
				if (__instance != Player.m_localPlayer || !S.Enabled || !Data.Enabled) return;
				if (_timerLoadedFor != __instance) LoadTimer(__instance);
				if (_ready || Paused(__instance)) return;
				_left -= Time.deltaTime;
				if (_left <= 0f)
				{
					_ready = true;
					Plugin.Debug("[Enemies] Unique enemy timer done: the next natural spawn nearby becomes a unique enemy.");
				}
				if (Time.time >= _saveAt) SaveTimer(__instance);
			}
		}

		private static void LoadTimer(Player p)
		{
			_timerLoadedFor = p;
			_ready = false;
			_left = p.m_customData != null && p.m_customData.TryGetValue(TimerKey, out string text)
			        && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : NewTimer();
			if (_left <= 0f) _ready = true;
			_saveAt = Time.time + 10f;
		}

		private static void SaveTimer(Player p)
		{
			_saveAt = Time.time + 10f;
			if (p?.m_customData != null) p.m_customData[TimerKey] = (_ready ? 0f : _left).ToString("0.#", CultureInfo.InvariantCulture);
		}

		public static void SetTimer(float seconds)
		{
			_left = Mathf.Max(0f, seconds);
			_ready = _left <= 0f;
			SaveTimer(Player.m_localPlayer);
		}

		// Natural spawns: SpawnSystem.Spawn creates one creature per call; catch it as it wakes.
		[ThreadStatic] private static bool _inSpawn;
		[ThreadStatic] private static Character _spawned;

		[HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.Spawn))]
		private static class Patch_NaturalSpawn
		{
			private static void Prefix(bool eventSpawner)
			{
				_inSpawn = !eventSpawner;
				_spawned = null;
			}

			private static void Postfix(Vector3 spawnPoint)
			{
				Character c = _spawned;
				_inSpawn = false;
				_spawned = null;
				try
				{
					Player me = Player.m_localPlayer;
					if (!_ready || c == null || me == null || !S.Enabled || !Data.Enabled) return;
					if (Paused(me) || Vector3.Distance(me.transform.position, spawnPoint) > S.Range) return;
					if (c.m_nview == null || !c.m_nview.IsValid() || !c.m_nview.IsOwner()) return;
					ZDO zdo = c.m_nview.GetZDO();
					if (zdo.GetInt(EnemyScaling.KeyStars) != 0 || !EnemyScaling.Eligible(c, zdo)) return;
					zdo.Set(EnemyScaling.KeyUnique, true);
					_ready = false;
					_left = NewTimer();
					SaveTimer(me);
					Plugin.Debug($"[Enemies] {Utils.GetPrefabName(c.gameObject)} spawned as a unique enemy; next in {_left / 60f:0} minutes.");
				}
				catch (Exception ex) { Plugin.Log.LogWarning($"Unique enemies: {ex.Message}"); }
			}
		}

		[HarmonyPatch(typeof(Character), nameof(Character.Awake))]
		private static class Patch_Awake
		{
			private static void Postfix(Character __instance)
			{
				if (_inSpawn && _spawned == null && !__instance.IsPlayer()) _spawned = __instance;
			}
		}

		// ---------- dungeons ----------

		[HarmonyPatch(typeof(CreatureSpawner), nameof(CreatureSpawner.Spawn))]
		private static class Patch_DungeonSpawn
		{
			private static void Postfix(CreatureSpawner __instance, ZNetView __result)
			{
				try
				{
					if (__result == null || !S.Enabled || !Data.Enabled || __instance.m_nview == null || !__instance.m_nview.IsValid()) return;
					if (!Character.InInterior(__instance.transform.position)) return;
					ZDO sz = __instance.m_nview.GetZDO();
					if (sz.GetInt(KeyPick) == 0) Decide(__instance);
					if (sz.GetInt(KeyPick) != 1) return;
					Character c = __result.GetComponent<Character>();
					if (c == null || !__result.IsValid()) return;
					ZDO zdo = __result.GetZDO();
					if (!EnemyScaling.Eligible(c, zdo)) return;
					zdo.Set(EnemyScaling.KeyUnique, true);
					zdo.Set(KeySource, sz.m_uid);
				}
				catch (Exception ex) { Plugin.Log.LogWarning($"Unique enemies (dungeon): {ex.Message}"); }
			}
		}

		/// <summary>Picks 1-3 spawners of this dungeon (the same picks on every game: seeded by the world and the dungeon's zone).</summary>
		private static void Decide(CreatureSpawner trigger)
		{
			var zone = ZoneSystem.GetZone(trigger.transform.position);
			var list = new List<CreatureSpawner>();
			foreach (CreatureSpawner sp in UnityEngine.Object.FindObjectsOfType<CreatureSpawner>())
			{
				if (sp == null || sp.m_nview == null || !sp.m_nview.IsValid()) continue;
				if (!Character.InInterior(sp.transform.position) || ZoneSystem.GetZone(sp.transform.position) != zone) continue;
				if (!Fits(sp)) continue;
				list.Add(sp);
			}
			list.Sort((a, b) =>
			{
				Vector3 pa = a.transform.position, pb = b.transform.position;
				int c = pa.x.CompareTo(pb.x);
				if (c == 0) c = pa.z.CompareTo(pb.z);
				return c == 0 ? pa.y.CompareTo(pb.y) : c;
			});
			int seed = (WorldGenerator.instance != null ? WorldGenerator.instance.GetSeed() : 0) ^ (zone.x * 73856093) ^ (zone.y * 19349663);
			var rng = new System.Random(seed);
			int want = Math.Min(list.Count, rng.Next(Mathf.Max(0, S.DungeonMin), Mathf.Max(S.DungeonMin, S.DungeonMax) + 1));
			for (int i = list.Count - 1; i > 0; i--)
			{
				int j = rng.Next(i + 1);
				(list[i], list[j]) = (list[j], list[i]);
			}
			for (int i = 0; i < list.Count; i++)
			{
				ZDO z = list[i].m_nview.GetZDO();
				if (z.GetInt(KeyPick) != 0) continue;
				z.Set(KeyPick.GetStableHashCode(), i < want ? 1 : 2, true);
			}
			ZDO tz = trigger.m_nview.GetZDO();
			if (tz.GetInt(KeyPick) == 0) tz.Set(KeyPick.GetStableHashCode(), 2, true);
			Plugin.Debug($"[Enemies] Dungeon at {zone}: {want} unique enemies among {list.Count} spawners.");
		}

		private static bool Fits(CreatureSpawner sp)
		{
			GameObject prefab = sp.m_creaturePrefab;
			Character c = prefab != null ? prefab.GetComponent<Character>() : null;
			if (c == null || c.m_boss) return false;
			string name = prefab.name;
			return !name.StartsWith("LLA_", StringComparison.Ordinal) && !Data.ExcludedSet.Contains(name);
		}

		/// <summary>A dungeon unique that dies stays dead: its spawner goes back to spawning normal creatures.</summary>
		[HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
		private static class Patch_Death
		{
			private static void Prefix(Character __instance)
			{
				if (__instance.IsPlayer() || __instance.m_nview == null || !__instance.m_nview.IsValid()) return;
				ZDO zdo = __instance.m_nview.GetZDO();
				if (!zdo.GetBool(EnemyScaling.KeyUnique)) return;
				ZDOID src = zdo.GetZDOID(KeySource);
				if (src == ZDOID.None || ZDOMan.instance == null) return;
				ZDO sz = ZDOMan.instance.GetZDO(src);
				sz?.Set(KeyPick.GetStableHashCode(), 3, true);
			}
		}

		/// <summary>Plays one of the game's effects for everyone, only if it exists (no fallback effect).</summary>
		internal static void SafeFx(string name, Vector3 at)
		{
			if (ZNetScene.instance != null && ZNetScene.instance.GetPrefab(name) != null) Powers.Fx.Broadcast(name, at, 1f);
		}

		// ---------- debug ----------

		/// <summary>lla-uniqueenemy: spawns a unique enemy. Missing affixes are rolled like a natural unique.</summary>
		public static string DebugSpawn(GameObject prefab, int stars, string boss, string normal)
		{
			Player p = Player.m_localPlayer;
			Vector3 at = p.transform.position + p.transform.forward * 6f + Vector3.up;
			GameObject go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.identity);
			Character c = go.GetComponent<Character>();
			ZDO zdo = c.m_nview.GetZDO();
			zdo.Set(EnemyScaling.KeyUnique, true);
			if (stars < 0 && boss == null && normal == null) return $"Spawned a unique {prefab.name} (rolled like a natural one).";
			if (stars < 0) stars = Mathf.Max(S.MinStars, EnemyScaling.Cap(at));
			int step = EnemyScaling.BiomeStepAt(at);
			boss = boss ?? EnemyScaling.Pick(Data.BossAffixes, stars, step);
			normal = normal ?? PickNormal(stars, step);
			var affixes = new List<string>();
			if (boss != null) affixes.Add(boss);
			if (normal != null) affixes.Add(normal);
			zdo.Set(EnemyScaling.KeyStars, stars + 1);
			zdo.Set(EnemyScaling.KeyAffixes, string.Join(",", affixes));
			c.SetLevel(stars + 1);
			return $"Spawned a {stars}-star unique {prefab.name}: {string.Join(" ", affixes)}.";
		}
	}

	/// <summary>
	/// The gold coat: each material on the creature (and the gear it wears) is tinted gold and made metallic and smooth,
	/// where its shader allows. Re-checked for a while, since star looks and equipment swap materials after spawning.
	/// </summary>
	public static class GoldCoat
	{
		private static readonly HashSet<string> Logged = new HashSet<string>();
		private static readonly int Color = Shader.PropertyToID("_Color");
		private static readonly int MainTex = Shader.PropertyToID("_MainTex");
		private static readonly int BumpMap = Shader.PropertyToID("_BumpMap");
		private static readonly int Metallic = Shader.PropertyToID("_Metallic");
		private static readonly int Glossiness = Shader.PropertyToID("_Glossiness");
		private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
		private static readonly int Emission = Shader.PropertyToID("_EmissionColor");
		private static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
		private static Texture2D _shimmer;
		private static string _shimmerFor;

		/// <summary>Diagonal bright bands on a faint floor (the floor matches the base emission).</summary>
		private static Texture2D ShimmerTexture(UniqueLook look)
		{
			string key = $"{look.ShimmerBands}|{look.Emission}|{look.Shimmer}";
			if (_shimmer != null && _shimmerFor == key) return _shimmer;
			const int size = 128;
			var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "LLA_GoldShimmer" };
			float total = Mathf.Max(0.01f, look.Emission + look.Shimmer);
			float floor = look.Emission / total;
			int bands = Mathf.Max(1, look.ShimmerBands);
			var px = new Color[size * size];
			for (int y = 0; y < size; y++)
			for (int x = 0; x < size; x++)
			{
				float t = (x + y) / (float)size * bands;            // diagonal
				float wave = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f);
				float band = Mathf.Pow(wave, 10f);                  // thin bright band
				float v = Mathf.Lerp(floor, 1f, band);
				px[y * size + x] = new Color(v, v, v, 1f);
			}
			tex.SetPixels(px);
			tex.Apply(true, false);
			_shimmer = tex;
			_shimmerFor = key;
			return tex;
		}

		private static Texture2D _coat;
		private static string _coatFile;

		/// <summary>textures/uniquecoat.png (or the look's texture), made tileable with mipmaps. Null if there's none.</summary>
		private static Texture2D CoatTexture(UniqueLook look)
		{
			string file = look.Texture ?? "";
			if (_coatFile == file) return _coat;
			_coatFile = file;
			_coat = null;
			if (file.Length == 0) return null;
			Sprite sprite = Weathering.LoadOverride(file, 0);
			if (sprite == null) { Plugin.Debug($"[Enemies] No {file}; unique enemies use the shimmer bands."); return null; }
			Texture2D src = sprite.texture;
			try
			{
				var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, true)
					{ wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = "LLA_UniqueCoat" };
				tex.SetPixels32(src.GetPixels32());
				tex.Apply(true, false);
				_coat = tex;
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning($"{file}: {ex.Message}");
				src.wrapMode = TextureWrapMode.Repeat;
				_coat = src;
			}
			return _coat;
		}

		/// <summary>Slides your tile, or the shimmer bands (Standard's emission map uses the main texture's offset).</summary>
		public static void Scroll(HashSet<Material> mats)
		{
			UniqueLook look = DataStore.Enemies.Uniques.Look;
			if (look == null) return;
			Vector2 offset;
			if (CoatTexture(look) != null)
				offset = new Vector2(Mathf.Repeat(Time.time * look.ScrollX, 1f), Mathf.Repeat(Time.time * look.ScrollY, 1f));
			else
			{
				if (look.Shimmer <= 0f || look.KeepTexture) return;
				float o = Mathf.Repeat(Time.time * look.ShimmerSpeed, 1f);
				offset = new Vector2(o, o * 0.5f);
			}
			foreach (Material m in mats)
				if (m != null && (m.shader == _standard || m.name == "LLA overlay")) m.mainTextureOffset = offset;
		}
		private static Shader _standard;
		private static bool _searched;

		private static Shader Standard
		{
			get
			{
				if (_searched) return _standard;
				_searched = true;
				_standard = Shader.Find("Standard");
				Plugin.Debug(_standard != null ? "[Enemies] Gold coat: using the Standard metal shader."
				                               : "[Enemies] Gold coat: Standard shader not found; using the gold tint.");
				return _standard;
			}
		}

		/// <param name="done">Materials already handled (ours, or the ones we tinted); destroyed with the creature.</param>
		public static void Apply(GameObject root, HashSet<Material> done)
		{
			UniqueLook look = DataStore.Enemies.Uniques.Look;
			if (look == null || !look.Enabled || root == null) return;
			if (!ColorUtility.TryParseHtmlString(look.Color ?? "#FFC23D", out Color gold)) gold = new Color(1f, 0.76f, 0.24f);
			bool metal = !string.Equals(look.Style, "tint", StringComparison.OrdinalIgnoreCase) && Standard != null;
			foreach (Renderer rend in root.GetComponentsInChildren<Renderer>(true))
			{
				if (!(rend is SkinnedMeshRenderer) && !(rend is MeshRenderer)) continue;
				if (metal) Metal(rend, gold, look, done);
				else Tint(rend, gold, look, done);
			}
		}

		/// <summary>Swaps each material for a gold Standard-shader metal, keeping its normal map (and texture if asked).</summary>
		private static void Metal(Renderer rend, Color gold, UniqueLook look, HashSet<Material> done)
		{
			Material[] mats = rend.sharedMaterials;
			bool changed = false;
			for (int i = 0; i < mats.Length; i++)
			{
				Material src = mats[i];
				if (src == null || done.Contains(src)) continue;
				var m = new Material(Standard) { name = src.name + " (LLA gold)" };
				Texture2D coat = CoatTexture(look);
				if (coat != null)
				{
					// Your seamless tile, as the surface and (its bright parts) as a glow; Scroll slides it.
					Color tone = look.TextureGold ? gold : UnityEngine.Color.white;
					m.SetTexture(MainTex, coat);
					m.mainTextureScale = Vector2.one * Mathf.Max(0.01f, look.TextureTiling);
					if (look.KeepDetail && src.HasProperty(BumpMap) && src.GetTexture(BumpMap) != null)
					{
						m.SetTexture(BumpMap, src.GetTexture(BumpMap));
						m.EnableKeyword("_NORMALMAP");
					}
					m.SetColor(Color, tone);
					m.SetFloat(Metallic, Mathf.Clamp01(look.Metallic));
					m.SetFloat(Glossiness, Mathf.Clamp01(look.Smoothness));
					m.EnableKeyword("_EMISSION");
					m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
					m.SetTexture(EmissionMap, coat);
					m.SetColor(Emission, tone * Mathf.Max(0f, look.TextureGlow + look.Emission));
					m.renderQueue = src.renderQueue;
					done.Add(m);
					mats[i] = m;
					changed = true;
					continue;
				}
				if (look.KeepTexture && src.HasProperty(MainTex) && src.GetTexture(MainTex) != null)
				{
					m.SetTexture(MainTex, src.GetTexture(MainTex));
					m.SetTextureScale(MainTex, src.GetTextureScale(MainTex));
					m.SetTextureOffset(MainTex, src.GetTextureOffset(MainTex));
				}
				if (look.KeepDetail && src.HasProperty(BumpMap) && src.GetTexture(BumpMap) != null)
				{
					m.SetTexture(BumpMap, src.GetTexture(BumpMap));
					m.EnableKeyword("_NORMALMAP");
				}
				m.SetColor(Color, gold);
				m.SetFloat(Metallic, Mathf.Clamp01(look.Metallic));
				m.SetFloat(Glossiness, Mathf.Clamp01(look.Smoothness));
				if (look.Emission > 0f || look.Shimmer > 0f)
				{
					m.EnableKeyword("_EMISSION");
					m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
					if (look.Shimmer > 0f)
					{
						// Bright bands over a faint base glow; they slide when the main texture offset scrolls.
						m.SetTexture(EmissionMap, ShimmerTexture(look));
						m.SetColor(Emission, gold * (look.Emission + look.Shimmer));
					}
					else m.SetColor(Emission, gold * look.Emission);
				}
				m.renderQueue = src.renderQueue;
				done.Add(m);
				mats[i] = m;
				changed = true;
			}
			// Your tile as a glowing layer: an extra material draws the mesh again with a particle shader.
			Material overlay = OverlayMaterial(look, gold);
			if (overlay != null && !HasOverlay(mats))
			{
				var m = new Material(overlay) { name = "LLA overlay" };
				done.Add(m);
				var list = new List<Material>(mats) { m };
				mats = list.ToArray();
				changed = true;
			}
			if (changed) rend.sharedMaterials = mats;
		}

		private static bool HasOverlay(Material[] mats)
		{
			foreach (Material m in mats) if (m != null && m.name == "LLA overlay") return true;
			return false;
		}

		private static Shader _overlayShader;
		private static bool _overlaySearched;
		private static Material _overlayBase;
		private static string _overlayFor;

		/// <summary>An additive particle shader the game already uses (fire, magic), so it's never stripped.</summary>
		private static Shader OverlayShader
		{
			get
			{
				if (_overlaySearched) return _overlayShader;
				_overlaySearched = true;
				string[] names =
				{
					"Legacy Shaders/Particles/Additive", "Particles/Additive", "Mobile/Particles/Additive",
					"Legacy Shaders/Particles/Additive (Soft)", "Particles/Additive (Soft)", "Unlit/Transparent",
				};
				foreach (string n in names)
				{
					Shader sh = Shader.Find(n);
					if (sh != null && sh.isSupported) { _overlayShader = sh; break; }
				}
				var seen = new List<string>();
				foreach (Shader sh in Resources.FindObjectsOfTypeAll<Shader>())
				{
					if (sh == null) continue;
					string n = sh.name;
					if (n.IndexOf("Additive", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0
					    || n.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) >= 0)
						seen.Add(n);
					if (_overlayShader == null && n.IndexOf("Additive", StringComparison.OrdinalIgnoreCase) >= 0 && sh.isSupported) _overlayShader = sh;
				}
				Plugin.Log.LogInfo($"Unique enemy look: Standard shader {(Standard != null ? "found" : "missing")}, overlay shader " +
				                   $"{(_overlayShader != null ? "'" + _overlayShader.name + "'" : "missing")}. Loaded shaders: {string.Join(", ", seen)}");
				return _overlayShader;
			}
		}

		private static Material OverlayMaterial(UniqueLook look, Color gold)
		{
			if (!look.Overlay || look.OverlayStrength <= 0f) return null;
			Texture2D coat = CoatTexture(look);
			Shader sh = OverlayShader;
			if (coat == null || sh == null) return null;
			string key = $"{look.Color}|{look.OverlayStrength}|{look.OverlayCutoff}|{look.TextureTiling}|{look.TextureGold}";
			if (_overlayBase != null && _overlayFor == key) return _overlayBase;
			Texture2D lines = Lines(coat, look.OverlayCutoff);
			Color tone = (look.TextureGold ? gold : UnityEngine.Color.white) * look.OverlayStrength;
			var m = new Material(sh) { name = "LLA overlay", renderQueue = 3100 };
			m.mainTexture = lines;
			m.mainTextureScale = Vector2.one * Mathf.Max(0.01f, look.TextureTiling);
			if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(tone.r * 0.5f, tone.g * 0.5f, tone.b * 0.5f, 0.5f));
			if (m.HasProperty(Color)) m.SetColor(Color, new Color(tone.r, tone.g, tone.b, 1f));
			_overlayBase = m;
			_overlayFor = key;
			return m;
		}

		/// <summary>The tile's bright lines only: darker parts go black and see-through.</summary>
		private static Texture2D Lines(Texture2D src, float cutoff)
		{
			cutoff = Mathf.Clamp(cutoff, 0f, 0.95f);
			Color32[] px = src.GetPixels32();
			for (int i = 0; i < px.Length; i++)
			{
				float g = (0.299f * px[i].r + 0.587f * px[i].g + 0.114f * px[i].b) / 255f;
				float v = Mathf.Pow(Mathf.Clamp01((g - cutoff) / (1f - cutoff)), 1.3f);
				byte b = (byte)Mathf.RoundToInt(v * 255f);
				px[i] = new Color32(b, b, b, b);
			}
			var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, true)
				{ wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = "LLA_UniqueCoatLines" };
			tex.SetPixels32(px);
			tex.Apply(true, false);
			return tex;
		}

		/// <summary>The fallback: tints the creature's own materials gold.</summary>
		private static void Tint(Renderer rend, Color gold, UniqueLook look, HashSet<Material> done)
		{
			Material[] mats = rend.materials; // this renderer's own copies
			bool changed = false;
			foreach (Material m in mats)
			{
				if (m == null || done.Contains(m)) continue;
				done.Add(m);
				changed = true;
				if (m.HasProperty(Color)) m.SetColor(Color, UnityEngine.Color.Lerp(m.GetColor(Color), gold, Mathf.Clamp01(look.Tint)));
				if (m.HasProperty(Metallic)) m.SetFloat(Metallic, Mathf.Clamp01(look.Metallic));
				if (m.HasProperty(Glossiness)) m.SetFloat(Glossiness, Mathf.Clamp01(look.Smoothness));
				if (m.HasProperty(Smoothness)) m.SetFloat(Smoothness, Mathf.Clamp01(look.Smoothness));
				if (look.Emission > 0f && m.HasProperty(Emission))
				{
					m.EnableKeyword("_EMISSION");
					m.SetColor(Emission, gold * look.Emission);
				}
				string shader = m.shader != null ? m.shader.name : "?";
				if (Logged.Add(shader))
					Plugin.Debug($"[Enemies] Gold tint on shader '{shader}': metallic {m.HasProperty(Metallic)}, gloss {m.HasProperty(Glossiness) || m.HasProperty(Smoothness)}");
			}
			if (changed) rend.materials = mats;
		}
	}

	/// <summary>A unique enemy's gold glow, plus Mending and Black Hole timing on its owner.</summary>
	public class UniqueRuntime : MonoBehaviour
	{
		public Light Light;
		public AffixRuntime Runtime;
		public bool Coat = true;
		private Character _c;
		private float _mendUntil, _mendReady, _mendPerSecond, _mendTick, _nextPull;
		private bool _twinChecked;
		private readonly HashSet<Material> _coated = new HashSet<Material>();
		private float _coatUntil, _nextCoat;

		private void Start()
		{
			_c = GetComponent<Character>();
			_nextPull = Time.time + DataStore.Enemies.Get("Black Hole", "interval", 20f);
			_coatUntil = Time.time + 10f;
		}

		private void OnDestroy()
		{
			foreach (Material m in _coated) if (m != null) Destroy(m);
		}

		public void TryMend()
		{
			if (Time.time < _mendReady || Random.value * 100f >= DataStore.Enemies.Get("Mending", "chance", 10f)) return;
			float duration = Mathf.Max(1f, DataStore.Enemies.Get("Mending", "duration", 10f));
			_mendReady = Time.time + DataStore.Enemies.Get("Mending", "cooldown", 60f);
			_mendUntil = Time.time + duration;
			_mendPerSecond = _c.GetMaxHealth() * DataStore.Enemies.Get("Mending", "percent", 10f) / 100f / duration;
			_mendTick = Time.time + 1f;
			UniqueEnemies.SafeFx("vfx_Potion_health_medium", transform.position);
		}

		private void Update()
		{
			if (Light != null) Light.intensity = 1.8f + 0.4f * Mathf.Sin(Time.time * 2f);
			if (_coated.Count > 0) GoldCoat.Scroll(_coated);
			// Gold coat: right away, then every half second for 10 s (star looks and worn gear arrive late).
			if (Coat && Time.time <= _coatUntil && Time.time >= _nextCoat)
			{
				_nextCoat = Time.time + 0.5f;
				GoldCoat.Apply(gameObject, _coated);
			}
			if (_c == null || _c.IsDead() || _c.m_nview == null || !_c.m_nview.IsValid() || !_c.m_nview.IsOwner() || Runtime == null) return;

			if (!_twinChecked)
			{
				_twinChecked = true;
				if (Runtime.Has("Twin")) UniqueEnemies.MakeTwin(_c);
			}

			if (_mendUntil > 0f && Time.time >= _mendTick)
			{
				_mendTick = Time.time + 1f;
				if (Time.time <= _mendUntil + 0.01f) _c.Heal(_mendPerSecond, true);
				else _mendUntil = 0f;
			}

			if (Runtime.Has("Black Hole") && Time.time >= _nextPull)
			{
				BaseAI ai = _c.GetComponent<BaseAI>();
				if (ai != null && !ai.IsAlerted()) { _nextPull = Time.time + 1f; return; }
				_nextPull = UniqueEnemies.TryPull(_c)
					? Time.time + DataStore.Enemies.Get("Black Hole", "interval", 20f)
					: Time.time + 1f;   // nobody to pull yet: check again shortly
			}
		}
	}
}
