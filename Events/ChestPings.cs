using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace LegendaryLoot.Events
{
	/// <summary>
	/// A pulsing ping on the map (big map and minimap) at each of your event reward chests, until you empty it.
	/// Whoever makes a chest tells everyone where it is; only the player it's for keeps the ping. The pings are saved
	/// with the character, so they come back after a relog. A ping clears once you're near and its chest is gone
	/// (reward chests vanish when emptied).
	/// </summary>
	public static class ChestPings
	{
		private const string Rpc = "LLA_ChestPing";
		private const string SaveKey = "LLA.chestPings";
		private const float Match = 4f;        // a chest this close to a ping is its chest
		private const float CheckRange = 40f;  // near enough to tell whether the chest is still there

		private static readonly List<Vector3> Pings = new List<Vector3>();
		private static readonly Dictionary<int, Minimap.PinData> Pins = new Dictionary<int, Minimap.PinData>();
		private static readonly HashSet<Container> Live = new HashSet<Container>();   // loaded reward chests
		private static Player _loadedFor;
		private static readonly Dictionary<int, float> Pulse = new Dictionary<int, float>();   // ping key -> when its pulses began
		private const float PulseSeconds = 5f;
		private static GameObject _driver;

		[HarmonyPatch(typeof(Game), nameof(Game.Start))]
		private static class Patch_Start
		{
			private static void Postfix()
			{
				ZRoutedRpc.instance.Register<long, Vector3>(Rpc, OnPing);
				Pings.Clear();
				Pins.Clear();
				Pulse.Clear();
				Live.Clear();
				_loadedFor = null;
				if (_driver != null) return;
				_driver = new GameObject("LLA_ChestPings");
				UnityEngine.Object.DontDestroyOnLoad(_driver);
				_driver.AddComponent<Driver>();
			}
		}

		/// <summary>Called right after a reward chest is made, on the machine that made it.</summary>
		public static void Announce(long playerId, Vector3 pos)
		{
			if (ZRoutedRpc.instance == null) return;
			ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, Rpc, playerId, pos);
		}

		private static void OnPing(long sender, long playerId, Vector3 pos)
		{
			Player p = Player.m_localPlayer;
			if (p == null || p.GetPlayerID() != playerId) return;
			Load(p);
			Add(pos);
			Save(p);
			Pulse[Key(pos)] = Time.time;   // pops once a second for 5 seconds, so it's hard to miss
			try { Tick(); } catch (Exception ex) { Plugin.Log.LogWarning($"Chest pings: {ex.Message}"); }   // the pin now, not in a second
		}

		private static void Add(Vector3 pos)
		{
			foreach (Vector3 v in Pings) if (Flat(v, pos) < Match) return;
			Pings.Add(pos);
		}

		/// <summary>Reward chests: remember the loaded ones; one of yours with no ping gets one (chests from before).</summary>
		[HarmonyPatch(typeof(Container), nameof(Container.Awake))]
		private static class Patch_Awake
		{
			private static void Postfix(Container __instance)
			{
				ZDO zdo = __instance.m_nview != null && __instance.m_nview.IsValid() ? __instance.m_nview.GetZDO() : null;
				long owner = zdo != null ? zdo.GetLong(WorldEvents.KeyChestFor) : 0L;
				if (owner == 0) return;
				Live.Add(__instance);
				Player p = Player.m_localPlayer;
				if (p != null && owner == p.GetPlayerID() && __instance.GetInventory() != null && __instance.GetInventory().NrOfItems() > 0)
				{
					Load(p);
					int before = Pings.Count;
					Add(__instance.transform.position);
					if (Pings.Count != before) Save(p);
				}
			}
		}

		private class Driver : MonoBehaviour
		{
			private float _next;

			private void Update()
			{
				UpdatePulses();
				if (Time.time < _next) return;
				_next = Time.time + 1f;
				try { Tick(); }
				catch (Exception ex) { Plugin.Log.LogWarning($"Chest pings: {ex.Message}"); }
			}
		}

		private static void Tick()
		{
			Player p = Player.m_localPlayer;
			Minimap map = Minimap.instance;
			if (p == null || map == null) return;
			Load(p);
			Live.RemoveWhere(c => c == null);

			// A ping whose chest is gone (emptied): clear it once you're close enough to know.
			bool changed = false;
			for (int i = Pings.Count - 1; i >= 0; i--)
			{
				Vector3 pos = Pings[i];
				if (Flat(p.transform.position, pos) > CheckRange || !ZNetScene.instance.IsAreaReady(pos)) continue;
				bool there = false;
				foreach (Container c in Live)
					if (Flat(c.transform.position, pos) < Match && c.GetInventory() != null && c.GetInventory().NrOfItems() > 0) { there = true; break; }
				if (!there) { Pings.RemoveAt(i); changed = true; }
			}
			if (changed) Save(p);

			// Keep one animated pin per ping.
			var want = new HashSet<int>();
			foreach (Vector3 pos in Pings)
			{
				int key = Key(pos);
				want.Add(key);
				if (Pins.TryGetValue(key, out Minimap.PinData pin) && pin != null && map.m_pins.Contains(pin)) continue;
				pin = map.AddPin(pos, Minimap.PinType.Ping, "", false, false, 0L);
				pin.m_animate = true;
				Pins[key] = pin;
			}
			foreach (int key in new List<int>(Pins.Keys))
			{
				if (want.Contains(key)) continue;
				if (Pins[key] != null) map.RemovePin(Pins[key]);
				Pins.Remove(key);
			}
		}

		/// <summary>A new chest's pin pops to double size for half of each second, five times, then settles.</summary>
		private static void UpdatePulses()
		{
			if (Pulse.Count == 0) return;
			foreach (int key in new List<int>(Pulse.Keys))
			{
				float t = Time.time - Pulse[key];
				bool done = t >= PulseSeconds;
				if (Pins.TryGetValue(key, out Minimap.PinData pin) && pin != null)
					pin.m_doubleSize = !done && (t % 1f) < 0.5f;
				else if (t > PulseSeconds + 2f) done = true;   // its pin never showed
				if (done) Pulse.Remove(key);
			}
		}

		private static int Key(Vector3 pos) => (Mathf.RoundToInt(pos.x) * 73856093) ^ (Mathf.RoundToInt(pos.z) * 19349663);

		private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

		private static void Load(Player p)
		{
			if (_loadedFor == p) return;
			_loadedFor = p;
			Pings.Clear();
			if (p.m_customData == null || !p.m_customData.TryGetValue(SaveKey, out string text) || string.IsNullOrEmpty(text)) return;
			foreach (string part in text.Split(';'))
			{
				string[] xyz = part.Split(',');
				if (xyz.Length == 3 &&
				    float.TryParse(xyz[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
				    float.TryParse(xyz[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
				    float.TryParse(xyz[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
					Pings.Add(new Vector3(x, y, z));
			}
		}

		private static void Save(Player p)
		{
			if (p?.m_customData == null) return;
			var parts = new List<string>();
			foreach (Vector3 v in Pings)
				parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.#},{1:0.#},{2:0.#}", v.x, v.y, v.z));
			if (parts.Count == 0) p.m_customData.Remove(SaveKey);
			else p.m_customData[SaveKey] = string.Join(";", parts);
		}

		/// <summary>lla-chestpings: list (or "clear") your reward chest pings.</summary>
		public static string Debug(string arg)
		{
			Player p = Player.m_localPlayer;
			if (p == null) return "";
			Load(p);
			if (arg == "clear") { Pings.Clear(); Save(p); return "Reward chest pings cleared."; }
			if (Pings.Count == 0) return "No reward chest pings.";
			var parts = new List<string>();
			foreach (Vector3 v in Pings) parts.Add($"{Flat(p.transform.position, v):0} m away");
			return $"{Pings.Count} reward chest ping(s): {string.Join(", ", parts)}.";
		}
	}
}
