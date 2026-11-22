using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace LegendaryLoot.Powers
{
	/// <summary>
	/// Friendly summons, ported from Keen Combat's Primal Rally:
	///  - friendly faction on every player's game (re-checked twice a second), tamed, following the summoner
	///  - never hurts players, buildings or player-made objects (crops, fences)
	///  - no loot, despawns when its time runs out or the summoner logs out, cleaned up on the next login
	/// Summons carry their own marker ("LLA_Summon"), separate from Keen Combat's, so the two mods never mix.
	/// </summary>
	public static class Summons
	{
		private const string KeySummon = "LLA_Summon";
		private const string KeyOwner = "LLA_SummonOwner";
		private const string KeyDamage = "LLA_SummonDamageMult";
		private const string KeyScale = "LLA_SummonScale";
		private const string KeyHitDamage = "LLA_SummonHitDamage";   // fixed damage per hit (e.g. 30% of your weapon)
		private const string KeyInvulnerable = "LLA_SummonInvulnerable";
		private const string KeyLook = "LLA_SummonLook";             // a different body, e.g. "raven"

		private class Active
		{
			public Character Character;
			public string Group;
			public float Until;
		}

		private static readonly List<Active> Mine = new List<Active>();

		public static string LookOf(Character c)
		{
			ZDO zdo = c != null && c.m_nview != null ? c.m_nview.GetZDO() : null;
			return zdo != null ? zdo.GetString(KeyLook) : "";
		}

		public static bool IsSummon(Character c)
		{
			ZDO zdo = c != null && c.m_nview != null ? c.m_nview.GetZDO() : null;
			return zdo != null && zdo.GetBool(KeySummon);
		}

		/// <summary>
		/// Summons a creature for the local player. 'max' limits how many of this group can be out at once
		/// (the oldest goes first).
		/// </summary>
		public static Character Spawn(Player owner, string prefabName, Vector3 position, float duration,
			float damageMult, float healthMult, float scale, int max, string group,
			float hitDamage = 0f, bool invulnerable = false, string look = null)
		{
			if (owner == null || ZNetScene.instance == null) return null;
			// "A|B": the first creature that exists (e.g. another mod's raven, else a bat).
			GameObject prefab = null;
			foreach (string name in (prefabName ?? "").Split('|'))
			{
				prefab = ZNetScene.instance.GetPrefab(name.Trim());
				if (prefab != null) break;
			}
			if (prefab == null)
			{
				Plugin.Log.LogWarning($"Summon: no creature called '{prefabName}'.");
				return null;
			}

			// Make room within the group's limit.
			Mine.RemoveAll(a => a.Character == null || a.Character.IsDead());
			var group_ = Mine.FindAll(a => a.Group == group);
			while (max > 0 && group_.Count >= max)
			{
				Despawn(group_[0].Character);
				Mine.Remove(group_[0]);
				group_.RemoveAt(0);
			}

			GameObject go = Object.Instantiate(prefab, position, Quaternion.LookRotation(owner.transform.forward));
			Character c = go.GetComponent<Character>();
			if (c == null)
			{
				Object.Destroy(go);
				return null;
			}

			if (healthMult > 0f && !Mathf.Approximately(healthMult, 1f))
			{
				float hp = c.GetMaxHealth() * healthMult;
				c.SetMaxHealth(hp);
				c.SetHealth(hp);
			}
			c.m_faction = Character.Faction.Players;

			ZNetView nview = go.GetComponent<ZNetView>();
			ZDO zdo = nview != null ? nview.GetZDO() : null;
			if (zdo != null)
			{
				zdo.Set(ZDOVars.s_tamed, true);
				zdo.Set(ZDOVars.s_tamedName, "Ally");
				zdo.Set(ZDOVars.s_follow, owner.GetPlayerName());
				zdo.Set(KeySummon, true);
				zdo.Set(KeyOwner, owner.GetPlayerName());
				zdo.Set(KeyDamage, damageMult > 0f ? damageMult : 1f);
				zdo.Set(KeyScale, scale > 0f ? scale : 1f);
				if (hitDamage > 0f) zdo.Set(KeyHitDamage, hitDamage);
				if (invulnerable) zdo.Set(KeyInvulnerable, true);
				if (!string.IsNullOrEmpty(look)) zdo.Set(KeyLook, look);
			}
			SummonLooks.Apply(c);
			if (scale > 0f && !Mathf.Approximately(scale, 1f)) go.transform.localScale = Vector3.one * scale;

			Tameable tame = go.GetComponent<Tameable>();
			if (tame != null)
			{
				tame.m_tamedEffect = new EffectList(); // no "tamed" hearts and sound: it's a summon, not a pet
				tame.Tame();
			}
			else go.AddComponent<Tameable>();

			MonsterAI ai = go.GetComponent<MonsterAI>();
			if (ai == null && go.GetComponent<AnimalAI>() is AnimalAI animal)
			{
				Object.Destroy(animal);
				ai = go.AddComponent<MonsterAI>();
			}
			if (ai != null)
			{
				ai.SetFollowTarget(owner.gameObject);
				ai.m_enableHuntPlayer = false;
				ai.m_attackPlayerObjects = false;
				// Wide awareness at first so it finds a fight quickly, then back to normal.
				ai.m_alertRange = ai.m_viewRange = ai.m_hearRange = 60f;
				ai.m_viewAngle = 180f;
				Plugin.Instance.StartCoroutine(NormalAwareness(ai, 10f));
			}

			Mine.Add(new Active { Character = c, Group = group, Until = Time.time + duration });
			Plugin.Instance.StartCoroutine(Timer(c, duration));
			return c;
		}

		private static IEnumerator NormalAwareness(MonsterAI ai, float delay)
		{
			yield return new WaitForSeconds(delay);
			if (ai == null) yield break;
			ai.m_alertRange = ai.m_viewRange = ai.m_hearRange = 20f;
			ai.m_viewAngle = 90f;
		}

		private static IEnumerator Timer(Character c, float duration)
		{
			yield return new WaitForSeconds(Mathf.Max(1f, duration));
			if (c != null && !c.IsDead()) Despawn(c);
		}

		public static void Despawn(Character c)
		{
			if (c == null) return;
			GameObject fx = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("fx_perfectdodge") : null;
			if (fx != null) Object.Instantiate(fx, c.transform.position, Quaternion.identity);
			if (c.m_nview != null && c.m_nview.IsValid() && c.m_nview.IsOwner() && ZNetScene.instance != null)
				ZNetScene.instance.Destroy(c.gameObject);
		}

		public static void DespawnAll()
		{
			foreach (Active a in Mine) Despawn(a.Character);
			Mine.Clear();
		}

		// Keeping summons friendly everywhere ------------------------------------------------------------

		private static IEnumerator SyncLoop()
		{
			var wait = new WaitForSeconds(0.5f);
			while (true)
			{
				yield return wait;
				foreach (Character c in Character.GetAllCharacters())
				{
					if (c == null || !IsSummon(c)) continue;
					c.m_faction = Character.Faction.Players;
					SummonLooks.Apply(c);
					MonsterAI ai = c.GetComponent<MonsterAI>();
					if (ai != null && ai.m_attackPlayerObjects) ai.m_attackPlayerObjects = false;
					if (ai != null && c.m_nview.IsOwner()) PickFight(c, ai);
					float scale = c.m_nview.GetZDO().GetFloat(KeyScale, 1f);
					if (scale > 0f && !Mathf.Approximately(scale, 1f) && Mathf.Abs(c.transform.localScale.x - scale) > 0.01f)
						c.transform.localScale = Vector3.one * scale;
				}
			}
		}

		/// <summary>
		/// Some creatures (drakes especially) are happy to just follow their summoner around. If a summon has
		/// nothing to fight, point it at the nearest enemy within 30 m.
		/// </summary>
		private static void PickFight(Character summon, MonsterAI ai)
		{
			Character current = ai.m_targetCreature;
			if (current != null && !current.IsDead()) return;
			Character best = null;
			float bestDist = 30f * 30f;
			Vector3 pos = summon.transform.position;
			foreach (Character other in Character.GetAllCharacters())
			{
				if (other == null || other == summon || other.IsDead() || other.IsPlayer() || IsSummon(other)) continue;
				if (other.IsTamed() || !BaseAI.IsEnemy(summon, other)) continue;
				float d = (other.transform.position - pos).sqrMagnitude;
				if (d < bestDist) { bestDist = d; best = other; }
			}
			if (best == null) return;
			ai.m_targetCreature = best;
			ai.SetAlerted(true);
		}

		private static Coroutine _loop;

		[HarmonyPatch(typeof(Game), nameof(Game.Start))]
		private static class Patch_Start
		{
			private static void Postfix()
			{
				if (_loop != null) Plugin.Instance.StopCoroutine(_loop);
				_loop = Plugin.Instance.StartCoroutine(SyncLoop());
				Mine.Clear();
			}
		}

		[HarmonyPatch(typeof(Character), nameof(Character.Start))]
		private static class Patch_CharacterStart
		{
			private static void Postfix(Character __instance)
			{
				if (!IsSummon(__instance)) return;
				__instance.m_faction = Character.Faction.Players;
				SummonLooks.Apply(__instance);
			}
		}

		/// <summary>Summons never hurt players, and deal their damage multiplier to everything else.</summary>
		[HarmonyPatch(typeof(Character), nameof(Character.Damage))]
		private static class Patch_Damage
		{
			private static void Prefix(Character __instance, HitData hit)
			{
				if (hit == null) return;
				// Invulnerable summons (Raven's Wing) shrug off everything.
				if (IsSummon(__instance) && __instance.m_nview.GetZDO().GetBool(KeyInvulnerable))
				{
					hit.m_damage = new HitData.DamageTypes();
					return;
				}
				Character attacker = hit.GetAttacker();
				if (attacker == null || !IsSummon(attacker)) return;
				if (__instance.IsPlayer() || IsSummon(__instance)) { hit.m_damage = new HitData.DamageTypes(); return; }
				ZDO zdo = attacker.m_nview.GetZDO();
				float fixedDamage = zdo.GetFloat(KeyHitDamage, 0f);
				float total = hit.GetTotalDamage();
				if (fixedDamage > 0f && total > 0f) hit.m_damage.Modify(fixedDamage / total); // keep its damage types
				float mult = zdo.GetFloat(KeyDamage, 1f);
				if (!Mathf.Approximately(mult, 1f)) hit.m_damage.Modify(mult);
			}
		}

		[HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
		private static class Patch_NoDrops
		{
			private static void Postfix(CharacterDrop __instance, List<KeyValuePair<GameObject, int>> __result)
			{
				if (__result != null && IsSummon(__instance.GetComponent<Character>())) __result.Clear();
			}
		}

		[HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
		private static class Patch_NoBuildingDamage
		{
			private static bool Prefix(HitData hit) => hit == null || !IsSummon(hit.GetAttacker());
		}

		[HarmonyPatch(typeof(Destructible), nameof(Destructible.Damage))]
		private static class Patch_NoPlacedObjectDamage
		{
			private static bool Prefix(Destructible __instance, HitData hit)
			{
				if (hit == null || !IsSummon(hit.GetAttacker())) return true;
				return __instance.GetComponent<Piece>() == null; // crops and placed objects are safe
			}
		}

		[HarmonyPatch(typeof(Game), nameof(Game.ContinueLogout))]
		private static class Patch_Logout
		{
			private static void Prefix() => DespawnAll();
		}

		/// <summary>On login, remove this player's summons left over from a crash or disconnect.</summary>
		[HarmonyPatch(typeof(Game), nameof(Game.SpawnPlayer))]
		private static class Patch_Cleanup
		{
			private static void Postfix()
			{
				Player p = Player.m_localPlayer;
				if (p == null || ZNetScene.instance == null) return;
				string name = p.GetPlayerName();
				var leftovers = new List<ZNetView>();
				foreach (ZNetView nview in ZNetScene.instance.m_instances.Values)
				{
					ZDO zdo = nview != null ? nview.GetZDO() : null;
					if (zdo == null || !zdo.GetBool(KeySummon) || zdo.GetString(KeyOwner) != name) continue;
					leftovers.Add(nview);
				}
				foreach (ZNetView nview in leftovers)
					if (nview.IsOwner()) nview.Destroy();
			}
		}
	}
}
