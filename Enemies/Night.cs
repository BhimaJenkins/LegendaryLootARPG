using System;
using HarmonyLib;
using LegendaryLoot.Data;
using UnityEngine;

namespace LegendaryLoot.Enemies
{
	/// <summary>
	/// Night (enemies.yml night): from dusk to dawn creatures deal 30% more damage, take 30% less (30% more health in
	/// effect, so creatures already out get it too, and lose it at dawn), and their gear drops are better (not more).
	/// A message tells each player when night falls and when dawn breaks. Tamed creatures and summons are left alone;
	/// bosses only if night.bosses is on.
	/// </summary>
	public static class Night
	{
		private static NightSettings S => DataStore.Enemies.Night ?? new NightSettings();

		/// <summary>True at night (the same on every machine: it follows the world's clock).</summary>
		public static bool Active => DataStore.Enemies.Enabled && S.Enabled && EnvMan.instance != null && EnvMan.IsNight();

		private static bool Applies(Character c) =>
			c != null && !c.IsPlayer() && !c.IsTamed() && (!c.IsBoss() || S.Bosses);

		private static int _last = -1;   // -1 unknown (just joined: no message), 0 day, 1 night

		[HarmonyPatch(typeof(Player), nameof(Player.Update))]
		private static class Patch_Message
		{
			private static float _next;

			private static void Postfix(Player __instance)
			{
				if (__instance != Player.m_localPlayer || Time.time < _next) return;
				_next = Time.time + 1f;
				int now = Active ? 1 : 0;
				if (_last >= 0 && now != _last && !__instance.IsDead())
				{
					string text = now == 1 ? S.NightText : S.DawnText;
					if (!string.IsNullOrEmpty(text)) __instance.Message(MessageHud.MessageType.Center, text);
				}
				_last = now;
			}
		}

		[HarmonyPatch(typeof(Game), nameof(Game.Start))]
		private static class Patch_Reset
		{
			private static void Postfix() => _last = -1;
		}

		/// <summary>Runs on the machine that owns the creature being hit (where its damage is worked out).</summary>
		[HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
		private static class Patch_Damage
		{
			[HarmonyPriority(Priority.High)]
			private static void Prefix(Character __instance, HitData hit)
			{
				if (hit == null || !Active) return;
				try
				{
					// A creature hits anything: more damage.
					Character attacker = hit.GetAttacker();
					if (Applies(attacker) && S.Damage != 0f) hit.ApplyModifier(Mathf.Max(0f, 1f + S.Damage / 100f));
					// A creature is hit: tougher.
					if (Applies(__instance) && S.Health > -100f) hit.ApplyModifier(1f / Mathf.Max(0.01f, 1f + S.Health / 100f));
				}
				catch (Exception ex) { Plugin.Log.LogWarning($"Night: {ex.Message}"); }
			}
		}
	}
}
