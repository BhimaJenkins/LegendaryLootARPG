using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using LegendaryLoot.Core;
using LegendaryLoot.Data;
using LegendaryLoot.Effects;
using UnityEngine;

namespace LegendaryLoot.Loot
{
	/// <summary>
	/// Adds gear drops when a creature dies. Vanilla runs the death logic only on the game that owns the creature,
	/// so each kill rolls exactly once in multiplayer, and the items sync like any vanilla drop.
	///
	/// Two vanilla paths:
	///  - Most creatures (Greydwarfs, Boars, Draugr...) leave a ragdoll that drops its loot when the body fades.
	///    Vanilla turns CharacterDrop off for those and hands the loot list to the ragdoll. We roll at death,
	///    store the result on the ragdoll, and spawn it together with the vanilla loot.
	///  - Creatures without a dropping ragdoll drop straight away through CharacterDrop.OnDeath.
	/// </summary>
	public static class DropPatches
	{
		/// <summary>Key on each player's network object holding their Unique Find total, so whoever rolls a kill can read it.</summary>
		public const string UniqueFindZdoKey = "LLA.uniqueFind";
		private const string RagdollLootKey = "LLA.ragdollLoot";

		private static readonly List<Player> NearbyPlayers = new List<Player>();

		public struct PendingDrop
		{
			public LootRoller.Result Result;
			public long OwnerId;
			public string OwnerName;
		}

