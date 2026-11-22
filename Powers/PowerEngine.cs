using System;
using System.Collections.Generic;
using LegendaryLoot.Core;
using LegendaryLoot.Effects;
using UnityEngine;

namespace LegendaryLoot.Powers
{
	/// <summary>Everything a power needs to know about the moment it fires.</summary>
	public class PowerContext
	{
		public Player Self;
		public Character Target;        // the creature hit / hitting us (may be null for kills reported from another game)
		public HitData Hit;             // the hit being dealt or taken; effects may change it
		public Attack Attack;           // onAttack: the attack being started
		public ItemDrop.ItemData Weapon; // weapon used for the hit or kill
		public float Damage;            // total damage of the hit before armor
		public bool TargetIsBoss;
		public int TargetStars;
		public float TargetHealthPercent = 1f;

		// Results effects can set
		public bool ForceCrit;
		public bool PreventDeath;

		// onShoot results: what this volley gets
		public int ExtraProjectiles;
		public float Spread;            // degrees across the whole fan
		public float ExtraPercent = 100f; // damage of the extra projectiles (the original keeps full damage)
		public bool Pierce;
		public float ExplosivePercent;
		public float ExplosiveRadius = 3f;
		public bool Recall;
		public int PierceMax;           // pierce: stop after this many creatures (0 = no limit)
		public float PierceFalloff;     // pierce: % less damage after each creature
		public float PierceStamina;     // pierce: stamina back for each creature hit
		public int SplitCount;          // split: fragments on impact
		public float SplitPercent;

		// Where it happened (the target, or where a creature died), and whether the hit came from a thrown weapon.
		public Vector3 Position;
		public bool FromThrow;
	}

	/// <summary>Loaded powers.yml. A power only counts as working if every trigger and effect in it is known.</summary>
	public static class PowerStore
	{
		public static readonly Dictionary<string, List<PowerDef>> Affixes = new Dictionary<string, List<PowerDef>>(StringComparer.OrdinalIgnoreCase);
		public static readonly Dictionary<string, List<PowerDef>> Uniques = new Dictionary<string, List<PowerDef>>(StringComparer.OrdinalIgnoreCase);

		public static bool HasAffix(string id) => Affixes.ContainsKey(id);
		public static bool HasUnique(string id) => Uniques.ContainsKey(id);

		public static void Load(PowersFile file)
		{
			Affixes.Clear();
			Uniques.Clear();
			if (file == null) return;
			Add(file.Affixes, Affixes, "affix");
			Add(file.Uniques, Uniques, "Unique");
			Plugin.Log.LogInfo($"Powers loaded: {Affixes.Count} affixes, {Uniques.Count} Uniques.");
		}

		private static void Add(Dictionary<string, List<PowerDef>> from, Dictionary<string, List<PowerDef>> to, string kind)
		{
			if (from == null) return;
			foreach (KeyValuePair<string, List<PowerDef>> kv in from)
			{
				if (kv.Value == null || kv.Value.Count == 0) continue;
				bool ok = true;
				for (int i = 0; i < kv.Value.Count; i++)
				{
					PowerDef p = kv.Value[i];
					p.Key = $"{kind}:{kv.Key}:{i}";
					p.Conditions = p.Conditions ?? new Dictionary<string, string>();
					p.Effects = p.Effects ?? new List<Dictionary<string, string>>();
					if (!PowerEngine.KnownTriggers.Contains(p.Trigger ?? ""))
					{
						Plugin.Log.LogWarning($"powers.yml: {kind} {kv.Key} uses unknown trigger '{p.Trigger}', so it stays switched off.");
						ok = false;
					}
					foreach (Dictionary<string, string> e in p.Effects)
					{
						string type = PowerMath.GetText(e, "type", "");
						if (!PowerEngine.KnownEffects.Contains(type))
						{
							Plugin.Log.LogWarning($"powers.yml: {kind} {kv.Key} uses unknown effect '{type}', so it stays switched off.");
							ok = false;
						}
					}
					foreach (string c in p.Conditions.Keys)
					{
						if (!PowerEngine.KnownConditions.Contains(c))
						{
							Plugin.Log.LogWarning($"powers.yml: {kind} {kv.Key} uses unknown condition '{c}', so it stays switched off.");
							ok = false;
						}
					}
				}
				if (ok) to[kv.Key] = kv.Value;
			}
		}
	}

