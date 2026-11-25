using System.Collections.Generic;
using LegendaryLoot.Core;
using LegendaryLoot.Data;
using LegendaryLoot.Enemies;
using UnityEngine;

namespace LegendaryLoot.Events
{
	/// <summary>
	/// Runs one event in the game of the player who started it: spawns its creatures, tracks who takes part and the
	/// highest star level defeated, and decides win or fail.
	///  - Horde: waves of the biome's creatures; clear every wave.
	///  - Champion Hunt: a unique enemy with escorts; kill the champion.
	///  - Ambush: nothing until someone steps into it, then one wave springs around them; clear it.
	///  - Defend: protect the Shrine of Protection (a breakable ward) from three waves; it must survive.
	///  - Boss (rare): a weaker copy of the biome's boss (or one up to two biomes lower); kill it (and its Twin).
	/// Fails when nobody stays near it, or after 20 minutes.
	/// </summary>
	public class EventRunner : MonoBehaviour
	{
		private WorldEvent _ev;
		private EventType _type;
		private BiomeCreatures _creatures;
		private readonly Dictionary<ZDOID, int> _tracked = new Dictionary<ZDOID, int>(); // creature -> last known stars
		private readonly Dictionary<long, string> _players = new Dictionary<long, string>();
		private ZDOID _champion = ZDOID.None;
		private ZDOID _shrine = ZDOID.None;
		private float _shrineWarned = 1f;
		private int _wave = -1;
		private int _maxStars;
		private float _lastNear, _started, _next;
		private bool _sprung, _done;

		private static EventFile Data => DataStore.Events;

		public void Init(WorldEvent ev)
		{
			_ev = ev;
			Data.Types.TryGetValue(ev.Type, out _type);
			Data.Biomes.TryGetValue(ev.Biome, out _creatures);
			_type = _type ?? new EventType();
			_started = _lastNear = Time.time;
			if (_creatures == null || _creatures.Creatures.Count == 0) { Finish(false); return; }
			string start = string.IsNullOrEmpty(_type.Start) ? $"{ev.Type}!" : _type.Start;
			if (ev.Type != "Ambush") WorldEvents.Say(ev.Pos, start);
			if (ev.Type == "Champion") SpawnChampion();
			else if (ev.Type == "Boss") SpawnBoss();
			else if (ev.Type == "Defend") { if (SpawnWard()) NextWave(ev.Pos); else Finish(false); }
			else if (ev.Type != "Ambush" && ev.Type != "Defend") NextWave(ev.Pos);
			Plugin.Debug($"[Events] Running {ev.Type} #{ev.Id}.");
		}

		private void Update()
		{
			if (_done || Time.time < _next) return;
			_next = Time.time + 0.5f;

			var near = new List<Player>();
			Player.GetPlayersInRange(_ev.Pos, Data.LeaveRange, near);
			foreach (Player p in near)
			{
				if (p == null || p.IsDead()) continue;
				_players[p.GetPlayerID()] = p.GetPlayerName();
				_lastNear = Time.time;
			}
			if (Time.time - _lastNear > Data.LeaveSeconds || Time.time - _started > 20f * 60f) { Finish(false); return; }

			if (_ev.Type == "Ambush" && !_sprung)
			{
				foreach (Player p in near)
				{
					if (p == null || p.IsDead() || WorldEvents.Flat(p.transform.position, _ev.Pos) > _type.TriggerRange) continue;
					_sprung = true;
					WorldEvents.Say(_ev.Pos, string.IsNullOrEmpty(_type.Start) ? "Ambush!" : _type.Start);
					NextWave(p.transform.position);
					break;
				}
				return;
			}

			int alive = Track();
			if (_ev.Type == "Defend" && !WardCheck()) return;
			switch (_ev.Type)
			{
				case "Boss":
					AddTwins();
					if (alive == 0 && _wave >= 0) Finish(true);
					break;
				case "Champion":
					if (!_tracked.ContainsKey(_champion) && _wave >= 0) Finish(true);
					break;
				default:
					if (alive > 0) break;
					if (_wave + 1 < Mathf.Max(1, _type.Waves.Count)) { NextWave(_ev.Pos); WorldEvents.Say(_ev.Pos, $"Wave {_wave + 1} of {_type.Waves.Count}!"); }
					else Finish(true);
					break;
			}
		}

