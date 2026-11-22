using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace LegendaryLoot.Powers
{
	/// <summary>
	/// Projectile powers for the local player:
	///  - onShoot trigger: fires when a bow, crossbow, staff or throw launches its projectiles.
	///  - extraProjectiles: more arrows in the same volley, fanned out evenly (Echo, Eaglehorn, Windforce).
	///  - pierce: arrows fly through creatures and only stop on terrain (Valheim's own piercing setting).
	///  - explosive: arrows burst on impact, damaging nearby enemies (Explosive Shot).
	///  - meteor: a fireball falls from the sky onto the target (Skystrike).
	/// Explosions and the meteor use the Staff of Embers' fireball and its explosion, so they look native.
	/// </summary>
	public static class Projectiles
	{
		/// <summary>Remembered on each projectile fired with a power.</summary>
		public class Tag : MonoBehaviour
		{
			public float ExplosivePercent;
			public float ExplosiveRadius = 3f;
			public HitData.DamageTypes Damage;
			public Skills.SkillType Skill;
			public bool Exploded;
			public bool Pierce;
			public int PierceMax;
			public float PierceFalloff, PierceStamina;
			public int SplitCount;
			public float SplitPercent;
			public bool Split;
			public ItemDrop.ItemData HeldItem; // a piercing spear's item, held while it passes through a creature
			public Vector3 VelBefore;          // a piercing projectile flies on exactly as before (no bounce)
			public bool PassingThrough;
			public bool Recall;   // thrown weapon comes back (Thunderstroke)
			public string Summon; // creature to summon where it lands (Blobber Lobber)
			public float SummonDuration = 20f;
			public int SummonMax = 2;
			public readonly HashSet<Character> Hit = new HashSet<Character>();
		}

		// The volley being fired right now (set just before vanilla spawns its projectiles).
		private static PowerContext _volley;
		private static int _shotIndex, _shotCount;

		private class Saved
		{
			public int Projectiles;
			public float Accuracy, AccuracyMin;
		}

		[HarmonyPatch(typeof(Attack), nameof(Attack.FireProjectileBurst))]
		private static class Patch_Fire
		{
			private static void Prefix(Attack __instance, out Saved __state)
			{
				__state = null;
				_volley = null;
				if (!(__instance.m_character is Player self) || self != Player.m_localPlayer) return;
				// Bows and crossbows have no projectile of their own (the arrow or bolt supplies it), so don't
				// check for one here: vanilla works out which projectile to use itself.

				var ctx = new PowerContext { Self = self, Weapon = __instance.m_weapon, Attack = __instance };
				PowerEngine.Run("onShoot", ctx);
				if (ctx.ExtraProjectiles <= 0 && !ctx.Pierce && ctx.ExplosivePercent <= 0f && !ctx.Recall && ctx.SplitCount <= 0) return;

				__state = new Saved
				{
					Projectiles = __instance.m_projectiles,
					Accuracy = __instance.m_projectileAccuracy,
					AccuracyMin = __instance.m_projectileAccuracyMin,
				};
				if (ctx.ExtraProjectiles > 0)
				{
					__instance.m_projectiles = Mathf.Max(1, __instance.m_projectiles) + ctx.ExtraProjectiles;
					// The fan is laid out by hand below, so keep each arrow's own wobble small.
					__instance.m_projectileAccuracy = Mathf.Min(__instance.m_projectileAccuracy, 1f);
					__instance.m_projectileAccuracyMin = Mathf.Min(__instance.m_projectileAccuracyMin, 1f);
				}
				_volley = ctx;
				_shotIndex = 0;
				_shotCount = __instance.m_projectiles;
			}

			private static void Postfix(Attack __instance, Saved __state)
			{
				_volley = null;
				if (__state == null) return;
				__instance.m_projectiles = __state.Projectiles;
				__instance.m_projectileAccuracy = __state.Accuracy;
				__instance.m_projectileAccuracyMin = __state.AccuracyMin;
			}
		}

		/// <summary>Each projectile in a powered volley: fan it out, make it pierce, tag it to explode.</summary>
		[HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
		private static class Patch_Setup
		{
			private static void Postfix(Projectile __instance, Vector3 velocity, HitData hitData)
			{
				PowerContext v = _volley;
				if (v == null) return;
				int i = _shotIndex++;

				if (v.ExtraProjectiles > 0 && _shotCount > 1 && v.ExtraPercent < 100f && i != (_shotCount - 1) / 2)
					__instance.m_damage.Modify(v.ExtraPercent / 100f); // the middle one is the original, at full damage
				if (v.ExtraProjectiles > 0 && _shotCount > 1)
				{
					float spread = Mathf.Max(2f, v.Spread);
					float angle = Mathf.Lerp(-spread / 2f, spread / 2f, i / (float)(_shotCount - 1));
					Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * velocity;
					__instance.m_vel = dir;
					if (dir.sqrMagnitude > 0.001f) __instance.transform.rotation = Quaternion.LookRotation(dir);
				}
				Tag tag = null;
				if (v.Pierce || v.ExplosivePercent > 0f || v.Recall || v.SplitCount > 0) tag = __instance.gameObject.AddComponent<Tag>();
				if (v.SplitCount > 0) { tag.SplitCount = v.SplitCount; tag.SplitPercent = v.SplitPercent; }
				if (v.Recall) tag.Recall = true;
				if (v.Pierce)
				{
					__instance.m_onlyStopOnTerrain = true;
					tag.Pierce = true;
					tag.PierceMax = v.PierceMax;
					tag.PierceFalloff = v.PierceFalloff;
					tag.PierceStamina = v.PierceStamina;
				}
				if (v.ExplosivePercent > 0f && hitData != null)
				{
					tag.ExplosivePercent = v.ExplosivePercent;
					tag.ExplosiveRadius = v.ExplosiveRadius;
					tag.Damage = hitData.m_damage;
					tag.Skill = hitData.m_skill;
				}
			}
		}

		/// <summary>
		/// Piercing arrows: each creature is hit at most once (a creature can have several hit boxes), and the
		/// arrow keeps flying. Explosive arrows burst where they first hit.
		/// </summary>
		[HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
		private static class Patch_OnHit
		{
			private static bool Prefix(Projectile __instance, Collider collider)
			{
				Tag tag = __instance.GetComponent<Tag>();
				if (tag == null || !tag.Pierce || collider == null) return true;
				Character c = collider.GetComponentInParent<Character>();
				if (c == null) return true;
				if (!tag.Hit.Add(c)) return false; // already went through this one
				// Keeps flying until it has passed through 'max' creatures (so max 2 = 3 hits in all).
				__instance.m_onlyStopOnTerrain = tag.PierceMax <= 0 || tag.Hit.Count <= tag.PierceMax;
				tag.PassingThrough = __instance.m_onlyStopOnTerrain;
				tag.VelBefore = __instance.m_vel;
				if (tag.Hit.Count > 1 && tag.PierceFalloff > 0f) __instance.m_damage.Modify(1f - tag.PierceFalloff / 100f);
				if (tag.PierceStamina > 0f && __instance.m_owner is Player thrower && thrower == Player.m_localPlayer) thrower.AddStamina(tag.PierceStamina);
				// Valheim drops a thrown weapon's item at whatever it hits, before checking whether the projectile
				// flies on. Hold the item back while passing through; it drops (or returns) where the spear stops.
				if (__instance.m_onlyStopOnTerrain && __instance.m_spawnItem != null)
				{
					tag.HeldItem = __instance.m_spawnItem;
					__instance.m_spawnItem = null;
				}
				Plugin.Debug($"Pierce: hit {c.m_name} (#{tag.Hit.Count}).");
				return true;
			}

			private static void Postfix(Projectile __instance, Vector3 hitPoint)
			{
				Tag tag = __instance.GetComponent<Tag>();
				if (tag == null) return;
				if (tag.HeldItem != null)
				{
					__instance.m_spawnItem = tag.HeldItem;
					tag.HeldItem = null;
				}
				if (tag.PassingThrough)
				{
					// Some projectiles bounce off what they hit (reflected and slowed). Passing through, it should
					// carry on exactly as if nothing were there.
					tag.PassingThrough = false;
					__instance.m_vel = tag.VelBefore;
					__instance.m_didBounce = false;
					if (tag.VelBefore.sqrMagnitude > 0.001f) __instance.transform.rotation = Quaternion.LookRotation(tag.VelBefore);
				}
				if (tag.SplitCount > 0 && !tag.Split)
				{
					tag.Split = true;
					Fragments.Split(__instance, hitPoint, tag.SplitCount, tag.SplitPercent);
				}
				if (tag.Exploded) return;
				if (!string.IsNullOrEmpty(tag.Summon))
				{
					tag.Exploded = true;
					if (__instance.m_owner is Player summoner && summoner == Player.m_localPlayer)
						Summons.Spawn(summoner, tag.Summon, hitPoint + Vector3.up * 0.3f, tag.SummonDuration, 1f, 1f, 1f, tag.SummonMax, "lob:" + tag.Summon);
					return;
				}
				if (tag.ExplosivePercent <= 0f) return;
				if (!(__instance.m_owner is Player owner) || owner != Player.m_localPlayer) return;
				tag.Exploded = true;
				HitData.DamageTypes d = tag.Damage;
				d.Modify(tag.ExplosivePercent / 100f);
				Explode(owner, hitPoint, tag.ExplosiveRadius, d, tag.Skill, 0.5f);
			}
		}

		/// <summary>Damages every enemy of the owner within the radius, with a fireball explosion effect.</summary>
		public static void Explode(Player owner, Vector3 point, float radius, HitData.DamageTypes damage, Skills.SkillType skill, float effectScale)
		{
			Projectile fireball = Fireball();
			if (fireball != null && fireball.m_hitEffects != null)
				fireball.m_hitEffects.Create(point, Quaternion.identity, null, effectScale);

			var done = new HashSet<Character>();
			foreach (Collider col in Physics.OverlapSphere(point, radius))
			{
				Character c = col.GetComponentInParent<Character>();
				if (c == null || c == owner || c.IsDead() || !done.Add(c)) continue;
				if (c.IsPlayer() || !BaseAI.IsEnemy(owner, c)) continue;
				var hit = new HitData
				{
					m_damage = damage,
					m_point = c.GetCenterPoint(),
					m_dir = (c.transform.position - point).normalized,
					m_skill = skill,
					m_hitType = HitData.HitType.Undefined, // so it can't set off on-hit powers again
				};
				hit.SetAttacker(owner);
				c.Damage(hit);
			}
		}

		/// <summary>Skystrike: a big fireball falls on the target and explodes.</summary>
		public static void Meteor(Player owner, Character target, float damage)
		{
			if (owner == null || target == null || damage <= 0f) return;
			GameObject prefab = FireballPrefab();
			if (prefab == null) return;
			Vector3 aim = target.transform.position;
			Vector3 start = aim + new Vector3(Random.Range(-4f, 4f), 22f, Random.Range(-4f, 4f));
			Vector3 vel = (aim - start).normalized * 32f;
			GameObject go = Object.Instantiate(prefab, start, Quaternion.LookRotation(vel));
			go.transform.localScale *= 2.2f;
			IProjectile proj = go.GetComponent<IProjectile>();
			if (proj == null) { ZNetScene.instance.Destroy(go); return; }
			var hit = new HitData { m_hitType = HitData.HitType.Undefined, m_skill = Skills.SkillType.ElementalMagic };
			hit.m_damage.m_fire = damage * 0.6f;
			hit.m_damage.m_blunt = damage * 0.4f;
			hit.SetAttacker(owner);
			proj.Setup(owner, vel, 20f, hit, null, null);
			// Never set off another volley's powers, and always burst with a wide blast.
			if (go.GetComponent<Projectile>() is Projectile p) p.m_aoe = Mathf.Max(p.m_aoe, 4f);
		}

		private static GameObject _fireballPrefab;

		/// <summary>The Staff of Embers' fireball.</summary>
		private static GameObject FireballPrefab()
		{
			if (_fireballPrefab != null) return _fireballPrefab;
			GameObject staff = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("StaffFireball") : null;
			ItemDrop drop = staff != null ? staff.GetComponent<ItemDrop>() : null;
			_fireballPrefab = drop?.m_itemData?.m_shared?.m_attack?.m_attackProjectile;
			if (_fireballPrefab == null) Plugin.Log.LogWarning("Couldn't find the Staff of Embers fireball; meteors and explosions have no effect.");
			return _fireballPrefab;
		}

		private static Projectile Fireball()
		{
			GameObject prefab = FireballPrefab();
			return prefab != null ? prefab.GetComponent<Projectile>() : null;
		}

		/// <summary>onAttack: an attack (swing, cast, shot) has just started; refundCost can give back its cost.</summary>
		[HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
		private static class Patch_AttackStarted
		{
			private static void Postfix(Attack __instance, Humanoid character, bool __result)
			{
				if (!__result || !(character is Player self) || self != Player.m_localPlayer) return;
				PowerEngine.Run("onAttack", new PowerContext { Self = self, Weapon = __instance.m_weapon, Attack = __instance });
			}
		}
	}
}