	/// <summary>Runs powers from the local player's equipped gear when their trigger happens.</summary>
	public static class PowerEngine
	{
		public static readonly HashSet<string> KnownTriggers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
			{ "passive", "onHit", "onKill", "onShoot", "onAttack", "whenHit", "onBlock", "onParry", "onLethal", "onDodge", "every" };

		public static readonly HashSet<string> KnownEffects = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"stat", "heal", "stamina", "addDamage", "sneakDamage", "backstabBonus", "forceCrit", "currentHealthDamage",
			"slow", "paralyze", "bleed", "vulnerable", "reflect", "reduceDamage", "buff", "preventDeath", "immunity",
			"extraProjectiles", "pierce", "explosive", "meteor",
			"nova", "strike", "chainLightning", "lob", "slowNearby", "zone", "seasons", "elementalDamage", "recall", "slowAura",
			"summon", "extraAttack",
			"adrenaline", "storeBurst", "drain", "blink", "refundCost", "split", "vanish", "feathers", "shield", "root", "coinDamage",
		};

		public static readonly HashSet<string> KnownConditions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"notBoss", "targetBoss", "targetMinStars", "targetBelowHealth", "targetFullHealth", "targetStaggered",
			"targetSlowed", "firstHitBelowHealth", "selfBelowHealth", "selfStaminaAbove", "selfMoving", "selfStillFor",
			"fromBehind", "secondaryAttack", "weaponDamage", "comboHit", "thrown", "everyN", "inCombat", "targetRooted",
		};

		private static readonly Dictionary<string, float> CooldownUntil = new Dictionary<string, float>();
		private static readonly Dictionary<string, float> NextEvery = new Dictionary<string, float>();
		private static readonly HashSet<ZDOID> Executed = new HashSet<ZDOID>();
		public static float LastMovedTime;

		/// <summary>Fires every power on the player's gear that listens for this trigger.</summary>
		public static void Run(string trigger, PowerContext ctx)
		{
			Player self = ctx.Self;
			if (self == null) return;
			foreach (ItemDrop.ItemData item in PlayerStats.EquippedItems(self))
			{
				MagicItem magic = item.GetMagic();
				if (magic == null) continue;

				// Weapon powers only fire for hits and kills made with that weapon; armor and trinket powers fire for any.
				bool isWeapon = ItemClassifier.IsWeaponCategory(ItemClassifier.Classify(item.m_shared));
				bool weaponTrigger = trigger == "onHit" || trigger == "onKill" || trigger == "onShoot" || trigger == "onAttack";
				if (isWeapon && weaponTrigger && ctx.Weapon != null && ctx.Weapon != item) continue;

				foreach (RolledAffix a in magic.Affixes)
					if (PowerStore.Affixes.TryGetValue(a.Id, out List<PowerDef> powers))
						RunList(powers, trigger, ctx, a.Value);
				if (magic.IsUnique && PowerStore.Uniques.TryGetValue(magic.UniqueId, out List<PowerDef> uPowers))
					RunList(uPowers, trigger, ctx, 0f);
			}
		}

		private static readonly Dictionary<string, int> Counters = new Dictionary<string, int>();

		private static bool RunList(List<PowerDef> powers, string trigger, PowerContext ctx, float x)
		{
			bool fired = false;
			foreach (PowerDef p in powers)
			{
				if (!string.Equals(p.Trigger, trigger, StringComparison.OrdinalIgnoreCase)) continue;
				if (!Ready(p, x)) continue;
				if (!ConditionsMet(p, ctx, x)) continue;
				// everyN: only every Nth time (e.g. every 5th block).
				if (p.Conditions.TryGetValue("everyN", out string everyN))
				{
					int need = Mathf.Max(1, Mathf.RoundToInt(PowerMath.Eval(everyN, x, 1f)));
					Counters.TryGetValue(p.Key, out int count);
					count++;
					if (count < need) { Counters[p.Key] = count; continue; }
					Counters[p.Key] = 0;
				}
				if (!string.IsNullOrEmpty(p.Chance) && UnityEngine.Random.value * 100f >= PowerMath.Eval(p.Chance, x, 100f)) continue;
				StartCooldown(p, x);
				foreach (Dictionary<string, string> e in p.Effects) Apply(e, ctx, x);
				fired = true;
			}
			return fired;
		}

		private static bool Ready(PowerDef p, float x)
		{
			return !CooldownUntil.TryGetValue(p.Key, out float until) || Time.time >= until;
		}

		private static readonly Dictionary<string, int> UsesLeft = new Dictionary<string, int>();

		private static void StartCooldown(PowerDef p, float x)
		{
			float cd = PowerMath.Eval(p.Cooldown, x, 0f);
			if (cd <= 0f) return;
			// With charges, the cooldown only starts once they're all used.
			int charges = Mathf.Max(1, Mathf.RoundToInt(PowerMath.Eval(p.Charges, x, 1f)));
			if (charges > 1)
			{
				if (!UsesLeft.TryGetValue(p.Key, out int left)) left = charges;
				left--;
				if (left > 0) { UsesLeft[p.Key] = left; return; }
				UsesLeft.Remove(p.Key);
			}
			CooldownUntil[p.Key] = Time.time + cd;
		}

		// Conditions --------------------------------------------------------------------------------

		private static bool ConditionsMet(PowerDef p, PowerContext ctx, float x)
		{
			foreach (KeyValuePair<string, string> c in p.Conditions)
			{
				// Yes/no conditions ("targetStaggered: true") aren't numbers: don't try to read them as one.
				string raw = (c.Value ?? "").Trim();
				float n = raw.Equals("true", StringComparison.OrdinalIgnoreCase) ? 1f
					: raw.Equals("false", StringComparison.OrdinalIgnoreCase) ? 0f
					: PowerMath.Eval(raw, x, 0f);
				Character t = ctx.Target;
				Player self = ctx.Self;
				switch (c.Key)
				{
					case "notBoss": if (ctx.TargetIsBoss) return false; break;
					case "targetBoss": if (!ctx.TargetIsBoss) return false; break;
					case "targetMinStars": if (ctx.TargetStars < n) return false; break;
					case "targetBelowHealth": if (ctx.TargetHealthPercent * 100f >= n) return false; break;
					case "targetFullHealth": if (ctx.TargetHealthPercent < 0.999f) return false; break;
					case "targetStaggered": if (t == null || !t.IsStaggering()) return false; break;
					case "targetSlowed":
						if (t == null) return false;
						if (!t.IsStaggering() && !Debuffs.Has(t, DebuffKind.Slow) && !Debuffs.Has(t, DebuffKind.Paralyze)) return false;
						break;
					case "firstHitBelowHealth":
						if (t == null || t.m_nview == null || ctx.TargetHealthPercent * 100f >= n) return false;
						ZDOID id = t.m_nview.GetZDO()?.m_uid ?? ZDOID.None;
						if (Executed.Contains(id)) return false;
						if (Executed.Count > 512) Executed.Clear();
						Executed.Add(id);
						break;
					case "selfBelowHealth": if (self.GetHealthPercentage() * 100f >= n) return false; break;
					case "selfStaminaAbove": if (self.GetStamina() / Mathf.Max(1f, self.GetMaxStamina()) * 100f <= n) return false; break;
					case "selfMoving": if (self.GetVelocity().magnitude < 0.5f) return false; break;
					case "selfStillFor": if (Time.time - LastMovedTime < n) return false; break;
					case "fromBehind":
						if (t == null) return false;
						Vector3 toAttacker = t.transform.position - self.transform.position;
						if (Vector3.Dot(self.transform.forward, toAttacker) > 0f) return false;
						break;
					case "secondaryAttack": if (!self.m_currentAttackIsSecondary) return false; break;
					case "comboHit":
					{
						// The Nth swing of a combo (1 = first).
						Attack a = self.m_currentAttack;
						if (a == null || a.m_currentAttackCainLevel != Mathf.RoundToInt(n) - 1) return false;
						break;
					}
					case "thrown": if (!ctx.FromThrow) return false; break;
					case "everyN": break; // counted in RunList, once everything else passes
					case "targetRooted": if (t == null || !Debuffs.Has(t, DebuffKind.Paralyze)) return false; break;
					case "inCombat":
					{
						// An enemy that's alerted is within this many metres (default 25).
						bool fighting = false;
						foreach (Character foe in Areas.EnemiesNear(self, self.transform.position, n > 0f ? n : 25f))
						{
							BaseAI ai = foe.GetBaseAI();
							if (ai != null && ai.IsAlerted()) { fighting = true; break; }
						}
						if (!fighting) return false;
						break;
					}
					case "weaponDamage":
						if (ctx.Weapon == null || !WeaponHas(ctx.Weapon, c.Value)) return false;
						break;
				}
			}
			return true;
		}

		private static bool WeaponHas(ItemDrop.ItemData weapon, string type)
		{
			HitData.DamageTypes d = weapon.GetDamage();
			switch ((type ?? "").ToLowerInvariant())
			{
				case "fire": return d.m_fire > 0f;
				case "frost": return d.m_frost > 0f;
				case "lightning": return d.m_lightning > 0f;
				case "poison": return d.m_poison > 0f;
				case "spirit": return d.m_spirit > 0f;
				case "blunt": return d.m_blunt > 0f;
				case "slash": return d.m_slash > 0f;
				case "pierce": return d.m_pierce > 0f;
				default: return false;
			}
		}

		// Effects -----------------------------------------------------------------------------------

		private static void Apply(Dictionary<string, string> e, PowerContext ctx, float x)
		{
			string type = PowerMath.GetText(e, "type", "");
			Player self = ctx.Self;
			Character target = ctx.Target;
			switch (type)
			{
				case "stat":
				case "immunity":
				case "slowAura":
				case "feathers":
					break; // passive; read by PlayerStats

				case "heal":
				{
					float hp = PowerMath.Get(e, "amount", x)
					           + ctx.Damage * PowerMath.Get(e, "percentOfDamage", x) / 100f
					           + self.GetMaxHealth() * PowerMath.Get(e, "percentOfMaxHealth", x) / 100f;
					if (hp > 0f) self.Heal(hp, true);
					break;
				}
				case "stamina":
					self.AddStamina(PowerMath.Get(e, "amount", x));
					break;

				case "addDamage":
					if (ctx.Hit != null) ctx.Hit.m_damage.Modify(1f + PowerMath.Get(e, "percent", x) / 100f);
					break;
				case "sneakDamage":
					// Deal this hit as if it were a sneak attack (the weapon's backstab multiplier).
					if (ctx.Hit != null && ctx.Weapon != null) ctx.Hit.m_damage.Modify(Mathf.Max(1f, ctx.Weapon.m_shared.m_backstabBonus));
					break;
				case "backstabBonus":
					if (ctx.Hit != null) ctx.Hit.m_backstabBonus *= 1f + PowerMath.Get(e, "percent", x) / 100f;
					break;
				case "forceCrit":
					ctx.ForceCrit = true;
					break;
				case "currentHealthDamage":
					if (ctx.Hit != null && target != null)
					{
						float pct = ctx.TargetIsBoss ? PowerMath.Get(e, "bossPercent", x, PowerMath.Get(e, "percent", x)) : PowerMath.Get(e, "percent", x);
						ctx.Hit.m_damage.m_damage += target.GetHealth() * pct / 100f;
					}
					break;

				case "slow":
					if (target != null && !(PowerMath.GetText(e, "bosses") != "true" && ctx.TargetIsBoss))
						Debuffs.Send(target, DebuffKind.Slow, PowerMath.Get(e, "percent", x), PowerMath.Get(e, "duration", x, 2f));
					break;
				case "paralyze":
					if (target != null && !ctx.TargetIsBoss)
						Debuffs.Send(target, DebuffKind.Paralyze, 100f, PowerMath.Get(e, "duration", x, 1f));
					break;
				case "bleed":
					if (target != null)
					{
						float total = ctx.Damage * PowerMath.Get(e, "percentOfDamage", x) / 100f;
						Debuffs.Send(target, DebuffKind.Bleed, total, PowerMath.Get(e, "duration", x, 2f), (int)PowerMath.Get(e, "maxStacks", x, 1f));
					}
					break;
				case "vulnerable":
					if (target != null)
						Debuffs.Send(target, DebuffKind.Vulnerable, PowerMath.Get(e, "percent", x), PowerMath.Get(e, "duration", x, 5f));
					break;

				case "reflect":
					if (target != null && !target.IsDead())
					{
						var back = new HitData { m_attacker = self.GetZDOID(), m_point = target.GetCenterPoint(), m_hitType = HitData.HitType.Undefined };
						back.m_damage.m_damage = ctx.Damage * PowerMath.Get(e, "percentOfDamage", x) / 100f;
						if (back.m_damage.m_damage > 0f) target.Damage(back);
					}
					break;
				case "reduceDamage":
					if (ctx.Hit != null) ctx.Hit.m_damage.Modify(1f - Mathf.Clamp01(PowerMath.Get(e, "percent", x) / 100f));
					break;

				case "buff":
					Buffs.Add(PowerMath.GetText(e, "stat", ""), PowerMath.Get(e, "value", x), PowerMath.Get(e, "duration", x, 5f),
						(int)PowerMath.Get(e, "maxStacks", x, 1f));
					break;
				case "preventDeath":
					ctx.PreventDeath = true;
					break;

				case "extraProjectiles":
					// Several powers can add to the same volley; the widest fan wins.
					ctx.ExtraProjectiles += Mathf.Max(0, Mathf.RoundToInt(PowerMath.Get(e, "count", x, 2f)));
					ctx.Spread = Mathf.Max(ctx.Spread, PowerMath.Get(e, "spread", x, 10f));
					ctx.ExtraPercent = Mathf.Min(ctx.ExtraPercent, PowerMath.Get(e, "percent", x, 100f));
					if (PowerMath.GetText(e, "pierce") == "true") ctx.Pierce = true;
					break;
				case "pierce":
					ctx.Pierce = true;
					ctx.PierceMax = Mathf.Max(ctx.PierceMax, Mathf.RoundToInt(PowerMath.Get(e, "max", x)));
					ctx.PierceFalloff = Mathf.Max(ctx.PierceFalloff, PowerMath.Get(e, "falloff", x));
					ctx.PierceStamina += PowerMath.Get(e, "staminaPerHit", x);
					break;
				case "explosive":
					ctx.ExplosivePercent += PowerMath.Get(e, "percent", x);
					ctx.ExplosiveRadius = Mathf.Max(ctx.ExplosiveRadius, PowerMath.Get(e, "radius", x, 3f));
					break;
				case "nova":
				{
					// Damage around you (at: self) or around the target (at: target).
					string at = PowerMath.GetText(e, "at", "self");
					float novaRadius = PowerMath.Get(e, "radius", x, 4f);
					Vector3 point = at == "target" && target != null ? target.transform.position
						: at == "kill" && ctx.Position != Vector3.zero ? ctx.Position
						: at == "ahead" ? self.transform.position + self.transform.forward * novaRadius // a blast in front of you
						: self.transform.position;
					float amount = PowerMath.Get(e, "amount", x) + ctx.Damage * PowerMath.Get(e, "percentOfDamage", x) / 100f;
					float novaBlock = PowerMath.Get(e, "percentOfBlockPower", x);
					if (novaBlock > 0f)
					{
						ItemDrop.ItemData blocker = self.GetCurrentBlocker();
						if (blocker != null) amount += blocker.GetBlockPower(self.GetSkillFactor(Skills.SkillType.Blocking)) * novaBlock / 100f;
					}
					Areas.Nova(self, point, novaRadius, Areas.Typed(PowerMath.GetText(e, "damageType", ""), amount),
						PowerMath.Get(e, "stagger", x), PowerMath.GetText(e, "fx", "fire"), WeaponSkill(ctx), PowerMath.GetText(e, "sound"));
					break;
				}
				case "strike":
				{
					if (target == null || target.IsDead()) break;
					float amount = PowerMath.Get(e, "amount", x) + ctx.Damage * PowerMath.Get(e, "percentOfDamage", x) / 100f;
					float pctBlock = PowerMath.Get(e, "percentOfBlockPower", x);
					if (pctBlock > 0f)
					{
						ItemDrop.ItemData blocker = self.GetCurrentBlocker();
						if (blocker != null) amount += blocker.GetBlockPower(self.GetSkillFactor(Skills.SkillType.Blocking)) * pctBlock / 100f;
					}
					string fx = PowerMath.GetText(e, "fx", "lightning");
					if (fx == "lightning") Bolt.Draw(target.GetCenterPoint() + Vector3.up * 6f, target.GetCenterPoint());
					Fx.Play(fx, target.GetCenterPoint(), 0.7f);
					Areas.HitOne(self, target, Areas.Typed(PowerMath.GetText(e, "damageType", "lightning"), amount), self.transform.position,
						PowerMath.Get(e, "stagger", x), WeaponSkill(ctx));
					break;
				}
				case "chainLightning":
				{
					if (target == null) break;
					float amount = ctx.Damage * PowerMath.Get(e, "percentOfDamage", x, 30f) / 100f;
					int casts = ctx.FromThrow ? Mathf.Max(1, Mathf.RoundToInt(PowerMath.Get(e, "thrownCasts", x, 1f))) : 1;
					for (int i = 0; i < casts; i++)
						Areas.ChainLightning(self, target, amount, Mathf.RoundToInt(PowerMath.Get(e, "jumps", x, 3f)), PowerMath.Get(e, "range", x, 8f));
					break;
				}
				case "lob":
				{
					Vector3 aim = target != null ? target.transform.position : self.transform.position + self.transform.forward * 8f;
					string items = PowerMath.GetText(e, "item", "BombOoze");
					Areas.Lob(self, aim, items.Split('|'), PowerMath.GetText(e, "summon"),
						PowerMath.Get(e, "duration", x, 20f), Mathf.RoundToInt(PowerMath.Get(e, "max", x, 2f)));
					break;
				}
				case "slowNearby":
					foreach (Character c in Areas.EnemiesNear(self, self.transform.position, PowerMath.Get(e, "radius", x, 5f)))
					{
						if (c.IsBoss() && PowerMath.GetText(e, "bosses") != "true") continue;
						Debuffs.Send(c, DebuffKind.Slow, PowerMath.Get(e, "percent", x, 20f), PowerMath.Get(e, "duration", x, 1.5f));
					}
					break;
				case "zone":
				{
					Vector3 center = ctx.Position != Vector3.zero ? ctx.Position : self.transform.position;
					Areas.AddZone(center, PowerMath.Get(e, "radius", x, 5f), PowerMath.Get(e, "duration", x, 6f),
						PowerMath.GetText(e, "stat", "damage"), PowerMath.Get(e, "value", x, 20f));
					break;
				}
				case "seasons":
					if (ctx.Hit != null)
						Areas.BoostType(ref ctx.Hit.m_damage, Areas.CurrentSeason(PowerMath.Get(e, "period", x, 5f)), 1f + PowerMath.Get(e, "percent", x, 25f) / 100f);
					break;
				case "elementalDamage":
					if (ctx.Hit != null)
					{
						HitData.DamageTypes add = Areas.Typed(PowerMath.GetText(e, "damageType", "fire"), ctx.Damage * PowerMath.Get(e, "percent", x) / 100f);
						ctx.Hit.m_damage.Add(add);
					}
					break;
				case "recall":
					ctx.Recall = true;
					break;

				case "extraAttack":
					ExtraAttacks.Strike(self, target, ctx.Hit, Mathf.RoundToInt(PowerMath.Get(e, "count", x, 1f)), PowerMath.GetText(e, "fx", "thrasher"));
					break;

				case "adrenaline":
					self.AddAdrenaline(PowerMath.Get(e, "amount", x, 10f));
					break;
				case "storeBurst":
					if (target != null)
						Specials.StoreBurst(self, target, ctx.Damage * PowerMath.Get(e, "percent", x, 30f) / 100f,
							PowerMath.Get(e, "delay", x, 3f), PowerMath.Get(e, "radius", x, 3f),
							PowerMath.GetText(e, "fx", "fire"), PowerMath.Get(e, "fxScale", x, 1f), PowerMath.GetText(e, "sound"));
					break;
				case "drain":
					if (target != null)
						Debuffs.Send(target, DebuffKind.Drained, PowerMath.Get(e, "heal", x, 5f), PowerMath.Get(e, "duration", x, 3f));
					break;
				case "blink":
					Specials.Blink(self, PowerMath.Get(e, "distance", x, 8f), target);
					break;
				case "refundCost":
					Specials.Refund(self, ctx.Attack, PowerMath.GetText(e, "resource", "eitr"));
					break;
				case "split":
					ctx.SplitCount = Mathf.Max(ctx.SplitCount, Mathf.RoundToInt(PowerMath.Get(e, "count", x, 3f)));
					ctx.SplitPercent = Mathf.Max(ctx.SplitPercent, PowerMath.Get(e, "percent", x, 30f));
					break;
				case "vanish":
					Specials.Vanish(self, PowerMath.Get(e, "duration", x, 4f), PowerMath.Get(e, "radius", x, 30f),
						PowerMath.GetText(e, "fx", "poison"), PowerMath.GetText(e, "sound"));
					break;
				case "shield":
					Specials.Shield(self, PowerMath.Get(e, "percentMaxHealth", x, 10f), PowerMath.Get(e, "duration", x, 15f));
					break;
				case "root":
				{
					float r = PowerMath.Get(e, "radius", x, 10f), d = PowerMath.Get(e, "duration", x, 8f);
					Plugin.Debug($"Root: enemies within {r} m for {d} s");
					Fx.Broadcast(PowerMath.GetText(e, "fx", "poison"), self.transform.position, 1f);
					Specials.Sound(PowerMath.GetText(e, "sound"), self.transform.position);
					foreach (Character c in Areas.EnemiesNear(self, self.transform.position, r))
					{
						if (c.IsBoss()) continue;
						Debuffs.Send(c, DebuffKind.Paralyze, 0f, d);
					}
					break;
				}
				case "coinDamage":
					if (ctx.Hit != null)
						ctx.Hit.m_damage.Modify(1f + Specials.CoinBonus(self, PowerMath.Get(e, "max", x, 25f), PowerMath.Get(e, "scale", x, 1000f)) / 100f);
					break;

				case "summon":
				{
					// Where: beside you (self), at the target, or where a creature died (kill).
					string at = PowerMath.GetText(e, "at", "self");
					Vector3 basePos = at == "target" && target != null ? target.transform.position
						: at == "kill" && ctx.Position != Vector3.zero ? ctx.Position
						: self.transform.position + self.transform.right * 1.5f - self.transform.forward * 1.5f;
					int count = Mathf.Max(1, Mathf.RoundToInt(PowerMath.Get(e, "count", x, 1f)));
					string creature = PowerMath.GetText(e, "creature", "Wolf");
					float hitDamage = 0f;
					float pctWeapon = PowerMath.Get(e, "weaponDamagePercent", x);
					if (pctWeapon > 0f)
					{
						ItemDrop.ItemData w = ctx.Weapon ?? self.GetCurrentWeapon();
						if (w != null) hitDamage = w.GetDamage().GetTotalDamage() * pctWeapon / 100f;
					}
					for (int i = 0; i < count; i++)
					{
						Vector2 r = UnityEngine.Random.insideUnitCircle * (count > 1 ? 1.5f : 0.5f);
						Summons.Spawn(self, creature, basePos + new Vector3(r.x, 0.5f, r.y),
							PowerMath.Get(e, "duration", x, 20f), PowerMath.Get(e, "damageMult", x, 1f), PowerMath.Get(e, "healthMult", x, 1f),
							PowerMath.Get(e, "scale", x, 1f), Mathf.RoundToInt(PowerMath.Get(e, "max", x, count)),
							PowerMath.GetText(e, "group", creature), hitDamage, PowerMath.GetText(e, "invulnerable") == "true",
							PowerMath.GetText(e, "look"));
					}
					if (PowerMath.GetText(e, "look") == "raven") RavenSound.Broadcast(RavenSound.File, basePos); // one caw as they arrive
					break;
				}

				case "meteor":
					if (target != null && !target.IsDead())
						Projectiles.Meteor(self, target, ctx.Damage * PowerMath.Get(e, "percentOfDamage", x, 150f) / 100f);
					break;
			}
		}

		/// <summary>The first effect of this type on the player's gear (and the rolled value it uses).</summary>
		public static bool FindEffect(Player p, string type, out Dictionary<string, string> effect, out float x)
		{
			effect = null;
			x = 0f;
			foreach (ItemDrop.ItemData item in PlayerStats.EquippedItems(p))
			{
				MagicItem magic = item.GetMagic();
				if (magic == null) continue;
				if (magic.IsUnique && PowerStore.Uniques.TryGetValue(magic.UniqueId, out List<PowerDef> u) && Find(u, type, out effect)) return true;
				foreach (RolledAffix a in magic.Affixes)
					if (PowerStore.Affixes.TryGetValue(a.Id, out List<PowerDef> list) && Find(list, type, out effect)) { x = a.Value; return true; }
			}
			return false;
		}

		private static bool Find(List<PowerDef> powers, string type, out Dictionary<string, string> effect)
		{
			foreach (PowerDef p in powers)
				foreach (Dictionary<string, string> e in p.Effects)
					if (string.Equals(PowerMath.GetText(e, "type", ""), type, StringComparison.OrdinalIgnoreCase)) { effect = e; return true; }
			effect = null;
			return false;
		}

		private static Skills.SkillType WeaponSkill(PowerContext ctx)
		{
			return ctx.Weapon != null ? ctx.Weapon.m_shared.m_skillType : Skills.SkillType.None;
		}

		/// <summary>Passive "stat" and "immunity" effects from Uniques (and passive affixes), added into the player's stat totals.</summary>
		public static void AddPassiveStats(MagicItem magic, Dictionary<string, float> totals)
		{
			if (magic.IsUnique && PowerStore.Uniques.TryGetValue(magic.UniqueId, out List<PowerDef> u)) AddPassives(u, 0f, totals);
			foreach (RolledAffix a in magic.Affixes)
				if (PowerStore.Affixes.TryGetValue(a.Id, out List<PowerDef> p)) AddPassives(p, a.Value, totals);
		}

		private static void AddPassives(List<PowerDef> powers, float x, Dictionary<string, float> totals)
		{
			foreach (PowerDef p in powers)
			{
				if (!string.Equals(p.Trigger, "passive", StringComparison.OrdinalIgnoreCase)) continue;
				foreach (Dictionary<string, string> e in p.Effects)
				{
					string type = PowerMath.GetText(e, "type", "");
					if (type == "slowAura")
					{
						// Biggest radius wins; slows add up (capped when used).
						totals.TryGetValue("SlowAuraRadius", out float r);
						totals["SlowAuraRadius"] = Mathf.Max(r, PowerMath.Get(e, "radius", x, 5f));
						totals.TryGetValue("SlowAuraPercent", out float sp);
						totals["SlowAuraPercent"] = sp + PowerMath.Get(e, "percent", x, 20f);
						continue;
					}
					string stat = type == "immunity" ? "Immune" + PowerMath.GetText(e, "to", "") : PowerMath.GetText(e, "stat", null);
					if (string.IsNullOrEmpty(stat)) continue;
					float v = type == "immunity" ? 1f : PowerMath.Get(e, "value", x);
					totals.TryGetValue(stat, out float cur);
					totals[stat] = cur + v;
				}
			}
		}

		/// <summary>Called every frame for the local player: "every" powers, and movement tracking for selfStillFor.</summary>
		public static void Tick(Player self)
		{
			if (self.GetVelocity().magnitude > 0.5f) LastMovedTime = Time.time;
			Areas.AuraTick(self);
			Specials.Tick(self);
			SeasonMotes.Tick(self, FindEffect(self, "seasons", out Dictionary<string, string> season, out float sx)
				? Mathf.Max(1f, PowerMath.Get(season, "period", sx, 5f)) : 0f);
			foreach (ItemDrop.ItemData item in PlayerStats.EquippedItems(self))
			{
				MagicItem magic = item.GetMagic();
				if (magic == null) continue;
				foreach (RolledAffix a in magic.Affixes)
					if (PowerStore.Affixes.TryGetValue(a.Id, out List<PowerDef> powers)) TickList(powers, self, a.Value);
				if (magic.IsUnique && PowerStore.Uniques.TryGetValue(magic.UniqueId, out List<PowerDef> u)) TickList(u, self, 0f);
			}
		}

		/// <summary>Seconds for "every": a number, or a range "180-210" picked at random each time.</summary>
		private static float Interval(string every, float x)
		{
			if (!string.IsNullOrEmpty(every))
			{
				string[] parts = every.Split('-');
				if (parts.Length == 2 && float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float lo)
				    && float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hi))
					return Mathf.Max(0.25f, UnityEngine.Random.Range(Mathf.Min(lo, hi), Mathf.Max(lo, hi)));
			}
			return Mathf.Max(0.25f, PowerMath.Eval(every, x, 10f));
		}

		private static void TickList(List<PowerDef> powers, Player self, float x)
		{
			foreach (PowerDef p in powers)
			{
				if (!string.Equals(p.Trigger, "every", StringComparison.OrdinalIgnoreCase)) continue;
				if (!NextEvery.TryGetValue(p.Key, out float next)) { NextEvery[p.Key] = Time.time + Interval(p.Every, x); continue; }
				if (Time.time < next) continue;
				// When it's due, it waits for its conditions (e.g. inCombat), checking twice a second, then fires.
				if (RunList(new List<PowerDef> { p }, "every", new PowerContext { Self = self }, x))
					NextEvery[p.Key] = Time.time + Interval(p.Every, x);
				else
					NextEvery[p.Key] = Time.time + 0.5f;
			}
		}
	}
}