		/// <summary>Counts living event creatures; dead ones leave and their star level counts toward the reward.</summary>
		private int Track()
		{
			int alive = 0;
			foreach (ZDOID id in new List<ZDOID>(_tracked.Keys))
			{
				GameObject go = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(id) : null;
				Character c = go != null ? go.GetComponent<Character>() : null;
				bool dead;
				if (c != null)
				{
					dead = c.IsDead() || c.GetHealth() <= 0f;
					if (!dead) _tracked[id] = Mathf.Max(0, c.GetLevel() - 1);
				}
				else dead = ZDOMan.instance == null || ZDOMan.instance.GetZDO(id) == null;
				if (dead)
				{
					_maxStars = Mathf.Max(_maxStars, _tracked[id]);
					_tracked.Remove(id);
				}
				else alive++;
			}
			return alive;
		}

		private int Extra() => Mathf.RoundToInt(Mathf.Max(0, _players.Count - 1) * _type.PerExtraPlayer);

		private void NextWave(Vector3 around)
		{
			_wave++;
			int count = (_type.Waves.Count > 0 ? _type.Waves[Mathf.Clamp(_wave, 0, _type.Waves.Count - 1)] : 4) + Extra();
			if (_ev.Type == "Horde") count = Enemies.EnemyCount.Multiply(count);   // the Enemy count setting
			bool ambush = _ev.Type == "Ambush", defend = _ev.Type == "Defend";
			StaticTarget ward = null;
			if (defend)
			{
				GameObject wg = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(_shrine) : null;
				ward = wg != null ? wg.GetComponent<StaticTarget>() : null;
			}
			float dist = Mathf.Max(12f, _type.WaveDistance);
			for (int i = 0; i < count; i++)
			{
				string name = _creatures.Creatures[Random.Range(0, _creatures.Creatures.Count)];
				Character c = defend ? Spawn(name, around, dist - 4f, dist + 4f, false)
				                     : Spawn(name, around, ambush ? 7f : 10f, ambush ? 11f : 18f, false);
				MonsterAI ai = c != null ? c.GetComponent<MonsterAI>() : null;
				if (ai != null && ward != null) ai.m_targetStatic = ward;
				if (c != null && defend) c.m_nview.GetZDO().Set(Raiders.KeyTarget, _shrine);
			}
		}

		private void SpawnChampion()
		{
			_wave = 0;
			List<string> pool = _creatures.Champions.Count > 0 ? _creatures.Champions : _creatures.Creatures;
			Character champ = Spawn(pool[Random.Range(0, pool.Count)], _ev.Pos, 0f, 2f, true);
			if (champ == null) { Finish(false); return; }
			_champion = champ.m_nview.GetZDO().m_uid;
			for (int i = 0; i < Mathf.Max(0, _type.Escorts) + Extra(); i++)
				Spawn(_creatures.Creatures[Random.Range(0, _creatures.Creatures.Count)], _ev.Pos, 4f, 9f, false);
		}

