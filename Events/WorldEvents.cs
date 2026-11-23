using System;
using System.Collections.Generic;
using HarmonyLib;
using LegendaryLoot.Core;
using LegendaryLoot.Data;
using LegendaryLoot.Enemies;
using LegendaryLoot.Loot;
using LegendaryLoot.Vendors;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LegendaryLoot.Events
{
	/// <summary>One world event, as the server tracks it and sends it to everyone.</summary>
	public class WorldEvent
	{
		public int Id;
		public string Type = "";
		public Vector3 Pos;      // the real spot
		public Vector3 Circle;   // the map circle's center (near, not on, the spot)
		public int Step;         // its biome's step
		public string Biome = "";
		public int State;        // 0 waiting, 1 running
		public long Owner;       // the game running it
		public double Created;   // server time (server only)
	}

	/// <summary>
	/// World events (events.yml). The server keeps 1-2 events near each player (105-420 m away, in biomes you've reached,
	/// on land), each with a circle on everyone's map. When a player comes within 60 m, their game runs the event: it
	/// spawns the creatures and, on a win, a personal reward chest for each player who took part. Unfinished events fade
	/// after 60 minutes; a spot then rests for 45 minutes.
	/// </summary>
	public static class WorldEvents
	{
		private const string ListRpc = "LLA_EvList", RequestRpc = "LLA_EvReq", StartRpc = "LLA_EvStart", EndRpc = "LLA_EvEnd", MessageRpc = "LLA_EvMsg";
		public const string KeyEvent = "LLA_Event";       // on event creatures: the event's id
		public const string KeyChestFor = "LLA_ChestFor"; // on a reward chest: the player it's for
		public const string KeyChestName = "LLA_ChestName";
		public const string KeyEventBoss = "LLA_EventBoss"; // a Boss event's boss (weaker; its kill doesn't count for progression)

		private static EventFile Data => DataStore.Events;

		// ---------- setup ----------

		private static GameObject _driver;

		[HarmonyPatch(typeof(Game), nameof(Game.Start))]
		private static class Patch_Start
		{
			private static void Postfix()
			{
				ZRoutedRpc.instance.Register<ZPackage>(ListRpc, OnList);
				ZRoutedRpc.instance.Register(RequestRpc, OnRequest);
				ZRoutedRpc.instance.Register<int>(StartRpc, OnStart);
				ZRoutedRpc.instance.Register<int, int>(EndRpc, OnEnd);
				ZRoutedRpc.instance.Register<Vector3, string>(MessageRpc, OnMessage);
				Server.Clear();
				Cooldowns.Clear();
				Known.Clear();
				_requested = false;
				_nextCheck = 0;
				if (_driver == null)
				{
					_driver = new GameObject("LLA_WorldEvents");
					UnityEngine.Object.DontDestroyOnLoad(_driver);
					_driver.AddComponent<Driver>();
				}
			}
		}

		private class Driver : MonoBehaviour
		{
			private float _next;

			private void Update()
			{
				if (Time.time < _next || ZNet.instance == null || ZRoutedRpc.instance == null) return;
				_next = Time.time + 1f;
				try
				{
					if (ZNet.instance.IsServer()) ServerTick();
					if (Player.m_localPlayer != null) ClientTick(Player.m_localPlayer);
				}
				catch (Exception ex) { Plugin.Log.LogWarning($"World events: {ex}"); }
			}
		}

		// ---------- server ----------

		private static readonly List<WorldEvent> Server = new List<WorldEvent>();
		private static readonly List<KeyValuePair<Vector3, double>> Cooldowns = new List<KeyValuePair<Vector3, double>>();
		private static int _nextId = 1;
		private static double _nextCheck;

		private static void ServerTick()
		{
			double now = ZNet.instance.GetTimeSeconds();
			bool changed = false;
			if (!Data.Enabled)
			{
				if (Server.Count > 0) { Server.Clear(); Broadcast(); }
				return;
			}
			for (int i = Server.Count - 1; i >= 0; i--)
			{
				WorldEvent ev = Server[i];
				if (now - ev.Created < Data.LifetimeMinutes * 60f) continue;
				Server.RemoveAt(i);
				Cooldowns.Add(new KeyValuePair<Vector3, double>(ev.Pos, now + Data.CooldownMinutes * 60f));
				changed = true;
				Plugin.Debug($"[Events] {ev.Type} #{ev.Id} faded.");
			}
			Cooldowns.RemoveAll(c => c.Value < now);
			if (now >= _nextCheck)
			{
				_nextCheck = now + Mathf.Max(0.25f, Data.CheckMinutes) * 60f;
				TopUp(now);
				changed = true;   // also resends the countdown to the next check
			}
			if (changed) Broadcast();
		}

		private static List<Vector3> PlayerPositions()
		{
			var list = new List<Vector3>();
			if (!ZNet.instance.IsDedicated() && Player.m_localPlayer != null) list.Add(Player.m_localPlayer.transform.position);
			foreach (ZNetPeer peer in ZNet.instance.GetConnectedPeers())
				if (peer != null && !peer.m_characterID.IsNone()) list.Add(peer.m_refPos);
			return list;
		}

		private static bool TopUp(double now)
		{
			List<Vector3> players = PlayerPositions();
			if (players.Count == 0) return false;
			int cap = Mathf.Min(Data.CapMax, Data.CapPerPlayer * players.Count);
			bool added = false;
			foreach (Vector3 p in players)
			{
				int near = 0;
				foreach (WorldEvent ev in Server) if (Flat(ev.Pos, p) < Data.NearRange) near++;
				while (near < Data.PerPlayer && Server.Count < cap && TryPlace(p, players, out WorldEvent ev))
				{
					ev.Created = now;
					Server.Add(ev);
					near++;
					added = true;
					Plugin.Debug($"[Events] {ev.Type} #{ev.Id} in the {ev.Biome} at {ev.Pos:F0}.");
				}
			}
			return added;
		}

		private static bool TryPlace(Vector3 from, List<Vector3> players, out WorldEvent ev)
		{
			ev = null;
			WorldGenerator wg = WorldGenerator.instance;
			if (wg == null || ZoneSystem.instance == null) return false;
			float water = ZoneSystem.instance.m_waterLevel;
			int reached = QuestBoard.QuestStep();
			string type = PickType();
			if (type == null) return false;
			for (int tries = 0; tries < 40; tries++)
			{
				float ang = Random.value * Mathf.PI * 2f, dist = Random.Range(Data.SpawnMin, Data.SpawnMax);
				float x = from.x + Mathf.Cos(ang) * dist, z = from.z + Mathf.Sin(ang) * dist;
				string biome = wg.GetBiome(x, z).ToString();
				if (!Data.Biomes.TryGetValue(biome, out BiomeCreatures bc) || bc == null || bc.Creatures.Count == 0) continue;
				int step = DataStore.Loot.BiomeStep(biome);
				if (step < 0 || step > reached) continue;
				float h = wg.GetHeight(x, z);
				if (h < water + 1f) continue;
				var pos = new Vector3(x, h, z);
				if (TooClose(pos, players)) continue;
				Vector2 off = Random.insideUnitCircle * Data.CircleOffset;
				ev = new WorldEvent { Id = _nextId++, Type = type, Pos = pos, Circle = pos + new Vector3(off.x, 0f, off.y), Step = step, Biome = biome };
				return true;
			}
			return false;
		}

		private static bool TooClose(Vector3 pos, List<Vector3> players)
		{
			foreach (Vector3 p in players) if (Flat(pos, p) < Data.SpawnMin * 0.9f) return true;
			foreach (WorldEvent ev in Server) if (Flat(pos, ev.Pos) < Data.Spacing) return true;
			foreach (var c in Cooldowns) if (Flat(pos, c.Key) < Data.CooldownRadius) return true;
			return false;
		}

		private static string PickType()
		{
			float total = 0f;
			foreach (var kv in Data.Types) if (kv.Value != null && kv.Value.Weight > 0f) total += kv.Value.Weight;
			if (total <= 0f) return null;
			float r = Random.value * total;
			foreach (var kv in Data.Types)
			{
				if (kv.Value == null || kv.Value.Weight <= 0f) continue;
				r -= kv.Value.Weight;
				if (r <= 0f) return kv.Key;
			}
			return null;
		}

		private static void Broadcast()
		{
			var pkg = new ZPackage();
			pkg.Write(Server.Count);
			foreach (WorldEvent ev in Server)
			{
				pkg.Write(ev.Id);
				pkg.Write(ev.Type);
				pkg.Write(ev.Pos);
				pkg.Write(ev.Circle);
				pkg.Write(ev.Step);
				pkg.Write(ev.Biome);
				pkg.Write(ev.State);
				pkg.Write(ev.Owner);
			}
			pkg.Write((float)Math.Max(0.0, _nextCheck - ZNet.instance.GetTimeSeconds()));
			ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, ListRpc, pkg);
		}

		private static void OnRequest(long sender)
		{
			if (ZNet.instance != null && ZNet.instance.IsServer()) Broadcast();
		}

		private static void OnStart(long sender, int id)
		{
			if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
			WorldEvent ev = Server.Find(e => e.Id == id);
			if (ev == null || ev.State != 0) return;
			ev.State = 1;
			ev.Owner = sender;
			Broadcast();
		}

		/// <summary>result: 1 won, 0 failed, 2 cancelled (too close to a base).</summary>
		private static void OnEnd(long sender, int id, int result)
		{
			if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
			WorldEvent ev = Server.Find(e => e.Id == id);
			if (ev == null) return;
			Server.Remove(ev);
			Cooldowns.Add(new KeyValuePair<Vector3, double>(ev.Pos, ZNet.instance.GetTimeSeconds() + Data.CooldownMinutes * 60f));
			Broadcast();
		}

		// ---------- every game ----------

		private static readonly Dictionary<int, WorldEvent> Known = new Dictionary<int, WorldEvent>();
		private static readonly Dictionary<int, float> StartAsked = new Dictionary<int, float>();
		private static readonly Dictionary<int, EventRunner> Runners = new Dictionary<int, EventRunner>();
		private static bool _requested;
		private static float _nextEventsAt = -1f;

		/// <summary>Seconds until the server next tops up events (-1: not known yet).</summary>
		public static float NextEventsIn => _nextEventsAt < 0f ? -1f : Mathf.Max(0f, _nextEventsAt - Time.time);
		private static float _requestAt;

		public static IEnumerable<WorldEvent> All => Known.Values;

		private static void OnList(long sender, ZPackage pkg)
		{
			Known.Clear();
			int n = pkg.ReadInt();
			for (int i = 0; i < n; i++)
			{
				var ev = new WorldEvent
				{
					Id = pkg.ReadInt(), Type = pkg.ReadString(), Pos = pkg.ReadVector3(), Circle = pkg.ReadVector3(),
					Step = pkg.ReadInt(), Biome = pkg.ReadString(), State = pkg.ReadInt(), Owner = pkg.ReadLong(),
				};
				Known[ev.Id] = ev;
			}
			if (pkg.GetPos() < pkg.Size()) _nextEventsAt = Time.time + pkg.ReadSingle();
			Pins.Sync();
		}

		private static void ClientTick(Player p)
		{
			if (!_requested)
			{
				if (_requestAt <= 0f) _requestAt = Time.time + 3f;
				if (Time.time >= _requestAt)
				{
					_requested = true;
					ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RequestRpc);
				}
			}
			Pins.Sync();
			Shrines.Tick(p, Known.Values);
			if (p.IsDead()) return;
			long me = ZRoutedRpc.instance.m_id;
			foreach (WorldEvent ev in Known.Values)
			{
				if (ev.Type == "Shrine") continue;   // shrines need no runner: touching one ends it (Shrines)
				if (ev.State == 1)
				{
					if (ev.Owner == me && !Runners.ContainsKey(ev.Id)) StartRunner(ev);
					continue;
				}
				if (Flat(p.transform.position, ev.Pos) > Data.ActivateRange || p.InInterior()) continue;
				if (StartAsked.TryGetValue(ev.Id, out float asked) && Time.time - asked < 5f) continue;
				StartAsked[ev.Id] = Time.time;
				if (NearBase(ev.Pos))
				{
					ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), EndRpc, ev.Id, 2);
					Plugin.Debug($"[Events] {ev.Type} #{ev.Id} was next to a base; cancelled.");
					continue;
				}
				ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), StartRpc, ev.Id);
			}
		}

		private static bool NearBase(Vector3 pos)
		{
			if (EffectArea.IsPointInsideArea(pos, EffectArea.Type.PlayerBase, Data.BaseDistance) != null) return true;
			var pieces = new List<Piece>();
			Piece.GetAllPiecesInRadius(pos, Data.BaseDistance, pieces);
			foreach (Piece piece in pieces) if (piece != null && piece.IsPlacedByPlayer()) return true;
			return false;
		}

		private static void StartRunner(WorldEvent ev)
		{
			var go = new GameObject($"LLA_Event_{ev.Id}");
			EventRunner r = go.AddComponent<EventRunner>();
			r.Init(ev);
			Runners[ev.Id] = r;
		}

		/// <summary>Tells the server an event is over (1 won, 0 failed, 2 cancelled).</summary>
		internal static void End(int id, int result) =>
			ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), EndRpc, id, result);

		internal static void Finished(WorldEvent ev, bool won)
		{
			Runners.Remove(ev.Id);
			ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), EndRpc, ev.Id, won ? 1 : 0);
		}

		/// <summary>A message for every player near the spot.</summary>
		internal static void Say(Vector3 pos, string text) => ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, MessageRpc, pos, text);

		private static void OnMessage(long sender, Vector3 pos, string text)
		{
			Player p = Player.m_localPlayer;
			if (p != null && Flat(p.transform.position, pos) < Mathf.Max(150f, Data.LeaveRange + 30f))
				p.Message(MessageHud.MessageType.Center, text);
		}

		internal static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

		// ---------- map ----------

		/// <summary>Each event's circle and name on the map. Not saved; redrawn from the server's list.</summary>
		private static class Pins
		{
			private static readonly Dictionary<int, Minimap.PinData> Circles = new Dictionary<int, Minimap.PinData>();
			private static Minimap _for;
			private static Sprite _blue;

			/// <summary>A blue ring with a faint fill, so events don't read as raids on your base.</summary>
			private static Sprite BlueCircle
			{
				get
				{
					if (_blue != null) return _blue;
					const int size = 256;
					var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "LLA_EventCircle" };
					var px = new Color32[size * size];
					float r = size / 2f;
					for (int y = 0; y < size; y++)
					for (int x = 0; x < size; x++)
					{
						float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r)) / r;
						float a = d > 1f ? 0f : d > 0.94f ? 0.85f : 0.22f;
						px[y * size + x] = new Color32(70, 150, 255, (byte)(a * 255f));
					}
					tex.SetPixels32(px);
					tex.Apply(false, false);
					_blue = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
					return _blue;
				}
			}

			public static void Recolor()
			{
				foreach (Minimap.PinData pin in Circles.Values)
				{
					if (pin?.m_iconElement == null) continue;
					if (pin.m_iconElement.sprite != BlueCircle) pin.m_iconElement.sprite = BlueCircle;
					pin.m_iconElement.color = Color.white;
				}
			}

			public static void Sync()
			{
				Minimap map = Minimap.instance;
				if (map == null) return;
				if (_for != map) { Circles.Clear(); _for = map; }
				// Just a blue circle: what the event is stays a surprise until you walk in.
				foreach (int id in new List<int>(Circles.Keys))
				{
					if (Known.ContainsKey(id)) continue;
					if (Circles[id] != null) map.RemovePin(Circles[id]);
					Circles.Remove(id);
				}
				foreach (WorldEvent ev in Known.Values)
				{
					if (Circles.ContainsKey(ev.Id)) continue;
					Minimap.PinData circle = map.AddPin(ev.Circle, Minimap.PinType.EventArea, "", false, false, 0L);
					if (circle != null) circle.m_worldSize = Data.CircleRadius * 2f;
					Circles[ev.Id] = circle;
				}
			}
		}

		/// <summary>Event circles and icons are blue on both maps (the pins' images are rebuilt as the map redraws).</summary>
		[HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdatePins))]
		private static class Patch_BluePins
		{
			private static void Postfix() => Pins.Recolor();
		}

		/// <summary>The big map shows "New events in m:ss" at the top.</summary>
		[HarmonyPatch(typeof(Minimap), nameof(Minimap.Update))]
		private static class Patch_Countdown
		{
			private static TMPro.TMP_Text _label;

			private static void Postfix(Minimap __instance)
			{
				if (__instance.m_mode != Minimap.MapMode.Large || __instance.m_largeRoot == null) return;
				if (_label == null)
				{
					TMPro.TMP_Text source = __instance.m_biomeNameLarge;
					if (source == null) return;
					GameObject go = UnityEngine.Object.Instantiate(source.gameObject, __instance.m_largeRoot.transform, false);
					go.name = "LLA_EventCountdown";
					foreach (MonoBehaviour mb in go.GetComponents<MonoBehaviour>()) if (!(mb is TMPro.TMP_Text)) UnityEngine.Object.Destroy(mb);
					_label = go.GetComponent<TMPro.TMP_Text>();
					var rt = (RectTransform)go.transform;
					rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
					rt.pivot = new Vector2(0.5f, 1f);
					rt.sizeDelta = new Vector2(500f, 40f);
					rt.anchoredPosition = new Vector2(0f, -70f);
					_label.alignment = TMPro.TextAlignmentOptions.Center;
					_label.fontSize = Mathf.Max(18f, source.fontSize * 0.6f);
					_label.color = new Color(0.6f, 0.82f, 1f);
					_label.enableWordWrapping = false;
					_label.raycastTarget = false;
				}
				float left = Data.Enabled ? NextEventsIn : -1f;
				bool show = left >= 0f;
				if (_label.gameObject.activeSelf != show) _label.gameObject.SetActive(show);
				if (!show) return;
				int s = Mathf.CeilToInt(left);
				_label.text = $"New events in {s / 60}:{s % 60:00}";
			}
		}

		// ---------- reward chests ----------

		private static GameObject _holder;

		/// <summary>A personal chest: 2 gear rolls from the event's biome at the highest star level defeated, plus gold.</summary>
		internal static void RewardChest(WorldEvent ev, Vector3 at, long playerId, string playerName, int stars)
		{
			if (ZNetScene.instance == null) return;
			GameObject prefab = null;
			foreach (string n in new[] { Data.Chest, "TreasureChest_meadows", "TreasureChest_forestcrypt", "piece_chest_wood" })
				if (!string.IsNullOrEmpty(n) && (prefab = ZNetScene.instance.GetPrefab(n)) != null) break;
			if (prefab == null) return;
			if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(at, out float h, 1000)) at.y = h;

			if (_holder == null) { _holder = new GameObject("LLA_EventChestHolder"); _holder.SetActive(false); UnityEngine.Object.DontDestroyOnLoad(_holder); }
			GameObject chest = UnityEngine.Object.Instantiate(prefab, _holder.transform);
			foreach (RandomSpawn rs in chest.GetComponentsInChildren<RandomSpawn>(true)) UnityEngine.Object.DestroyImmediate(rs);
			chest.transform.position = at;
			chest.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
			chest.transform.SetParent(null, true);
			chest.SetActive(true);
			ZNetView nv = chest.GetComponent<ZNetView>();
			Container box = chest.GetComponent<Container>();
			if (nv == null || !nv.IsValid() || box == null) { UnityEngine.Object.Destroy(chest); return; }
			nv.GetZDO().Set(KeyChestFor, playerId);
			nv.GetZDO().Set(KeyChestName, playerName ?? "");
			box.m_autoDestroyEmpty = true;

			Inventory inv = box.GetInventory();
			inv.RemoveAll();   // no default treasure: just the reward
			Data.Types.TryGetValue(ev.Type, out EventType type);
			int rolls = type != null && type.GearRolls >= 0 ? type.GearRolls : Data.GearRolls;
			for (int i = 0; i < Mathf.Max(0, rolls); i++)
			{
				LootRoller.Result r = LootRoller.Roll(new LootRoller.Context
				{
					BiomeStep = ev.Step, Stars = Mathf.Clamp(stars, 0, 5), Guaranteed = true, Boss = type != null && type.BossLoot,
					UniquePercent = type != null ? type.UniquePercent : 0f, Source = $"event {ev.Type} ({stars} stars)",
				});
				if (r != null) inv.AddItem(Shop.Make(r));
			}
			int gold = Mathf.RoundToInt(Data.Gold * (type != null ? type.GoldMultiplier : 1f) *
			                            VendorTables.AtStep(DataStore.Vendors.BiomeSellMultiplier, Mathf.Max(0, ev.Step)));
			GameObject coins = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Coins") : null;
			if (gold > 0 && coins != null)
			{
				ItemDrop.ItemData c = coins.GetComponent<ItemDrop>().m_itemData.Clone();
				c.m_dropPrefab = coins;
				c.m_stack = gold;
				inv.AddItem(c);
			}
			LightShaft.Mark(chest, Tier.Legendary);
		}

		/// <summary>Reward chests open only for the player they're for, and disappear once emptied.</summary>
		[HarmonyPatch(typeof(Container), nameof(Container.Interact))]
		private static class Patch_PersonalChest
		{
			private static bool Prefix(Container __instance, Humanoid character, ref bool __result)
			{
				if (__instance.m_nview == null || !__instance.m_nview.IsValid() || !(character is Player p)) return true;
				long owner = __instance.m_nview.GetZDO().GetLong(KeyChestFor);
				if (owner == 0 || owner == p.GetPlayerID()) return true;
				string name = __instance.m_nview.GetZDO().GetString(KeyChestName);
				p.Message(MessageHud.MessageType.Center, $"This reward is for {(string.IsNullOrEmpty(name) ? "someone else" : name)}.");
				__result = false;
				return false;
			}
		}

		[HarmonyPatch(typeof(Container), nameof(Container.Awake))]
		private static class Patch_ChestAwake
		{
			private static void Postfix(Container __instance)
			{
				if (__instance.m_nview != null && __instance.m_nview.IsValid() && __instance.m_nview.GetZDO().GetLong(KeyChestFor) != 0)
					__instance.m_autoDestroyEmpty = true;
			}
		}

		// ---------- debug ----------

		public static string DebugList()
		{
			if (Known.Count == 0) return "No world events known yet.";
			Player p = Player.m_localPlayer;
			var parts = new List<string>();
			foreach (WorldEvent ev in Known.Values)
				parts.Add($"#{ev.Id} {ev.Type} ({ev.Biome}{(ev.State == 1 ? ", under way" : "")}) {(p != null ? Flat(p.transform.position, ev.Pos).ToString("0") + " m away" : "")}");
			return string.Join("; ", parts);
		}

		/// <summary>lla-event: the server places an event of this type right next to you (host or single player).</summary>
		public static string DebugSpawn(string type)
		{
			if (ZNet.instance == null || !ZNet.instance.IsServer()) return "Only the host (or single player) can place events.";
			Player p = Player.m_localPlayer;
			if (p == null) return "";
			string key = null;
			foreach (string k in Data.Types.Keys) if (string.Equals(k, type, StringComparison.OrdinalIgnoreCase)) key = k;
			if (key == null) return $"Unknown event type. Types: {string.Join(", ", Data.Types.Keys)}.";
			if (key == "Hunted") return Hunted.Debug(null);
			Vector3 pos = p.transform.position + p.transform.forward * 30f;
			if (ZoneSystem.instance != null) pos.y = ZoneSystem.instance.GetGroundHeight(pos);
			string biome = WorldGenerator.instance.GetBiome(pos.x, pos.z).ToString();
			var ev = new WorldEvent
			{
				Id = _nextId++, Type = key, Pos = pos, Circle = pos, Step = Mathf.Max(0, DataStore.Loot.BiomeStep(biome)), Biome = biome,
				Created = ZNet.instance.GetTimeSeconds(),
			};
			Server.Add(ev);
			Broadcast();
			return $"Placed a {key} in the {biome}, 30 m ahead. Walk to it.";
		}
	}
}
