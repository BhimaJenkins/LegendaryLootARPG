using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LegendaryLoot.Enemies
{
	/// <summary>
	/// "5 - Enemies / Enemy count" (1x-3x): more creatures everywhere, by raising the game's own numbers rather than
	/// copying spawns.
	///  - Overworld spawns and vanilla raids (SpawnSystem): each entry's population cap and group size are multiplied
	///    while the game decides what to spawn, then put back.
	///  - Nests and other spawner structures (SpawnArea): their near/total limits are multiplied the same way.
	///  - Camp and dungeon spawn points (CreatureSpawner): each spawn brings extra copies (1.5x: 50% chance of one more),
	///    topped up only to the count, so they never pile up.
	/// Bosses are never multiplied, and neither are the mod's own events (their counts are in events.yml).
	/// </summary>
	public static class EnemyCount
	{
		private const string KeyExtraOf = "LLA_ExtraOf";   // on an extra creature: the spawn point it came from

		private static float Multiplier => Mathf.Clamp(Plugin.EnemyCount.Value, 1f, 3f);
		private static bool On => Multiplier > 1.001f;

		private static int Scale(int n) => Mathf.Max(n, Mathf.RoundToInt(n * Multiplier));

		/// <summary>1.5x of 1 = 1 or 2 (50/50); 2x = 2; 2.5x = 2 or 3.</summary>
		private static int RollCount()
		{
			float m = Multiplier;
			int whole = Mathf.FloorToInt(m);
			return whole + (Random.value < m - whole ? 1 : 0);
		}

		private static bool IsBoss(GameObject prefab)
		{
			Character c = prefab != null ? prefab.GetComponent<Character>() : null;
			return c != null && c.m_boss;
		}

		// ---------- overworld spawns and raids ----------

		private struct Saved
		{
			public SpawnSystem.SpawnData Data;
			public int Max, GroupMin, GroupMax;
		}

		[HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.UpdateSpawnList))]
		private static class Patch_SpawnList
		{
			private static void Prefix(List<SpawnSystem.SpawnData> spawners, out List<Saved> __state)
			{
				__state = null;
				if (!On || spawners == null) return;
				__state = new List<Saved>(spawners.Count);
				foreach (SpawnSystem.SpawnData d in spawners)
				{
					if (d == null || IsBoss(d.m_prefab)) continue;
					__state.Add(new Saved { Data = d, Max = d.m_maxSpawned, GroupMin = d.m_groupSizeMin, GroupMax = d.m_groupSizeMax });
					d.m_maxSpawned = Scale(d.m_maxSpawned);
					d.m_groupSizeMin = Scale(d.m_groupSizeMin);
					d.m_groupSizeMax = Mathf.Max(d.m_groupSizeMin, Scale(d.m_groupSizeMax));
				}
			}

			private static Exception Finalizer(Exception __exception, List<Saved> __state)
			{
				if (__state != null)
					foreach (Saved s in __state)
					{
						s.Data.m_maxSpawned = s.Max;
						s.Data.m_groupSizeMin = s.GroupMin;
						s.Data.m_groupSizeMax = s.GroupMax;
					}
				return __exception;
			}
		}

		// ---------- nests and spawner structures ----------

		[HarmonyPatch(typeof(SpawnArea), nameof(SpawnArea.UpdateSpawn))]
		private static class Patch_SpawnArea
		{
			private static void Prefix(SpawnArea __instance, out Vector2Int __state)
			{
				__state = new Vector2Int(-1, -1);
				if (!On) return;
				foreach (SpawnArea.SpawnData d in __instance.m_prefabs) if (d != null && IsBoss(d.m_prefab)) return;
				__state = new Vector2Int(__instance.m_maxNear, __instance.m_maxTotal);
				__instance.m_maxNear = Scale(__instance.m_maxNear);
				__instance.m_maxTotal = Scale(__instance.m_maxTotal);
			}

			private static Exception Finalizer(Exception __exception, SpawnArea __instance, Vector2Int __state)
			{
				if (__state.x >= 0) { __instance.m_maxNear = __state.x; __instance.m_maxTotal = __state.y; }
				return __exception;
			}
		}

		// ---------- camp and dungeon spawn points ----------

		[HarmonyPatch(typeof(CreatureSpawner), nameof(CreatureSpawner.Spawn))]
		private static class Patch_CreatureSpawner
		{
			[HarmonyPriority(Priority.Low)]
			private static void Postfix(CreatureSpawner __instance, ZNetView __result)
			{
				if (!On || __result == null || !__result.IsValid() || __instance.m_nview == null || !__instance.m_nview.IsValid()) return;
				try
				{
					GameObject prefab = __instance.m_creaturePrefab;
					if (prefab == null || IsBoss(prefab)) return;
					Character first = __result.GetComponent<Character>();
					if (first == null) return;
					ZDOID source = __instance.m_nview.GetZDO().m_uid;
					Vector3 at = __instance.transform.position;

					// Extras from this spawn point that are still alive count toward the total.
					int alive = 0;
					foreach (Character c in Character.GetAllCharacters())
					{
						if (c == null || c.IsDead() || c.m_nview == null || !c.m_nview.IsValid()) continue;
						if (c.m_nview.GetZDO().GetZDOID(KeyExtraOf) == source) alive++;
					}
					int extra = RollCount() - 1 - alive;
					for (int i = 0; i < extra; i++)
					{
						Vector2 off = Random.insideUnitCircle * 2.5f;
						Vector3 pos = at + new Vector3(off.x, 0.2f, off.y);
						GameObject go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
						Character c = go.GetComponent<Character>();
						if (c == null || c.m_nview == null || !c.m_nview.IsValid()) continue;
						c.m_nview.GetZDO().Set(KeyExtraOf, source);
						c.SetLevel(first.GetLevel());
						BaseAI ai = go.GetComponent<BaseAI>();
						BaseAI firstAi = first.GetComponent<BaseAI>();
						if (ai != null && firstAi != null && firstAi.IsAlerted()) ai.SetAlerted(true);
					}
				}
				catch (Exception ex) { Plugin.Log.LogWarning($"Enemy count: {ex.Message}"); }
			}
		}
	}
}
