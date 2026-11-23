using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using LegendaryLoot.Data;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LegendaryLoot.Enemies
{
	/// <summary>
	/// Enemy scaling in the CreatureLevelAndLootControl style (enemies.yml):
	///  - Every creature that spawns rolls its stars on its owner's game, replacing vanilla's 0-2: a cascade of
	///    "+1 star?" rolls (20% each, +2% per boss defeated), capped by biome (Meadows 2, Black Forest 3, Swamp 4,
	///    then 5). Health and damage per star are vanilla's (+100% / +50%); size grows 10% per star.
	///  - 1+ star creatures get one effect (Quick, Aggressive, Curious, Splitting, Armored, Rending, Deadly) and from
	///    3 stars a chance of one infusion (Fire, Frost, Lightning, Spirit, Poison, Chaos), shown as a name prefix.
	///  - The roll is saved on the creature, so every player sees the same stars and affixes and it never re-rolls.
	/// Bosses, tamed creatures, summons and bounty creatures keep their own levels (bounties still get affixes).
	/// </summary>
	public static class EnemyScaling
	{
		public const string KeyStars = "LLA_Stars";   // stars + 1 once rolled (0 = not yet)
		public const string KeyAffixes = "LLA_Aff";   // comma-separated affix names
		public const string KeyNoLoot = "LLA_NoLoot"; // split copies: no gear
		public const string KeyUnique = "LLA_Unique"; // a unique enemy (set before it rolls)
		public const string KeyTwin = "LLA_Twin";     // a Twin unique: its partner's ZDOID
		public const string KeyBoss = "LLA_Boss";     // a boss we rolled (stars don't change its level)
		public const string KeyTwinCopy = "LLA_TwinCopy"; // the Twin that was spawned (not the original)
		public const string KeyPlayers = "LLA_Players";   // extra players nearby, for multiplayer health

		private static EnemyFile Data => DataStore.Enemies;

		// ---------- rolling ----------

		[HarmonyPatch(typeof(Character), nameof(Character.Start))]
		private static class Patch_Start
		{
			private static void Postfix(Character __instance) => SafeStart(__instance);
		}

		// Humanoids (Greydwarfs, Draugr, Fulings, Necks...) have their own Start that skips Character's.
		[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Start))]
		private static class Patch_HumanoidStart
		{
			private static void Postfix(Humanoid __instance) => SafeStart(__instance);
		}

		private static void SafeStart(Character c)
		{
			try { OnStart(c); }
			catch (Exception ex) { Plugin.Log.LogWarning($"Enemy scaling: {c?.name}: {ex.Message}"); }
		}

		private static void OnStart(Character c)
		{
			if (c == null || c.IsPlayer() || c.m_nview == null || !c.m_nview.IsValid()) return;
			ZDO zdo = c.m_nview.GetZDO();
			if (c.m_nview.IsOwner() && zdo.GetInt(KeyStars) == 0)
			{
				if (c.IsBoss()) { if (BossScaling.Eligible(c)) BossScaling.Roll(c, zdo); }
				else if (Eligible(c, zdo)) Roll(c, zdo);
			}
			Apply(c, zdo);
		}

		internal static bool Eligible(Character c, ZDO zdo)
		{
			if (!Data.Enabled || c.IsBoss() || c.IsTamed() || Powers.Summons.IsSummon(c)) return false;
			string name = Utils.GetPrefabName(c.gameObject);
			if (name.StartsWith("LLA_", StringComparison.Ordinal) || Data.ExcludedSet.Contains(name)) return false;
			return true;
		}

		internal static int BossesDefeated()
		{
			int n = 0;
			foreach (string key in DataStore.Vendors.BossKeys.Keys)
				if (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(key)) n++;
			return n;
		}

		internal static int Cap(Vector3 pos)
		{
			int cap = Data.MaxStars;
			if (!Data.UseBiomeCaps || WorldGenerator.instance == null) return cap;
			string biome = WorldGenerator.instance.GetBiome(pos).ToString();
			int step = DataStore.Loot.BiomeStep(biome);
			string name = step >= 0 ? DataStore.Loot.BiomeAt(step) : biome;
			foreach (KeyValuePair<string, int> kv in Data.BiomeCaps)
				if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return Mathf.Min(cap, kv.Value);
			return cap;
		}

		private static void Roll(Character c, ZDO zdo)
		{
			if (zdo.GetBool(KeyUnique) && Data.Uniques.Enabled && string.IsNullOrEmpty(zdo.GetString("LLA_Quest")))
			{
				UniqueEnemies.Roll(c, zdo);
				return;
			}
			int stars;
			bool bounty = !string.IsNullOrEmpty(zdo.GetString("LLA_Quest"));
			if (bounty)
			{
				stars = Mathf.Max(0, c.GetLevel() - 1); // the bounty already chose its stars
			}
			else
			{
				int cap = Cap(c.transform.position);
				float chance = (Data.StarChance + Data.StarChancePerBoss * BossesDefeated()) / 100f;
				stars = 0;
				while (stars < cap && Random.value < chance) stars++;
				int fixedStars = zdo.GetInt(Events.Hunted.KeyStars);
				if (fixedStars > 0) stars = Mathf.Clamp(fixedStars - 1, 0, 5);   // a Hunted creature with set stars
				else if (zdo.GetBool(Events.Hunted.KeyApex))   // an Apex Predator: +2 on whatever it rolled
					stars = Mathf.Clamp(stars + Mathf.Max(0, DataStore.Events.Hunted.StarBonus), 0, Mathf.Max(Data.MaxStars, stars));
				c.SetLevel(stars + 1);
			}
			var affixes = new List<string>();
			if (stars >= 1 && Random.value * 100f < Data.EffectChance)
			{
				string e = Pick(Data.Effects, stars, BiomeStepAt(c.transform.position));
				if (e != null) affixes.Add(e);
			}
			if (stars >= Data.InfusionMinStars && Random.value * 100f < Data.InfusionChance)
			{
				string i = Pick(Data.Infusions, stars, BiomeStepAt(c.transform.position));
				if (i != null) affixes.Add(i);
			}
			zdo.Set(KeyStars, stars + 1);
			zdo.Set(KeyAffixes, string.Join(",", affixes));
			if (affixes.Count > 0 || stars > 2)
				Plugin.Debug($"[Enemies] {Utils.GetPrefabName(c.gameObject)}: {stars} stars" + (affixes.Count > 0 ? ", " + string.Join(" ", affixes) : ""));
		}

		internal static int BiomeStepAt(Vector3 pos)
		{
			if (WorldGenerator.instance == null) return 0;
			return Mathf.Max(0, DataStore.Loot.BiomeStep(WorldGenerator.instance.GetBiome(pos).ToString()));
		}

		internal static string Pick(Dictionary<string, Dictionary<string, float>> table, int stars, int biomeStep)
		{
			var names = new List<string>();
			var weights = new List<float>();
			float total = 0f;
			foreach (KeyValuePair<string, Dictionary<string, float>> kv in table)
			{
				Dictionary<string, float> d = kv.Value ?? new Dictionary<string, float>();
				float min = d.TryGetValue("minStars", out float m) ? m : 1f;
				float w = d.TryGetValue("weight", out float ww) ? ww : 1f;
				if (stars < min || w <= 0f) continue;
				if (d.TryGetValue("minBiome", out float mb) && biomeStep < mb) continue;
				names.Add(kv.Key);
				weights.Add(w);
				total += w;
			}
			if (total <= 0f) return null;
			float r = Random.value * total;
			for (int i = 0; i < names.Count; i++)
			{
				r -= weights[i];
				if (r <= 0f) return names[i];
			}
			return names[names.Count - 1];
		}

		// ---------- applying (every player's game) ----------

		private static void Apply(Character c, ZDO zdo)
		{
			if (zdo.GetInt(KeyStars) == 0) return;
			int stars = Mathf.Max(0, zdo.GetInt(KeyStars) - 1);
			string text = zdo.GetString(KeyAffixes);
			var r = c.gameObject.GetComponent<AffixRuntime>() ?? c.gameObject.AddComponent<AffixRuntime>();
			if (r.Applied) return;
			r.Applied = true;
			r.Stars = stars;
			if (!string.IsNullOrEmpty(text)) r.Affixes.AddRange(text.Split(','));
			r.Unique = zdo.GetBool(KeyUnique);
			r.Twin = zdo.GetZDOID(KeyTwin) != ZDOID.None || r.Has("Twin");
			r.TwinCopy = zdo.GetBool(KeyTwinCopy);
			r.EventBoss = zdo.GetBool(Events.WorldEvents.KeyEventBoss);
			r.Apex = zdo.GetBool(Events.Hunted.KeyApex);
			if (r.EventBoss) c.m_defeatSetGlobalKey = "";   // an event's boss doesn't count as defeating the real one

			// Size: from sizeFromStars (3) up, +10% per star: 3 stars 30% bigger, 4 stars 40%, 5 stars 50%.
			r.Boss = c.IsBoss();
			if (!r.Boss && stars >= Data.SizeFromStars && Data.SizePerStar > 0f)
			{
				r.BaseScale = c.transform.localScale;
				r.SizeMultiplier = 1f + Data.SizePerStar * stars;
				c.transform.localScale = r.BaseScale * r.SizeMultiplier;
			}

			foreach (string a in r.Affixes)
			{
				switch (a)
				{
					case "Quick": Speed(c, 1f + Data.Get(a, "speed", 40f) / 100f); break;
					case "Armored": Speed(c, Mathf.Max(0.1f, 1f + Data.Get(a, "speed", -50f) / 100f)); break;
					case "Curious":
					{
						BaseAI ai = c.GetComponent<BaseAI>();
						float m = 1f + Data.Get(a, "senses", 100f) / 100f;
						if (ai != null) { ai.m_viewRange *= m; ai.m_hearRange *= m; }
						break;
					}
					case "Aggressive":
					{
						MonsterAI ai = c.GetComponent<MonsterAI>();
						if (ai != null) ai.m_minAttackInterval *= Mathf.Max(0.1f, 1f - Data.Get(a, "interval", 50f) / 100f);
						r.AttackSpeed = 1f + Data.Get(a, "attackSpeed", 25f) / 100f;
						break;
					}
					case "Fire-infused": Element(c, ref c.m_damageModifiers.m_fire, ref c.m_damageModifiers.m_frost); break;
					case "Frost-infused": Element(c, ref c.m_damageModifiers.m_frost, ref c.m_damageModifiers.m_fire); break;
					case "Lightning-infused": Element(c, ref c.m_damageModifiers.m_lightning, ref c.m_damageModifiers.m_spirit); break;
					case "Spirit-infused": Element(c, ref c.m_damageModifiers.m_spirit, ref c.m_damageModifiers.m_lightning); break;
					case "Poison-infused": c.m_damageModifiers.m_poison = HitData.DamageModifier.Immune; break;
				}
			}
			InfusionLook(c, r);
			if (r.Unique) UniqueEnemies.Look(c, r);
			else if (r.Boss && r.Affixes.Count > 0)
			{
				// Bosses use the same timers (Mending, Black Hole, Twin) without the gold look.
				UniqueRuntime u = c.gameObject.GetComponent<UniqueRuntime>() ?? c.gameObject.AddComponent<UniqueRuntime>();
				u.Runtime = r;
				u.Coat = false;
			}
		}

		private static void Speed(Character c, float m)
		{
			c.m_speed *= m;
			c.m_walkSpeed *= m;
			c.m_runSpeed *= m;
			c.m_swimSpeed *= m;
			c.m_flySlowSpeed *= m;
			c.m_flyFastSpeed *= m;
		}

		/// <summary>Resistant to its element and weak to the opposite, but never undoes a creature's own stronger resistance.</summary>
		private static void Element(Character c, ref HitData.DamageModifier resist, ref HitData.DamageModifier weak)
		{
			switch (resist)
			{
				case HitData.DamageModifier.Immune:
				case HitData.DamageModifier.Ignore:
				case HitData.DamageModifier.VeryResistant:
				case HitData.DamageModifier.Resistant:
					break;
				default:
					resist = HitData.DamageModifier.Resistant;
					break;
			}
			if (weak == HitData.DamageModifier.Normal) weak = HitData.DamageModifier.Weak;
		}

		/// <summary>Vanilla has star looks (color shift, extra parts) for 1 and 2 stars only; 3+ stars wear the 2-star look.</summary>
		[HarmonyPatch(typeof(LevelEffects), nameof(LevelEffects.SetupLevelVisualization))]
		private static class Patch_LevelLook
		{
			private static void Prefix(LevelEffects __instance, ref int level)
			{
				int count = __instance.m_levelSetups != null ? __instance.m_levelSetups.Count : 0;
				if (count > 0 && level > count + 1) level = count + 1;
			}
		}

		// ---------- infusion looks ----------

		private static void InfusionLook(Character c, AffixRuntime r)
		{
			foreach (string a in r.Affixes)
			{
				Color color;
				switch (a)
				{
					case "Fire-infused": color = new Color(1f, 0.35f, 0.1f); break;
					case "Frost-infused": color = new Color(0.45f, 0.8f, 1f); break;
					case "Lightning-infused": color = new Color(0.55f, 0.7f, 1f); break;
					case "Spirit-infused": color = new Color(0.85f, 0.8f, 1f); break;
					case "Poison-infused": color = new Color(0.35f, 1f, 0.25f); break;
					case "Chaos-infused": color = new Color(0.8f, 0.3f, 1f); r.ChaosLight = true; break;
					default: continue;
				}
				if (Data.Auras.TryGetValue(a, out AuraDef aura) && aura != null && !string.IsNullOrEmpty(aura.Prefab)) r.Auras.Add(aura);
				// And a soft glow in its color.
				var glow = new GameObject("LLA_InfusionGlow");
				glow.transform.SetParent(c.transform, false);
				glow.transform.position = c.GetCenterPoint();
				Light light = glow.AddComponent<Light>();
				light.type = LightType.Point;
				light.color = color;
				light.range = 3f * Mathf.Max(1f, r.SizeMultiplier);
				light.intensity = 1.6f;
				light.shadows = LightShadows.None;
				r.Glow = light;
			}
		}

		// ---------- reading ----------

		public static AffixRuntime Of(Character c) => c != null ? c.GetComponent<AffixRuntime>() : null;

		public static bool NoLoot(Character c) =>
			c != null && c.m_nview != null && c.m_nview.IsValid() && c.m_nview.GetZDO().GetBool(KeyNoLoot);

		/// <summary>Extra stars for the loot roll: affixed enemies count as one star higher.</summary>
		public static int LootStarBonus(Character c)
		{
			AffixRuntime r = Of(c);
			return r != null && r.Affixes.Count > 0 ? Data.AffixLootStars : 0;
		}

		/// <summary>"Quick Fire-infused Greydwarf".</summary>
		[HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
		private static class Patch_Name
		{
			private static void Postfix(Character __instance, ref string __result)
			{
				AffixRuntime r = Of(__instance);
				if (r == null || __instance.IsPlayer()) return;
				if (r.Apex && !r.Unique) __result = "<color=#FF5A36>Apex</color> " + __result;
				if (r.Unique)
				{
					string prefix = r.Affixes.Count > 0 ? string.Join(" ", r.Affixes) + " " : "";
					__result = $"<color={UniqueEnemies.NameColor}>{prefix}{__result}</color>";
					return;
				}
				if (r.Affixes.Count == 0) return;
				if (r.Boss)
				{
					__result = $"<color=#FFB347>{string.Join(" ", r.Affixes)}</color> " + __result +
					           (r.Stars > 0 ? $" <color=#FFB347>({r.Stars} star{(r.Stars == 1 ? "" : "s")})</color>" : "");
					return;
				}
				__result = $"<color=#FFB347>{string.Join(" ", r.Affixes)}</color> " + __result;
			}
		}

		// ---------- combat ----------

		[ThreadStatic] private static bool _frostHit;
		[ThreadStatic] private static Character _leechFrom;
		private static readonly Dictionary<ZDOID, float> RendCooldown = new Dictionary<ZDOID, float>();
		private static readonly string[] Elements = { "fire", "frost", "lightning", "spirit", "poison" };

		[HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
		private static class Patch_Damage
		{
			[HarmonyPriority(Priority.High)]
			private static void Prefix(Character __instance, HitData hit, out float __state)
			{
				__state = __instance.GetHealth();
				_frostHit = false;
				_leechFrom = null;
				if (hit == null) return;

				// Damage taken by an affixed creature.
				AffixRuntime self = Of(__instance);
				if (self != null && self.Has("Armored"))
					hit.ApplyModifier(Mathf.Max(0f, 1f + Data.Get("Armored", "damageTaken", -25f) / 100f));

				// Damage dealt to you by an affixed creature.
				if (!(__instance is Player p) || p != Player.m_localPlayer) return;
				AffixRuntime from = Of(hit.GetAttacker());
				if (from == null || from.Affixes.Count == 0) return;
				UniqueEnemies.ScaleOutgoing(from, hit);
				float total = hit.GetTotalDamage();
				if (from.Has("Deadly") && Random.value * 100f < Data.Get("Deadly", "chance", 20f))
				{
					hit.ApplyModifier(Data.Get("Deadly", "damage", 150f) / 100f);
					total = hit.GetTotalDamage();
				}
				foreach (string a in from.Affixes)
				{
					switch (a)
					{
						case "Fire-infused": hit.m_damage.m_fire += total * Data.Get(a, "extra", 25f) / 100f; break;
						case "Frost-infused": hit.m_damage.m_frost += total * Data.Get(a, "extra", 25f) / 100f; _frostHit = true; break;
						case "Lightning-infused": hit.m_damage.m_lightning += total * Data.Get(a, "extra", 25f) / 100f; break;
						case "Spirit-infused": hit.m_damage.m_spirit += total * Data.Get(a, "extra", 25f) / 100f; break;
						case "Poison-infused":
							if (Random.value * 100f < Data.Get(a, "chance", 10f)) PoisonCloud(hit.GetAttacker(), total * Data.Get(a, "extra", 50f) / 100f);
							break;
						case "Chaos-infused":
						{
							float extra = total * Data.Get(a, "extra", 25f) / 100f;
							switch (Elements[Random.Range(0, Elements.Length)])
							{
								case "fire": hit.m_damage.m_fire += extra; break;
								case "frost": hit.m_damage.m_frost += extra; _frostHit = true; break;
								case "lightning": hit.m_damage.m_lightning += extra; break;
								case "spirit": hit.m_damage.m_spirit += extra; break;
								default: hit.m_damage.m_poison += extra; break;
							}
							break;
						}
						case "Leeching":
							_leechFrom = hit.GetAttacker();
							break;
						case "Exhausting":
							p.UseStamina(p.GetMaxStamina() * Data.Get(a, "percent", 15f) / 100f);
							break;
						case "Sapping":
							p.UseEitr(p.GetMaxEitr() * Data.Get(a, "percent", 30f) / 100f);
							break;
						case "Rending":
						{
							ZDOID id = hit.m_attacker;
							if (RendCooldown.TryGetValue(id, out float until) && Time.time < until) break;
							if (Random.value * 100f >= Data.Get(a, "chance", 25f)) break;
							RendCooldown[id] = Time.time + Data.Get(a, "cooldown", 30f);
							Plugin.Instance.StartCoroutine(Bleed(p, p.GetMaxHealth() * Data.Get(a, "percent", 10f) / 100f, Data.Get(a, "duration", 5f)));
							break;
						}
					}
				}
			}

			/// <summary>On the creature's owner: Splitting creatures become two weaker copies when they die.</summary>
			private static void Postfix(Character __instance, HitData hit, float __state)
			{
				// Boss affixes on the creature that was hit (its owner's game): Reflective, Mending, Summoner.
				if (!__instance.IsPlayer() && hit != null) UniqueEnemies.OnHurt(__instance, hit, __state);
				// Leeching: it heals for a share of the damage that actually got through to you.
				if (_leechFrom != null && __instance == Player.m_localPlayer)
				{
					float dealt = __state - __instance.GetHealth();
					if (dealt > 0f && !_leechFrom.IsDead()) _leechFrom.Heal(dealt * Data.Get("Leeching", "percent", 20f) / 100f, true);
					_leechFrom = null;
				}
				// An infused frost hit chills you for a set time (frostSeconds), not vanilla's damage-based time.
				if (_frostHit && __instance == Player.m_localPlayer)
				{
					_frostHit = false;
					StatusEffect frost = __instance.GetSEMan().GetStatusEffect(SEMan.s_statusEffectFrost);
					if (frost != null)
					{
						frost.m_ttl = Data.Get("Frost-infused", "frostSeconds", 3f);
						frost.m_time = 0f;
					}
				}
				if (__state <= 0f || __instance.GetHealth() > 0f || __instance.IsPlayer()) return;
				AffixRuntime r = Of(__instance);
				if (r == null || !r.Has("Splitting") || __instance.m_nview == null || !__instance.m_nview.IsOwner()) return;
				Split(__instance, r);
			}
		}

		private static void Split(Character c, AffixRuntime r)
		{
			GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(Utils.GetPrefabName(c.gameObject)) : null;
			if (prefab == null) return;
			int stars = Mathf.Max(0, r.Stars - (int)Data.Get("Splitting", "starsLost", 2f));
			for (int i = 0; i < 2; i++)
			{
				Vector3 at = c.transform.position + c.transform.right * (i == 0 ? -1f : 1f) + Vector3.up * 0.3f;
				GameObject go = UnityEngine.Object.Instantiate(prefab, at, c.transform.rotation);
				Character copy = go.GetComponent<Character>();
				if (copy == null || copy.m_nview == null || !copy.m_nview.IsValid()) continue;
				copy.SetLevel(stars + 1);
				ZDO zdo = copy.m_nview.GetZDO();
				zdo.Set(KeyStars, stars + 1);
				zdo.Set(KeyAffixes, "");
				zdo.Set(KeyNoLoot, true);
			}
			Plugin.Debug($"[Enemies] {c.m_name} split into two {stars}-star copies.");
		}

		private static void PoisonCloud(Character source, float poison)
		{
			if (source == null) return;
			float radius = Data.Get("Poison-infused", "radius", 3f);
			Powers.Fx.Broadcast("vfx_blob_attack", source.transform.position, 1f);
			foreach (Player pl in Player.GetAllPlayers())
			{
				if (Vector3.Distance(pl.transform.position, source.transform.position) > radius) continue;
				var hit = new HitData { m_point = pl.transform.position };
				hit.m_damage.m_poison = poison;
				hit.SetAttacker(source);
				pl.Damage(hit);
			}
		}

		/// <summary>
		/// Rending: your health drains over a few seconds and can kill you. It goes straight to your health, so armor,
		/// blocking and shields (Staff of Protection's bubble) don't stop it; healing does.
		/// </summary>
		private static IEnumerator Bleed(Player p, float total, float duration)
		{
			if (p == null || total <= 0f) yield break;
			p.Message(MessageHud.MessageType.TopLeft, "You're bleeding!");
			int ticks = Mathf.Max(1, Mathf.RoundToInt(duration * 2f));
			float each = total / ticks;
			for (int i = 0; i < ticks; i++)
			{
				yield return new WaitForSeconds(0.5f);
				if (p == null || p.IsDead()) yield break;
				var hit = new HitData { m_point = p.GetCenterPoint(), m_hitType = HitData.HitType.Undefined };
				hit.m_damage.m_damage = each;
				p.ApplyDamage(hit, true, false);
			}
		}
	}

	/// <summary>A creature's rolled stars and affixes on this game, plus Aggressive's faster attacks.</summary>
	public class AffixRuntime : MonoBehaviour
	{
		public bool Applied;
		public int Stars;
		public readonly List<string> Affixes = new List<string>();
		public float AttackSpeed = 1f;
		public Vector3 BaseScale;
		public readonly List<GameObject> Looks = new List<GameObject>();
		public readonly List<AuraDef> Auras = new List<AuraDef>();
		private readonly Dictionary<AuraDef, float> _nextAura = new Dictionary<AuraDef, float>();
		private GameObject _lastRandom;
		public Light Glow;
		public bool ChaosLight;
		public bool Unique;
		public bool Twin;
		public bool Boss;
		public bool TwinCopy;
		public bool EventBoss;
		public bool Apex;      // a Hunted event's Apex Predator
		public float SizeMultiplier = 1f;
		private int _sizeFrames = 120; // keep the size for the first moments, in case star visuals reset it

		private Character _c;
		private Animator _anim;
		private bool _boosted;

		public bool Has(string affix) => Affixes.Contains(affix);

		/// <summary>
		/// One burst of each aura at the feet. Purely visual and local to each player: copied with its scripts,
		/// damage and sound removed, and cleared after a few seconds. Only while a player is near.
		/// </summary>
		private void PlayAuras()
		{
			if (_c == null || _c.IsDead() || Player.m_localPlayer == null) return;
			bool near = Vector3.Distance(Player.m_localPlayer.transform.position, transform.position) <= 60f;
			foreach (AuraDef aura in Auras)
			{
				// Each aura keeps its own beat (fire every 0.5 s, frost every 5 s...).
				if (_nextAura.TryGetValue(aura, out float next) && Time.time < next) continue;
				_nextAura[aura] = Time.time + Mathf.Max(0.1f, aura.Interval);
				if (!near) continue;
				AuraDef a = aura;
				bool random = string.Equals(aura.Prefab, "random", StringComparison.OrdinalIgnoreCase);
				if (random)
				{
					// Chaos: one of the other auras, at that aura's size. The previous one is cleared first.
					var others = new List<AuraDef>();
					foreach (AuraDef o in DataStore.Enemies.Auras.Values)
						if (o != null && !string.IsNullOrEmpty(o.Prefab) && !string.Equals(o.Prefab, "random", StringComparison.OrdinalIgnoreCase)) others.Add(o);
					if (others.Count == 0) continue;
					a = others[UnityEngine.Random.Range(0, others.Count)];
					if (_lastRandom != null) Destroy(_lastRandom);
				}
				GameObject prefab = AuraPrefab(a.Prefab);
				if (prefab == null) continue;
				// Made inside an inactive holder, so nothing in it wakes up (no sound, no scripts) until it's stripped.
				GameObject fx;
				bool was = ZNetView.m_forceDisableInit;
				ZNetView.m_forceDisableInit = true;
				try { fx = Instantiate(prefab, Holder.transform); }
				finally { ZNetView.m_forceDisableInit = was; }
				foreach (MonoBehaviour mb in fx.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);   // no damage, no network, no sound scripts
				foreach (Collider col in fx.GetComponentsInChildren<Collider>(true)) DestroyImmediate(col);
				foreach (AudioSource au in fx.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(au);     // silent
				float height = (aura.Height > 0f ? aura.Height : 0.05f);
				if (aura.Follow) fx.transform.SetParent(transform, false);
				else fx.transform.SetParent(null, false);
				fx.transform.position = transform.position + Vector3.up * height;
				fx.transform.rotation = Quaternion.identity;
				Vector3 baseScale = prefab.transform.localScale * Mathf.Max(0.05f, a.Scale) * Mathf.Max(1f, SizeMultiplier);
				fx.transform.localScale = aura.Follow ? Div(baseScale, transform.lossyScale) : baseScale;
				fx.transform.localScale *= random ? Mathf.Max(0.05f, aura.Scale) : 1f;
				if (random) _lastRandom = fx;
				Destroy(fx, 4f);
			}
		}

		private static GameObject _holder;
		private static GameObject Holder
		{
			get
			{
				if (_holder != null) return _holder;
				_holder = new GameObject("LLA_AuraHolder");
				_holder.SetActive(false);
				DontDestroyOnLoad(_holder);
				return _holder;
			}
		}

		private static Vector3 Div(Vector3 a, Vector3 b) =>
			new Vector3(a.x / Mathf.Max(0.001f, b.x), a.y / Mathf.Max(0.001f, b.y), a.z / Mathf.Max(0.001f, b.z));

		private static readonly Dictionary<string, GameObject> AuraPrefabs = new Dictionary<string, GameObject>();

		private static GameObject AuraPrefab(string name)
		{
			if (AuraPrefabs.TryGetValue(name, out GameObject p) && p != null) return p;
			p = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
			if (p == null) Plugin.Log.LogWarning($"enemies.yml: aura effect '{name}' not found.");
			AuraPrefabs[name] = p;
			return p;
		}

		private void OnDestroy()
		{
			foreach (GameObject g in Looks) if (g != null) Destroy(g);
		}

		private void Start()
		{
			_c = GetComponent<Character>();
			_anim = GetComponentInChildren<Animator>();
		}

		private float _nextPlayerCheck;
		private static readonly List<Player> Nearby = new List<Player>();

		/// <summary>On the owner, every few seconds: count the extra players nearby and rescale health, keeping its share.</summary>
		private void CheckPlayers()
		{
			if (Time.time < _nextPlayerCheck) return;
			_nextPlayerCheck = Time.time + 4f + UnityEngine.Random.value;
			if (_c == null || _c.IsDead() || _c.m_nview == null || !_c.m_nview.IsValid() || !_c.m_nview.IsOwner()) return;
			int extra = 0;
			if (Plugin.HealthPerExtraPlayer.Value > 0f)
			{
				Nearby.Clear();
				Player.GetPlayersInRange(transform.position, Plugin.ExtraPlayerRange.Value, Nearby);
				extra = Mathf.Max(0, Nearby.Count - 1);
			}
			ZDO zdo = _c.m_nview.GetZDO();
			if (zdo.GetInt(EnemyScaling.KeyPlayers) == extra) return;
			float share = _c.GetHealth() / Mathf.Max(1f, _c.GetMaxHealth());
			zdo.Set(EnemyScaling.KeyPlayers, extra);
			_c.SetupMaxHealth();
			_c.SetHealth(Mathf.Max(1f, _c.GetMaxHealth() * share));
		}

		private void LateUpdate()
		{
			CheckPlayers();
			if (SizeMultiplier != 1f && _sizeFrames > 0)
			{
				_sizeFrames--;
				Vector3 want = BaseScale * SizeMultiplier;
				if (transform.localScale != want) transform.localScale = want;
			}
			if (Auras.Count > 0) PlayAuras();
			if (Glow != null)
			{
				if (ChaosLight) Glow.color = Color.HSVToRGB(Mathf.Repeat(Time.time * 0.25f, 1f), 0.8f, 1f);
				Glow.intensity = 1.4f + 0.3f * Mathf.Sin(Time.time * 3f);
			}
			if (AttackSpeed <= 1f || _c == null || _anim == null) return;
			bool attacking = _c.InAttack();
			if (attacking && !_boosted) { _anim.speed *= AttackSpeed; _boosted = true; }
			else if (!attacking && _boosted) { _anim.speed /= AttackSpeed; _boosted = false; }
		}
	}
}