		/// <summary>The Shrine of Protection: a copy of the vanilla ward that's only a target (no ward powers), sturdier
		/// in later biomes. Raiders go for it.</summary>
		private bool SpawnWard()
		{
			GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("guard_stone") : null;
			if (prefab == null) return false;
			var holder = new GameObject("LLA_WardHolder");
			holder.SetActive(false);
			GameObject go = Instantiate(prefab, holder.transform);
			foreach (PrivateArea pa in go.GetComponentsInChildren<PrivateArea>(true)) DestroyImmediate(pa);
			foreach (RandomSpawn rs in go.GetComponentsInChildren<RandomSpawn>(true)) DestroyImmediate(rs);
			Piece piece = go.GetComponent<Piece>();
			if (piece != null)
			{
				piece.m_name = "Shrine of Protection";
				piece.m_canBeRemoved = false;
				piece.m_primaryTarget = true;
				piece.m_randomTarget = true;
			}
			WearNTear wnt = go.GetComponent<WearNTear>();
			float health = 300f;
			if (_type.ShrineHealth != null && _type.ShrineHealth.TryGetValue(_ev.Biome, out float listed)) health = listed;
			else if (_type.ShrineHealth != null)
				foreach (var kv in _type.ShrineHealth)   // not listed: the highest of the biomes at or below this one
					if (DataStore.Loot.BiomeStep(kv.Key) <= _ev.Step) health = Mathf.Max(health, kv.Value);
			health = Mathf.Max(50f, health);
			if (wnt != null) { wnt.m_health = health; wnt.m_noRoofWear = false; wnt.m_noSupportWear = false; }
			Vector3 pos = _ev.Pos;
			if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(pos, out float h, 1000)) pos.y = h;
			go.transform.position = pos;
			go.transform.SetParent(null, true);
			Destroy(holder);
			ZNetView nv = go.GetComponent<ZNetView>();
			if (nv == null || !nv.IsValid()) { Destroy(go); return false; }
			nv.GetZDO().Set(WorldEvents.KeyEvent, _ev.Id);
			nv.GetZDO().Set(Raiders.KeyWardMax, health);
			_shrine = nv.GetZDO().m_uid;
			Raiders.SetupWard(go);
			var glow = new GameObject("LLA_WardGlow");
			glow.transform.SetParent(go.transform, false);
			glow.transform.localPosition = Vector3.up * 2f;
			Light l = glow.AddComponent<Light>();
			l.type = LightType.Point;
			l.color = new Color(0.35f, 0.65f, 1f);
			l.range = 10f;
			l.intensity = 2.5f;
			LightShaft.Mark(go, Tier.Magic);
			return true;
		}

		/// <summary>False (and the event fails) once the shrine is destroyed; warns at half and a quarter health.</summary>
		private bool WardCheck()
		{
			GameObject go = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(_shrine) : null;
			WearNTear wnt = go != null ? go.GetComponent<WearNTear>() : null;
			if (go == null && (ZDOMan.instance == null || ZDOMan.instance.GetZDO(_shrine) == null))
			{
				WorldEvents.Say(_ev.Pos, "The Shrine of Protection has fallen!");
				Finish(false);
				return false;
			}
			if (wnt != null)
			{
				float hp = wnt.GetHealthPercentage();
				if (hp <= 0.25f && _shrineWarned > 0.25f) { _shrineWarned = 0.25f; WorldEvents.Say(_ev.Pos, "The shrine is about to fall!"); }
				else if (hp <= 0.5f && _shrineWarned > 0.5f) { _shrineWarned = 0.5f; WorldEvents.Say(_ev.Pos, "The shrine is at half strength!"); }
			}
			return true;
		}

		/// <summary>The boss of the event's biome or of one up to tiersLower biomes below (events.yml bosses).</summary>
		private void SpawnBoss()
		{
			_wave = 0;
			var options = new List<string>();
			for (int step = Mathf.Max(0, _ev.Step - Mathf.Max(0, _type.TiersLower)); step <= _ev.Step; step++)
			{
				string biome = step < DataStore.Loot.BiomeOrder.Count ? DataStore.Loot.BiomeOrder[step] : null;
				if (biome != null && Data.Bosses.TryGetValue(biome, out string boss) && !string.IsNullOrEmpty(boss)) options.Add(boss);
			}
			if (options.Count == 0) { Finish(false); return; }
			Character c = Spawn(options[Random.Range(0, options.Count)], _ev.Pos, 0f, 3f, false);
			if (c == null) { Finish(false); return; }
			ZDO zdo = c.m_nview.GetZDO();
			zdo.Set(WorldEvents.KeyEventBoss, true);
			c.m_defeatSetGlobalKey = "";
		}

		/// <summary>A Twin boss's partner counts too: the event is won when both are down.</summary>
		private void AddTwins()
		{
			foreach (ZDOID id in new List<ZDOID>(_tracked.Keys))
			{
				ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(id) : null;
				if (zdo == null) continue;
				ZDOID twin = zdo.GetZDOID(EnemyScaling.KeyTwin);
				if (twin != ZDOID.None && !_tracked.ContainsKey(twin) && ZDOMan.instance.GetZDO(twin) != null) _tracked[twin] = 0;
			}
		}