		// Path 1: creatures without a loot-dropping ragdoll
		[HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.OnDeath))]
		private static class Patch_OnDeath
		{
			private static void Postfix(CharacterDrop __instance)
			{
				// Ragdoll creatures arrive here with drops switched off; they're handled in Patch_RagdollSetup.
				if (!__instance.m_dropsEnabled) return;
				List<PendingDrop> drops = RollFor(__instance);
				if (drops == null) return;
				Vector3 pos = __instance.m_character.GetCenterPoint() + __instance.transform.TransformVector(__instance.m_spawnOffset);
				foreach (PendingDrop d in drops) Spawn(d.Result, pos, d.OwnerId, d.OwnerName);
			}
		}

		// Path 2a: roll at death and store the result on the ragdoll
		[HarmonyPatch(typeof(Ragdoll), nameof(Ragdoll.Setup))]
		private static class Patch_RagdollSetup
		{
			private static void Postfix(Ragdoll __instance, CharacterDrop characterDrop)
			{
				if (characterDrop == null || !__instance.m_dropItems) return;
				ZDO zdo = __instance.m_nview != null ? __instance.m_nview.GetZDO() : null;
				if (zdo == null) return;
				List<PendingDrop> drops = RollFor(characterDrop);
				if (drops == null || drops.Count == 0) return;
				zdo.Set(RagdollLootKey, Pack(drops));
			}
		}

		// Path 2b: spawn the stored drops when the body fades, next to the vanilla loot
		[HarmonyPatch(typeof(Ragdoll), nameof(Ragdoll.SpawnLoot))]
		private static class Patch_RagdollSpawnLoot
		{
			private static void Postfix(Ragdoll __instance, Vector3 center)
			{
				ZDO zdo = __instance.m_nview != null ? __instance.m_nview.GetZDO() : null;
				if (zdo == null) return;
				string packed = zdo.GetString(RagdollLootKey, "");
				if (string.IsNullOrEmpty(packed)) return;
				zdo.Set(RagdollLootKey, "");
				foreach (PendingDrop d in Unpack(packed)) Spawn(d.Result, center + Vector3.up * 0.5f, d.OwnerId, d.OwnerName);
			}
		}

		/// <summary>Runs the loot roll for a dying creature. Returns null if it can't drop gear.</summary>
		private static List<PendingDrop> RollFor(CharacterDrop characterDrop)
		{
			Character character = characterDrop.m_character;
			if (character == null || character.IsPlayer() || character.IsTamed()) return null; // no drops from tamed or summoned creatures
			if (Enemies.EnemyScaling.NoLoot(character)) return null; // a Splitting enemy's copies

			string prefabName = Utils.GetPrefabName(character.gameObject);
			int biomeStep = GetCreatureBiomeStep(prefabName, character.transform.position);
			// Affixed enemies count as a star higher for loot (enemies.yml affixLootStars).
			int stars = Mathf.Clamp(character.GetLevel() - 1 + Enemies.EnemyScaling.LootStarBonus(character), 0, 5);
			var drops = new List<PendingDrop>();

			if (character.IsBoss())
			{
				// Boss stars (ours, not its level) raise the Unique chance only. A Twin boss drops gear once.
				if (Enemies.UniqueEnemies.TwinAlive(character)) return null;
				Enemies.AffixRuntime br = Enemies.EnemyScaling.Of(character);
				stars = br != null ? Mathf.Clamp(br.Stars, 0, 5) : 0;
				NearbyPlayers.Clear();
				Player.GetPlayersInRange(character.transform.position, Plugin.BossLootRange.Value, NearbyPlayers);
				foreach (Player p in NearbyPlayers)
				{
					for (int i = 0; i < DataStore.Loot.BossDropsPerPlayer; i++)
					{
						var bossCtx = new LootRoller.Context
						{
							BiomeStep = biomeStep, Stars = stars, Boss = true, Guaranteed = true,
							UniqueFind = GetUniqueFind(p), Source = $"boss {prefabName} for {p.GetPlayerName()}",
						};
						LootRoller.Result r = LootRoller.Roll(bossCtx);
						if (r != null) drops.Add(new PendingDrop { Result = r, OwnerId = p.GetPlayerID(), OwnerName = p.GetPlayerName() });
					}
				}
				return drops;
			}

			Player killer = character.m_lastHit?.GetAttacker() as Player;

			// Unique enemies: 2 guaranteed drops, 2x Unique chance, +1 star. A Twin drops gear only when the second one dies.
			Enemies.AffixRuntime ar = Enemies.EnemyScaling.Of(character);
			if (ar != null && ar.Unique)
			{
				if (Enemies.UniqueEnemies.TwinAlive(character)) return null;
				UniqueEnemySettings u = DataStore.Enemies.Uniques;
				int uStars = Mathf.Clamp(character.GetLevel() - 1 + Enemies.EnemyScaling.LootStarBonus(character) + u.LootStars, 0, 8);
				for (int i = 0; i < Mathf.Max(1, u.Drops); i++)
				{
					var uctx = new LootRoller.Context
					{
						BiomeStep = biomeStep, Stars = uStars, Guaranteed = true, UniqueMultiplier = Mathf.Max(1f, u.UniqueChanceMultiplier),
						UniqueFind = GetUniqueFind(killer), Source = $"unique {prefabName} ({uStars} loot stars)",
					};
					LootRoller.Result ur = LootRoller.Roll(uctx);
					if (ur != null) drops.Add(new PendingDrop { Result = ur, OwnerId = killer != null ? killer.GetPlayerID() : 0, OwnerName = killer?.GetPlayerName() });
				}
				return drops;
			}
			var ctx = new LootRoller.Context
			{
				BiomeStep = biomeStep,
				Stars = stars,
				Swarm = DataStore.Loot.IsSwarm(prefabName),
				UniqueFind = GetUniqueFind(killer),
				Source = $"{prefabName} ({stars} stars)",
			};
			LootRoller.Result result = LootRoller.Roll(ctx);
			if (result != null)
				drops.Add(new PendingDrop { Result = result, OwnerId = killer != null ? killer.GetPlayerID() : 0, OwnerName = killer?.GetPlayerName() });
			else
				Plugin.Debug($"[Loot] {ctx.Source}: no drop ({LootRoller.DropChance(ctx) * 100f:0.#}% chance)");
			return drops;
		}

		// Stored as one line per drop: prefab \t ownerId \t ownerName \t magic data
		private static string Pack(List<PendingDrop> drops)
		{
			var sb = new StringBuilder();
			foreach (PendingDrop d in drops)
			{
				sb.Append(d.Result.Prefab.name).Append('\t').Append(d.OwnerId).Append('\t')
				  .Append((d.OwnerName ?? "").Replace('\t', ' ').Replace('\n', ' ')).Append('\t')
				  .Append(d.Result.Magic.Serialize()).Append('\n');
			}
			return sb.ToString();
		}

		private static IEnumerable<PendingDrop> Unpack(string packed)
		{
			foreach (string line in packed.Split('\n'))
			{
				string[] p = line.Split('\t');
				if (p.Length < 4) continue;
				GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(p[0]) : null;
				MagicItem magic = MagicItem.Deserialize(p[3]);
				if (prefab == null || magic == null) continue;
				long.TryParse(p[1], out long owner);
				yield return new PendingDrop { Result = new LootRoller.Result { Prefab = prefab, Magic = magic }, OwnerId = owner, OwnerName = p[2] };
			}
		}

		/// <summary>Spawns a dropped item with its mod data, the way vanilla spawns creature drops.</summary>
		public static ItemDrop Spawn(LootRoller.Result result, Vector3 pos, long ownerId, string ownerName)
		{
			Quaternion rot = Quaternion.Euler(0f, Random.Range(0, 360), 0f);
			Vector3 offset = Random.insideUnitSphere * 0.5f;
			offset.y = Mathf.Abs(offset.y);
			GameObject go = Object.Instantiate(result.Prefab, pos + offset, rot);
			ItemDrop drop = go.GetComponent<ItemDrop>();
			if (drop == null) return null;

			drop.m_itemData.SetMagic(result.Magic);
			if (ownerId != 0) PersonalLoot.Reserve(drop.m_itemData, ownerId, ownerName);
			DropSound.MarkFresh(drop.m_itemData); // every nearby player hears magicdrop.ogg once
			drop.Save();

			Rigidbody body = go.GetComponent<Rigidbody>();
			if (body != null)
			{
				Vector3 v = Random.insideUnitSphere;
				if (v.y < 0f) v.y = -v.y;
				body.AddForce(v * 5f, ForceMode.VelocityChange);
			}
			LightShaft.Attach(drop);
			return drop;
		}

		public static ItemDrop Spawn(LootRoller.Result result, Vector3 pos, Player owner) =>
			Spawn(result, pos, owner != null ? owner.GetPlayerID() : 0, owner?.GetPlayerName());

		/// <summary>Unique Find comes from the owner's gear, read from their network object so it works for any player.</summary>
		private static float GetUniqueFind(Player p)
		{
			if (p == null) return 0f;
			if (p == Player.m_localPlayer) return PlayerStats.Get(p, "Luck") + Powers.Buffs.Get("uniqueFind");
			ZDO zdo = p.m_nview != null ? p.m_nview.GetZDO() : null;
			return zdo != null ? zdo.GetFloat(UniqueFindZdoKey, 0f) : 0f;
		}

		/// <summary>The creature's home biome (from loot.yml), or the biome it died in if it isn't listed.</summary>
		public static int GetCreatureBiomeStep(string prefabName, Vector3 position)
		{
			LootTables t = DataStore.Loot;
			if (t.CreatureBiomes != null && t.CreatureBiomes.TryGetValue(prefabName, out string listed))
			{
				int s = t.BiomeStep(listed);
				if (s >= 0) return s;
			}
			Heightmap.Biome biome = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(position) : Heightmap.Biome.Meadows;
			int step = t.BiomeStep(biome.ToString());
			return step < 0 ? 0 : step;
		}
	}

	/// <summary>Publishes the local player's Unique Find total on their network object whenever it changes.</summary>
	[HarmonyPatch(typeof(Player), nameof(Player.UpdateModifiers))]
	public static class PublishUniqueFind
	{
		private static float _last = -1f;

		private static void Postfix(Player __instance)
		{
			if (__instance != Player.m_localPlayer || __instance.m_nview == null || !__instance.m_nview.IsOwner()) return;
			float value = PlayerStats.Get(__instance, "Luck") + Powers.Buffs.Get("uniqueFind");
			if (Mathf.Approximately(value, _last)) return;
			_last = value;
			__instance.m_nview.GetZDO()?.Set(DropPatches.UniqueFindZdoKey, value);
		}
	}
}
