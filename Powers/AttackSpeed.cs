using HarmonyLib;
using LegendaryLoot.Core;
using LegendaryLoot.Effects;
using UnityEngine;

namespace LegendaryLoot.Powers
{
	/// <summary>
	/// Attack speed, bow draw speed and crossbow reload speed for the local player.
	///  - Attack speed (Attack Speed affix, Uniques, the "attackSpeed" buff) plays attack animations faster.
	///    Hits land on animation events, so the hits come faster too.
	///  - Draw speed (Quick Draw, plus attack speed on bows) fills the bow's draw sooner.
	///  - Reload speed (Quick Draw, plus attack speed on crossbows) shortens the crossbow's reload.
	/// Other players see your swing at normal speed; the timing that matters (hits, damage) is on your game.
	/// </summary>
	public static class AttackSpeed
	{
		private const float MaxBonus = 100f; // never more than double speed

		/// <summary>Points of skill past 100 that count for one percent of speed (or block power).</summary>
		public const float PointsPerPercent = 5f;

		/// <summary>
		/// Skill mastery: vanilla stops a skill's effect at 100, but +skill gear can push the level past it.
		/// Every 5 points over 100 give 1% (counted smoothly, so 103 gives 0.6%).
		/// </summary>
		public static float MasteryBonus(Player p, Skills.SkillType skill)
		{
			if (p == null || skill == Skills.SkillType.None) return 0f;
			float level = p.GetSkills().GetSkillLevel(skill);
			return Mathf.Max(0f, level - 100f) / PointsPerPercent;
		}

		private static Skills.SkillType WeaponSkill(Player p)
		{
			ItemDrop.ItemData w = p.GetCurrentWeapon();
			return w != null ? w.m_shared.m_skillType : Skills.SkillType.Unarmed;
		}

		private static bool IsRanged(Skills.SkillType s) => s == Skills.SkillType.Bows || s == Skills.SkillType.Crossbows;

		/// <summary>Percent faster attacks from gear, buffs and weapon mastery (melee, unarmed and magic).</summary>
		public static float AttackBonus(Player p)
		{
			if (p == null) return 0f;
			Skills.SkillType skill = WeaponSkill(p);
			float mastery = IsRanged(skill) ? 0f : MasteryBonus(p, skill);
			return Mathf.Clamp(PlayerStats.Get(p, "ModifyAttackSpeed") + Buffs.Get("attackSpeed") + mastery, -50f, MaxBonus);
		}

		/// <summary>Percent faster bow draw and crossbow reload: Quick Draw, attack speed and bow/crossbow mastery.</summary>
		public static float DrawBonus(Player p)
		{
			if (p == null) return 0f;
			Skills.SkillType skill = WeaponSkill(p);
			float mastery = IsRanged(skill) ? MasteryBonus(p, skill) : 0f;
			return Mathf.Clamp(PlayerStats.Get(p, "QuickDraw") + AttackBonus(p) + mastery, -50f, MaxBonus);
		}

		private static float _lastSet = -1f;

		/// <summary>
		/// While attacking, vanilla leaves the animator's speed alone (except for an attack's own speed events and
		/// the hit-pause "freeze frame"). Scale whatever speed the attack is using, once each time it changes.
		/// </summary>
		[HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomFixedUpdate))]
		private static class Patch_AnimSpeed
		{
			private static void Postfix(CharacterAnimEvent __instance)
			{
				Player p = Player.m_localPlayer;
				if (p == null || __instance.m_character != p || __instance.m_animator == null) return;
				if (!p.InAttack() || __instance.m_pauseTimer > 0f) { _lastSet = -1f; return; }
				float bonus = AttackBonus(p);
				if (Mathf.Abs(bonus) < 0.01f) return;
				float current = __instance.m_animator.speed;
				if (Mathf.Abs(current - _lastSet) < 0.0001f) return; // already scaled
				_lastSet = current * (1f + bonus / 100f);
				__instance.m_animator.speed = _lastSet;
			}
		}

		/// <summary>Bow draw: the draw fills faster.</summary>
		[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetAttackDrawPercentage))]
		private static class Patch_Draw
		{
			private static void Postfix(Humanoid __instance, ref float __result)
			{
				if (__result <= 0f || __result >= 1f || __instance != Player.m_localPlayer) return;
				float bonus = DrawBonus((Player)__instance);
				if (Mathf.Abs(bonus) < 0.01f) return;
				__result = Mathf.Clamp01(__result * (1f + bonus / 100f));
			}
		}

		/// <summary>Crossbow reload: the reload finishes sooner.</summary>
		[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetWeaponLoadingTime))]
		private static class Patch_Reload
		{
			private static void Postfix(ItemDrop.ItemData __instance, ref float __result)
			{
				Player p = Player.m_localPlayer;
				if (p == null || __result <= 0f || !p.IsItemEquiped(__instance)) return;
				float bonus = DrawBonus(p);
				if (Mathf.Abs(bonus) < 0.01f) return;
				__result /= 1f + bonus / 100f;
			}
		}
	}
}