		private Character Spawn(string prefabName, Vector3 around, float minDist, float maxDist, bool champion)
		{
			GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabName) : null;
			if (prefab == null) { Plugin.Log.LogWarning($"World events: no creature called {prefabName}."); return null; }
			float ang = Random.value * Mathf.PI * 2f, dist = Random.Range(minDist, maxDist);
			Vector3 pos = around + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist;
			if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(pos, out float h, 1000)) pos.y = h;
			pos.y += 0.5f;
			GameObject go = Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
			Character c = go.GetComponent<Character>();
			if (c == null || c.m_nview == null || !c.m_nview.IsValid()) return null;
			ZDO zdo = c.m_nview.GetZDO();
			zdo.Set(WorldEvents.KeyEvent, _ev.Id);
			if (champion) zdo.Set(EnemyScaling.KeyUnique, true);
			BaseAI ai = go.GetComponent<BaseAI>();
			if (ai != null) { ai.SetHuntPlayer(true); ai.SetAlerted(true); }
			_tracked[zdo.m_uid] = 0;
			return c;
		}

		private void Finish(bool won)
		{
			if (_done) return;
			_done = true;
			if (won)
			{
				// One personal chest for each player who took part, in a small ring at the middle of the map circle
				// (that's where players look), unless that's in water.
				Vector3 center = _ev.Circle;
				if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(center, out float ch, 1000))
					center.y = ch;
				if (ZoneSystem.instance != null && center.y < ZoneSystem.instance.m_waterLevel + 0.5f) center = _ev.Pos;
				int i = 0, n = Mathf.Max(1, _players.Count);
				foreach (var kv in _players)
				{
					float ang = i++ * Mathf.PI * 2f / n;
					Vector3 at = center + (n > 1 ? new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 2.5f : Vector3.zero);
					WorldEvents.RewardChest(_ev, at, kv.Key, kv.Value, _maxStars);
				}
				WorldEvents.Say(_ev.Pos, "Event complete! Your reward chest awaits.");
				if (_shrine != ZDOID.None)
				{
					GameObject ward = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(_shrine) : null;
					if (ward != null && ZNetScene.instance != null) ZNetScene.instance.Destroy(ward);
				}
			}
			else WorldEvents.Say(_ev.Pos, "The event has failed.");
			Plugin.Debug($"[Events] {_ev.Type} #{_ev.Id} {(won ? "won" : "failed")}; highest star defeated {_maxStars}, {_players.Count} players.");
			WorldEvents.Finished(_ev, won);
			Destroy(gameObject);
		}
	}

	/// <summary>
	/// Defend raiders ignore players and go for the Shrine of Protection, even after being hit: every time the game
	/// picks their target, it's set back to the shrine. Once the shrine is gone they behave normally.
	/// </summary>
	public static class Raiders
	{
		public const string KeyTarget = "LLA_RaidTarget";
		public const string KeyWardMax = "LLA_WardMax";   // the Shrine of Protection's max health (marks it, too)

		/// <summary>On every game: the shrine isn't a real ward, has the event's max health, and shows a health bar.</summary>
		internal static void SetupWard(GameObject go)
		{
			foreach (PrivateArea pa in go.GetComponentsInChildren<PrivateArea>(true)) UnityEngine.Object.Destroy(pa);
			ZNetView nv = go.GetComponent<ZNetView>();
			float max = nv != null && nv.IsValid() ? nv.GetZDO().GetFloat(KeyWardMax) : 0f;
			WearNTear wnt = go.GetComponent<WearNTear>();
			if (wnt != null && max > 0f) wnt.m_health = max;
			Piece piece = go.GetComponent<Piece>();
			if (piece != null) { piece.m_name = "Shrine of Protection"; piece.m_canBeRemoved = false; }
			if (go.GetComponent<WardHealthBar>() == null) go.AddComponent<WardHealthBar>();
		}

		/// <summary>Players can't damage the Shrine of Protection (only raiders can).</summary>
		private static bool PlayerHitsWard(WearNTear wnt, HitData hit)
		{
			ZNetView nv = wnt.m_nview;
			if (hit == null || nv == null || !nv.IsValid() || nv.GetZDO().GetFloat(KeyWardMax) <= 0f) return false;
			Character attacker = hit.GetAttacker();
			return attacker != null && (attacker.IsPlayer() || attacker.IsTamed());
		}

		[HarmonyLib.HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
		private static class Patch_WardDamage
		{
			private static bool Prefix(WearNTear __instance, HitData hit) => !PlayerHitsWard(__instance, hit);
		}

		[HarmonyLib.HarmonyPatch(typeof(WearNTear), nameof(WearNTear.RPC_Damage))]
		private static class Patch_WardRpcDamage
		{
			private static bool Prefix(WearNTear __instance, HitData hit) => !PlayerHitsWard(__instance, hit);
		}

		[HarmonyLib.HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Awake))]
		private static class Patch_WardAwake
		{
			private static void Postfix(WearNTear __instance)
			{
				ZNetView nv = __instance.m_nview;
				if (nv == null || !nv.IsValid() || nv.GetZDO().GetFloat(KeyWardMax) <= 0f) return;
				SetupWard(__instance.gameObject);
			}
		}

		[HarmonyLib.HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateTarget))]
		private static class Patch_Target
		{
			private static void Postfix(MonsterAI __instance, ref bool canHearTarget, ref bool canSeeTarget)
			{
				ZNetView nv = __instance.m_nview;
				if (nv == null || !nv.IsValid()) return;
				ZDOID id = nv.GetZDO().GetZDOID(KeyTarget);
				if (id == ZDOID.None) return;
				GameObject ward = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(id) : null;
				StaticTarget target = ward != null ? ward.GetComponent<StaticTarget>() : null;
				if (target == null) return;
				__instance.m_attackPlayerObjects = true;
				__instance.m_targetCreature = null;
				__instance.m_targetStatic = target;
				canHearTarget = true;
				canSeeTarget = true;
			}
		}
	}

	/// <summary>A health bar floating above the Shrine of Protection, facing the camera: blue when healthy, red near death.</summary>
	public class WardHealthBar : MonoBehaviour
	{
		private WearNTear _wnt;
		private RectTransform _fill;
		private UnityEngine.UI.Image _fillImage;
		private GameObject _root;

		private void Start()
		{
			_wnt = GetComponent<WearNTear>();
			_root = new GameObject("LLA_WardBar", typeof(RectTransform));
			_root.transform.SetParent(transform, false);
			_root.transform.localPosition = Vector3.up * 3f;
			var canvas = _root.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.WorldSpace;
			var rt = (RectTransform)_root.transform;
			rt.sizeDelta = new Vector2(220f, 26f);
			rt.localScale = Vector3.one * 0.01f;   // 2.2 m wide

			Image(_root.transform, "Border", new Color(0.05f, 0.04f, 0.03f, 0.95f), Vector2.zero, Vector2.one, -3f);
			Image(_root.transform, "Back", new Color(0.18f, 0.15f, 0.12f, 0.9f), Vector2.zero, Vector2.one, 0f);
			_fillImage = Image(_root.transform, "Fill", new Color(0.35f, 0.65f, 1f), Vector2.zero, Vector2.one, 2f);
			_fill = _fillImage.rectTransform;
		}

		private static UnityEngine.UI.Image Image(Transform parent, string name, Color color, Vector2 min, Vector2 max, float inset)
		{
			var go = new GameObject(name, typeof(RectTransform));
			go.transform.SetParent(parent, false);
			var rt = (RectTransform)go.transform;
			rt.anchorMin = min;
			rt.anchorMax = max;
			rt.offsetMin = new Vector2(inset, inset);
			rt.offsetMax = new Vector2(-inset, -inset);
			var img = go.AddComponent<UnityEngine.UI.Image>();
			img.color = color;
			img.raycastTarget = false;
			return img;
		}

		private void LateUpdate()
		{
			if (_root == null || _wnt == null) return;
			Camera cam = Utils.GetMainCamera();
			bool show = cam != null && Vector3.Distance(cam.transform.position, transform.position) < 80f;
			if (_root.activeSelf != show) _root.SetActive(show);
			if (!show) return;
			_root.transform.rotation = Quaternion.LookRotation(_root.transform.position - cam.transform.position);
			float pct = Mathf.Clamp01(_wnt.GetHealthPercentage());
			_fill.anchorMax = new Vector2(pct, 1f);
			_fillImage.color = Color.Lerp(new Color(0.9f, 0.2f, 0.15f), new Color(0.35f, 0.65f, 1f), Mathf.SmoothStep(0.15f, 0.75f, pct));
		}
	}
}
