using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using LegendaryLoot.Core;
using LegendaryLoot.Data;
using LegendaryLoot.Effects;
using LegendaryLoot.Powers;
using UnityEngine;

namespace LegendaryLoot
{
	/// <summary>
	/// "Character Sheet" page at the top of the Compendium (the same menu as Active Effects and Stats):
	/// offense, defense, resources, movement, Unique powers, active buffs, and every bonus from gear.
	/// It's rebuilt each time the Compendium opens, so it always shows the gear you're wearing.
	/// </summary>
	[HarmonyPatch(typeof(TextsDialog), nameof(TextsDialog.UpdateTextsList))]
	public static class CharacterSheet
	{
		public const string Title = "Character Sheet";
		private const string Header = "#FFB21E";
		private const string Muted = "#9a9a9a";

		private static void Postfix(TextsDialog __instance)
		{
			Player p = Player.m_localPlayer;
			if (p == null || __instance.m_texts == null) return;
			__instance.m_texts.Insert(0, new TextsDialog.TextInfo(Title, Build(p)));
		}

		public static string Build(Player p)
		{
			var sb = new StringBuilder();

			// Active effects: short-lived buffs from powers (they change the numbers below while they last) --
			var buffs = Buffs.Current();
			float zone = Areas.ZoneBonus(p, "damage");
			if (buffs.Count > 0 || zone > 0f || Specials.Feathers > 0)
			{
				Section(sb, "Active effects");
				foreach (var b in buffs)
				{
					string stacks = b.Stacks > 1 ? $" ({b.Stacks} stacks)" : "";
					Line(sb, BuffName(b.Stat), BuffValue(b.Stat, b.Value) + stacks + $" <color={Muted}>{Mathf.CeilToInt(b.Left)} s</color>");
				}
				if (zone > 0f) Line(sb, "Circle of power", "+" + Pct(zone) + " damage");
				if (Specials.Feathers > 0) Line(sb, "Raven Mantle feathers", Specials.Feathers.ToString());
			}

			// Offense ------------------------------------------------------------------------------
			Section(sb, "Offense");
			ItemDrop.ItemData weapon = p.GetCurrentWeapon();
			if (weapon != null && weapon.m_shared.m_name != "$item_unarmed")
			{
				Line(sb, "Weapon", Localization.instance.Localize(weapon.m_shared.m_name));
				HitData.DamageTypes d = weapon.GetDamage();
				Line(sb, "Weapon damage", Num(d.GetTotalDamage()) + DamageBreakdown(d));
			}
			float critChance = Crits.Chance(p);
			Line(sb, "Critical strike chance", Pct(critChance) + Cap(critChance >= Crits.MaxChance, Crits.MaxChance + "%"));
			float critMult = Crits.Multiplier(p);
			Line(sb, "Critical strike damage", $"{critMult.ToString("0.##", CultureInfo.InvariantCulture)}x" + Cap(critMult >= Crits.MaxMultiplier, Crits.MaxMultiplier + "x"));
			// Rune of Seasons: which element is boosted right now.
			foreach (ItemDrop.ItemData item in PlayerStats.EquippedItems(p))
			{
				MagicItem m = item.GetMagic();
				if (m == null || !m.IsUnique || m.UniqueId != "RuneofSeasons") continue;
				string season = Areas.CurrentSeason(5f);
				Line(sb, "Rune of Seasons", char.ToUpperInvariant(season[0]) + season.Substring(1) + " +25%");
				break;
			}
			// Mastery: skill levels past 100 from +skill gear.
			Skills.SkillType wSkill = weapon != null ? weapon.m_shared.m_skillType : Skills.SkillType.Unarmed;
			float wLevel = p.GetSkills().GetSkillLevel(wSkill);
			if (wLevel > 100f)
			{
				string what = wSkill == Skills.SkillType.Bows ? "draw speed" : wSkill == Skills.SkillType.Crossbows ? "reload speed" : "attack speed";
				Line(sb, "Weapon mastery", $"{Num(wLevel)} {wSkill}: +{Pct(AttackSpeed.MasteryBonus(p, wSkill))} {what}");
			}
			float blockLevel = p.GetSkills().GetSkillLevel(Skills.SkillType.Blocking);
			if (blockLevel > 100f) Line(sb, "Blocking mastery", $"{Num(blockLevel)}: +{Pct(AttackSpeed.MasteryBonus(p, Skills.SkillType.Blocking))} block power");
			float atk = AttackSpeed.AttackBonus(p);
			if (Mathf.Abs(atk) >= 0.05f) Line(sb, "Attack speed", "+" + Pct(atk));
			Skills.SkillType skill = weapon != null ? weapon.m_shared.m_skillType : Skills.SkillType.None;
			if (skill == Skills.SkillType.Bows || skill == Skills.SkillType.Crossbows)
			{
				float draw = AttackSpeed.DrawBonus(p);
				if (Mathf.Abs(draw) >= 0.05f) Line(sb, skill == Skills.SkillType.Bows ? "Draw speed" : "Reload speed", "+" + Pct(draw));
			}

			// Defense ------------------------------------------------------------------------------
			Section(sb, "Defense");
			Line(sb, "Armor", Num(p.GetBodyArmor()));
			ItemDrop.ItemData blocker = p.GetCurrentBlocker();
			if (blocker != null) Line(sb, "Block armor", Num(blocker.GetBlockPower(p.GetSkillFactor(Skills.SkillType.Blocking))));
			float physical = PlayerStats.Get(p, "AddPhysicalResistancePercentage");
			float elemental = PlayerStats.Get(p, "AddElementalResistancePercentage");
			HitData.DamageModifiers mods = p.GetDamageModifiers(null);
			Resist(sb, "Blunt", physical + PlayerStats.Get(p, "AddBluntResistancePercentage"), mods.m_blunt);
			Resist(sb, "Slash", physical + PlayerStats.Get(p, "AddSlashingResistancePercentage"), mods.m_slash);
			Resist(sb, "Pierce", physical + PlayerStats.Get(p, "AddPiercingResistancePercentage"), mods.m_pierce);
			Resist(sb, "Fire", elemental + PlayerStats.Get(p, "AddFireResistancePercentage"), mods.m_fire);
			Resist(sb, "Frost", elemental + PlayerStats.Get(p, "AddFrostResistancePercentage"), mods.m_frost);
			Resist(sb, "Lightning", elemental + PlayerStats.Get(p, "AddLightningResistancePercentage"), mods.m_lightning);
			Resist(sb, "Poison", elemental + PlayerStats.Get(p, "AddPoisonResistancePercentage"), mods.m_poison);
			Resist(sb, "Spirit", PlayerStats.Get(p, "AddSpiritResistancePercentage"), mods.m_spirit);
			var immune = new List<string>();
			foreach (string se in new[] { "Burning", "Freezing", "Frost", "Cold", "Wet", "Poison", "Tared" })
				if (PlayerStats.Get(p, "Immune" + se) > 0f) immune.Add(se);
			if (immune.Count > 0) Line(sb, "Immune to", string.Join(", ", immune));

			// Resources ----------------------------------------------------------------------------
			Section(sb, "Resources");
			Line(sb, "Health", Num(p.GetMaxHealth()) + Bonus(PlayerStats.Get(p, "ModifyHealthRegen"), "regen"));
			Line(sb, "Stamina", Num(p.GetMaxStamina()) + Bonus(PlayerStats.Get(p, "ModifyStaminaRegen"), "regen"));
			if (p.GetMaxEitr() > 0f) Line(sb, "Eitr", Num(p.GetMaxEitr()) + Bonus(PlayerStats.Get(p, "ModifyEitrRegen"), "regen"));
			Line(sb, "Carry weight", Num(p.GetMaxCarryWeight()));
			Line(sb, "Unique Find", Pct(PlayerStats.Get(p, "Luck")));

			// Movement and stamina costs (vanilla's equipment modifiers, with gear and effects included) -----------
			Section(sb, "Movement and stamina");
			string[] names = Player.s_equipmentModifierTooltips;
			for (int i = 0; names != null && i < names.Length; i++)
			{
				float v = p.GetEquipmentModifierPlusSE(i);
				if (Mathf.Abs(v) < 0.0005f) continue;
				Line(sb, Localization.instance.Localize(names[i]).TrimEnd(':'), (v > 0 ? "+" : "") + Pct(v * 100f));
			}

			// Unique powers -------------------------------------------------------------------------
			var uniques = new List<string>();
			foreach (ItemDrop.ItemData item in PlayerStats.EquippedItems(p))
			{
				MagicItem m = item.GetMagic();
				if (m != null && m.IsUnique && DataStore.UniqueById.TryGetValue(m.UniqueId, out UniqueDef u))
					uniques.Add($"<color={Tiers.GetHex(Tier.Unique)}>{u.Name}</color>: {u.Description}");
			}
			if (uniques.Count > 0)
			{
				Section(sb, "Unique powers");
				foreach (string u in uniques) sb.AppendLine(u);
			}

			// Every bonus from gear, totaled -------------------------------------------------------
			var totals = new SortedDictionary<string, float>();
			foreach (ItemDrop.ItemData item in PlayerStats.EquippedItems(p))
			{
				MagicItem m = item.GetMagic();
				if (m == null) continue;
				foreach (RolledAffix a in m.Affixes)
				{
					totals.TryGetValue(a.Id, out float v);
					totals[a.Id] = v + a.Value;
				}
			}
			if (totals.Count > 0)
			{
				Section(sb, "All bonuses from gear");
				var lines = new List<string>();
				foreach (KeyValuePair<string, float> kv in totals) lines.Add(TooltipPatches.FormatAffix(new RolledAffix(kv.Key, kv.Value)));
				lines.Sort(System.StringComparer.OrdinalIgnoreCase);
				foreach (string l in lines) sb.AppendLine(l);
				sb.AppendLine($"<color={Muted}>Weapon bonuses (damage, procs) only apply to hits with that weapon.</color>");
			}
			else
			{
				sb.AppendLine();
				sb.AppendLine($"<color={Muted}>No magic gear equipped.</color>");
			}
			return sb.ToString();
		}

		// Formatting helpers ---------------------------------------------------------------------

		private static string BuffName(string stat)
		{
			switch (stat)
			{
				case "damage": return "Damage";
				case "moveSpeed": return "Movement speed";
				case "attackSpeed": return "Attack speed";
				case "damageTaken": return "Damage taken";
				case "critChance": return "Critical strike chance";
				case "critDamage": return "Critical strike damage";
				case "immune": return "Immune to damage";
				case "berserk": return "Berserk";
				default: return stat;
			}
		}

		private static string BuffValue(string stat, float v)
		{
			if (stat == "immune" || stat == "berserk") return "active";
			return (v >= 0f ? "+" : "") + Pct(v);
		}

		/// <summary>Keep the sheet current while it's open (buffs tick down, gear changes): rebuilt twice a second.</summary>
		[HarmonyPatch(typeof(TextsDialog), nameof(TextsDialog.Update))]
		private static class Patch_Live
		{
			private static float _next;

			private static void Postfix(TextsDialog __instance)
			{
				if (Time.unscaledTime < _next) return;
				_next = Time.unscaledTime + 0.5f;
				Player p = Player.m_localPlayer;
				if (p == null || __instance.m_texts == null || __instance.m_texts.Count == 0 || __instance.m_selectionIndex != 0) return;
				TextsDialog.TextInfo info = __instance.m_texts[0];
				if (info == null || info.m_topic != Title || __instance.m_textArea == null) return;
				string text = Build(p);
				if (text == info.m_text) return;
				info.m_text = text;
				__instance.m_textArea.text = Localization.instance.Localize(text);
			}
		}

		private static void Section(StringBuilder sb, string title)
		{
			if (sb.Length > 0) sb.AppendLine();
			sb.AppendLine($"<color={Header}><b>{title}</b></color>");
		}

		private static void Line(StringBuilder sb, string label, string value) => sb.AppendLine($"{label}: <color=orange>{value}</color>");

		private static void Resist(StringBuilder sb, string type, float percent, HitData.DamageModifier vanilla)
		{
			float capped = Mathf.Min(percent, 75f);
			string mod = vanilla == HitData.DamageModifier.Normal ? "" : $" <color={Muted}>({ModName(vanilla)})</color>";
			if (capped <= 0f && mod.Length == 0) return; // keep the list short: only types with something going on
			Line(sb, type + " resistance", Pct(capped) + Cap(percent > 75f, "75%") + mod);
		}

		private static string ModName(HitData.DamageModifier m)
		{
			switch (m)
			{
				case HitData.DamageModifier.VeryResistant: return "very resistant";
				case HitData.DamageModifier.SlightlyResistant: return "slightly resistant";
				case HitData.DamageModifier.VeryWeak: return "very weak";
				case HitData.DamageModifier.SlightlyWeak: return "slightly weak";
				default: return m.ToString().ToLowerInvariant();
			}
		}

		private static string DamageBreakdown(HitData.DamageTypes d)
		{
			var parts = new List<string>();
			void Add(string n, float v) { if (v > 0.05f) parts.Add($"{n} {Num(v)}"); }
			Add("blunt", d.m_blunt); Add("slash", d.m_slash); Add("pierce", d.m_pierce);
			Add("fire", d.m_fire); Add("frost", d.m_frost); Add("lightning", d.m_lightning);
			Add("poison", d.m_poison); Add("spirit", d.m_spirit);
			return parts.Count > 1 ? $" <color={Muted}>({string.Join(", ", parts)})</color>" : "";
		}

		private static string Bonus(float pct, string what) => pct > 0f ? $" <color={Muted}>(+{Pct(pct)} {what})</color>" : "";

		private static string Cap(bool atCap, string cap) => atCap ? $" <color={Muted}>(max {cap})</color>" : "";

		private static string Num(float v) => v.ToString(v % 1f == 0f ? "0" : "0.#", CultureInfo.InvariantCulture);

		private static string Pct(float v) => v.ToString("0.#", CultureInfo.InvariantCulture) + "%";
	}
}
