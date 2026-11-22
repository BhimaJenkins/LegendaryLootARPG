using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace LegendaryLoot.Powers
{
	/// <summary>
	/// Area and lightning powers: novas (stomps, clouds, shockwaves), single strikes, chain lightning, lobbed
	/// bombs, slowing auras, circles of power, and the returning throw. Effects borrow Valheim's own visuals
	/// (Eikthyr's stomp, Dundr's lightning, the Staff of Embers' fireball, the Ooze bomb, Ice Shards).
	/// All damage is dealt by the local player's game, like vanilla attacks.
	/// </summary>
	public static class Areas
	{
		// Damage -------------------------------------------------------------------------------------

		public static HitData.DamageTypes Typed(string type, float amount)
		{
			var d = new HitData.DamageTypes();
			switch ((type ?? "").ToLowerInvariant())
			{
				case "fire": d.m_fire = amount; break;
				case "frost": d.m_frost = amount; break;
				case "lightning": d.m_lightning = amount; break;
				case "poison": d.m_poison = amount; break;
				case "spirit": d.m_spirit = amount; break;
				case "slash": d.m_slash = amount; break;
				case "pierce": d.m_pierce = amount; break;
				case "blunt": d.m_blunt = amount; break;
				default: d.m_damage = amount; break; // untyped
			}
			return d;
		}

		/// <summary>
		/// Hits one enemy, and staggers it if asked (stagger is sent straight to the creature, so it works even
		/// with no damage). Never sets off on-hit powers again (no endless chains).
		/// </summary>
		public static void HitOne(Player owner, Character c, HitData.DamageTypes damage, Vector3 from, float stagger, Skills.SkillType skill)
		{
			if (c == null || c.IsDead()) return;
			Vector3 dir = c.transform.position - from;
			dir.y = 0f;
			dir = dir.sqrMagnitude > 0.001f ? dir.normalized : owner.transform.forward;
			if (damage.GetTotalDamage() > 0f)
			{
				var hit = new HitData
				{
					m_damage = damage,
					m_point = c.GetCenterPoint(),
					m_dir = dir,
					m_skill = skill,
					m_hitType = HitData.HitType.Undefined,
				};
				hit.SetAttacker(owner);
				c.Damage(hit);
			}
			if (stagger > 0f && !c.IsDead()) c.Stagger(dir);
		}

		/// <summary>Enemies of the player within a radius, nearest first.</summary>
		public static List<Character> EnemiesNear(Player owner, Vector3 point, float radius, Character skip = null)
		{
			var list = new List<Character>();
			foreach (Character c in Character.GetAllCharacters())
			{
				if (c == null || c == owner || c == skip || c.IsDead() || c.IsPlayer()) continue;
				if (!BaseAI.IsEnemy(owner, c)) continue;
				if ((c.transform.position - point).sqrMagnitude > radius * radius) continue;
				list.Add(c);
			}
			list.Sort((a, b) => (a.transform.position - point).sqrMagnitude.CompareTo((b.transform.position - point).sqrMagnitude));
			return list;
		}

		/// <summary>Damages every enemy within the radius, with a matching effect.</summary>
		public static void Nova(Player owner, Vector3 point, float radius, HitData.DamageTypes damage, float stagger, string fx, Skills.SkillType skill, string sound = null)
		{
			Plugin.Debug($"Blast ({fx}) at {point}, radius {radius}, damage {damage.GetTotalDamage():0}");
			Fx.Broadcast(fx, point, Mathf.Clamp(radius / 4f, 0.5f, 2f));
			Specials.Sound(sound, point);
			foreach (Character c in EnemiesNear(owner, point, radius))
				HitOne(owner, c, damage, point, stagger, skill);
		}

		/// <summary>Lightning that jumps from the target to nearby enemies, each jump drawn as a bolt.</summary>
		public static void ChainLightning(Player owner, Character first, float damage, int jumps, float range)
		{
			if (first == null) return;
			var hit = new HashSet<Character> { first };
			Character from = first;
			Fx.Play("lightning", first.GetCenterPoint(), 0.6f);
			for (int i = 0; i < jumps; i++)
			{
				Character next = null;
				foreach (Character c in EnemiesNear(owner, from.transform.position, range))
					if (!hit.Contains(c)) { next = c; break; }
				if (next == null) break;
				hit.Add(next);
				Bolt.Draw(from.GetCenterPoint(), next.GetCenterPoint());
				Fx.Play("lightning", next.GetCenterPoint(), 0.6f);
				HitOne(owner, next, Typed("lightning", damage), from.transform.position, 0f, Skills.SkillType.ElementalMagic);
				from = next;
			}
		}

		/// <summary>
		/// Throws a bomb item's projectile in an arc toward a point. With a summon, the bomb is only the
		/// flying bottle: it does no damage of its own, and the creature appears where it lands (Blobber Lobber).
		/// </summary>
		public static void Lob(Player owner, Vector3 target, string[] itemNames, string summon, float duration, int max)
		{
			GameObject item = Fx.FindItem(itemNames);
			ItemDrop drop = item != null ? item.GetComponent<ItemDrop>() : null;
			GameObject projPrefab = drop?.m_itemData?.m_shared?.m_attack?.m_attackProjectile;
			if (projPrefab == null) return;
			Vector3 start = owner.GetEyePoint() + owner.transform.forward * 0.6f;
			Vector3 vel = Ballistic(start, target, 14f);
			GameObject go = Object.Instantiate(projPrefab, start, Quaternion.LookRotation(vel));
			IProjectile proj = go.GetComponent<IProjectile>();
			if (proj == null) return;
			var hit = new HitData { m_hitType = HitData.HitType.Undefined, m_skill = Skills.SkillType.None };
			if (string.IsNullOrEmpty(summon)) hit.m_damage = drop.m_itemData.GetDamage();
			hit.SetAttacker(owner);
			proj.Setup(owner, vel, 10f, hit, null, null);
			if (!string.IsNullOrEmpty(summon) && go.GetComponent<Projectile>() is Projectile p)
			{
				// Just the bottle: no cloud, no blast, no creature of its own.
				p.m_spawnOnHit = null;
				if (p.m_randomSpawnOnHit != null) p.m_randomSpawnOnHit = new List<GameObject>();
				p.m_aoe = 0f;
				p.m_damage = new HitData.DamageTypes();
				Projectiles.Tag tag = go.AddComponent<Projectiles.Tag>();
				tag.Summon = summon;
				tag.SummonDuration = duration;
				tag.SummonMax = max;
			}
		}

		/// <summary>Launch velocity for an arc that lands on the target (fixed time of flight).</summary>
		private static Vector3 Ballistic(Vector3 from, Vector3 to, float speed)
		{
			Vector3 delta = to - from;
			float flat = new Vector3(delta.x, 0f, delta.z).magnitude;
			float t = Mathf.Clamp(flat / speed, 0.35f, 1.6f);
			Vector3 v = delta / t;
			v.y += 0.5f * 20f * t; // Valheim's projectile gravity is about 20 m/s²; extra lift so it arcs
			return v;
		}

		// Circles of power (Seidr Ring) ---------------------------------------------------------------

		private class Zone
		{
			public Vector3 Center;
			public float Radius, Until, Value;
			public string Stat;
			public GameObject Ring;
		}

		private static readonly List<Zone> Zones = new List<Zone>();

		public static void AddZone(Vector3 center, float radius, float duration, string stat, float value)
		{
			var z = new Zone { Center = center, Radius = radius, Until = Time.time + duration, Stat = stat, Value = value };
			z.Ring = Ring.Create(center, radius, new Color(1f, 0.32f, 0.1f), duration, fiery: true);
			Zones.Add(z);
		}

		/// <summary>Bonus from circles of power the player is standing in.</summary>
		public static float ZoneBonus(Player p, string stat)
		{
			if (p == null || Zones.Count == 0) return 0f;
			float total = 0f;
			Vector3 pos = p.transform.position;
			for (int i = Zones.Count - 1; i >= 0; i--)
			{
				Zone z = Zones[i];
				if (Time.time > z.Until) { Zones.RemoveAt(i); continue; }
				if (!string.Equals(z.Stat, stat, System.StringComparison.OrdinalIgnoreCase)) continue;
				Vector3 d = pos - z.Center;
				d.y = 0f;
				if (d.sqrMagnitude <= z.Radius * z.Radius) total += z.Value;
			}
			return total;
		}

		public static void ClearZones()
		{
			foreach (Zone z in Zones) if (z.Ring != null) Object.Destroy(z.Ring);
			Zones.Clear();
		}

		// Slowing aura (Frostgrip) ---------------------------------------------------------------------------

		private static readonly Dictionary<Character, Animator> AuraSlowed = new Dictionary<Character, Animator>();
		private static float _nextAuraPulse;
		private static FollowRing _auraRing;

		/// <summary>
		/// Every frame for the local player: enemies inside the aura are slowed (movement, through the slow
		/// debuff on the creature's owner) and visibly move in slow motion (their animation, Keen Combat's
		/// Sanctuary Dome trick). A faint cold ring on the ground shows the edge.
		/// </summary>
		public static void AuraTick(Player p)
		{
			float radius = Effects.PlayerStats.Get(p, "SlowAuraRadius");
			float pct = Mathf.Clamp(Effects.PlayerStats.Get(p, "SlowAuraPercent"), 0f, 90f);
			if (radius <= 0f || pct <= 0f || p.IsDead())
			{
				ReleaseAura();
				if (_auraRing != null) _auraRing.gameObject.SetActive(false);
				return;
			}

			if (_auraRing == null) _auraRing = FollowRing.Create(new Color(0.7f, 0.9f, 1f, 0.45f));
			_auraRing.gameObject.SetActive(true);
			_auraRing.Follow(p.transform, radius);

			float k = 1f - pct / 100f;
			List<Character> inside = EnemiesNear(p, p.transform.position, radius);
			bool pulse = Time.time >= _nextAuraPulse;
			if (pulse) _nextAuraPulse = Time.time + 0.5f;

			var keep = new HashSet<Character>();
			foreach (Character c in inside)
			{
				if (c.IsBoss()) continue;
				keep.Add(c);
				if (pulse) Debuffs.Send(c, DebuffKind.Slow, pct, 1f);
				if (!AuraSlowed.TryGetValue(c, out Animator anim))
				{
					anim = c.GetComponentInChildren<Animator>();
					AuraSlowed[c] = anim;
				}
				if (anim != null) anim.speed = k; // reapplied every frame: the game resets it
			}
			var leaving = new List<Character>();
			foreach (KeyValuePair<Character, Animator> kv in AuraSlowed)
				if (kv.Key == null || !keep.Contains(kv.Key)) leaving.Add(kv.Key);
			foreach (Character c in leaving)
			{
				if (c != null && AuraSlowed[c] != null) AuraSlowed[c].speed = 1f;
				AuraSlowed.Remove(c);
			}
		}

		private static void ReleaseAura()
		{
			foreach (KeyValuePair<Character, Animator> kv in AuraSlowed)
				if (kv.Key != null && kv.Value != null) kv.Value.speed = 1f;
			AuraSlowed.Clear();
		}

		// Rune of Seasons ---------------------------------------------------------------------------------

		public static readonly string[] Seasons = { "fire", "frost", "lightning", "poison" };

		public static Color SeasonColor(string season)
		{
			switch (season)
			{
				case "fire": return new Color(1f, 0.45f, 0.12f);
				case "frost": return new Color(0.55f, 0.85f, 1f);
				case "lightning": return new Color(0.78f, 0.74f, 1f);
				case "poison": return new Color(0.45f, 0.92f, 0.3f);
				default: return Color.white;
			}
		}

		public static string CurrentSeason(float period) => Seasons[(int)(Time.time / Mathf.Max(1f, period)) % Seasons.Length];

		public static void BoostType(ref HitData.DamageTypes d, string type, float factor)
		{
			switch (type)
			{
				case "fire": d.m_fire *= factor; break;
				case "frost": d.m_frost *= factor; break;
				case "lightning": d.m_lightning *= factor; break;
				case "poison": d.m_poison *= factor; break;
			}
		}

		// Returning throw (Thunderstroke) --------------------------------------------------------------------

		/// <summary>Set while a projectile is hitting something, so on-hit powers know the hit came from a throw.</summary>
		public static bool InThrownHit;

		[HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
		private static class Patch_ThrownHit
		{
			[HarmonyPriority(Priority.First)] // before a piercing spear's item is held back
			private static void Prefix(Projectile __instance) => InThrownHit = __instance.m_spawnItem != null;
			private static void Postfix() => InThrownHit = false;
		}

		/// <summary>A thrown weapon marked to return goes back into the thrower's hands instead of dropping.</summary>
		[HarmonyPatch(typeof(Projectile), nameof(Projectile.SpawnOnHit))]
		private static class Patch_Recall
		{
			private static void Prefix(Projectile __instance)
			{
				ItemDrop.ItemData item = __instance.m_spawnItem;
				if (item == null || !(__instance.m_owner is Player owner) || owner != Player.m_localPlayer) return;
				Projectiles.Tag tag = __instance.GetComponent<Projectiles.Tag>();
				if (tag == null || !tag.Recall) return;
				if (!owner.GetInventory().AddItem(item)) return; // bag full: let it drop as usual
				__instance.m_spawnItem = null;
				// The game refuses to equip while you're mid-attack or mid-action, so keep trying for a moment.
				Plugin.Instance.StartCoroutine(EquipWhenFree(owner, item));
				Fx.Play("lightning", owner.GetCenterPoint(), 0.5f);
				Bolt.Draw(__instance.transform.position, owner.GetCenterPoint());
			}
		}

		/// <summary>
		/// Puts the returned weapon back in your hands. The game won't equip mid-attack, mid-dodge or in the air,
		/// so keep asking for a few seconds. A broken weapon (no durability left) can't be equipped at all.
		/// </summary>
		private static System.Collections.IEnumerator EquipWhenFree(Player owner, ItemDrop.ItemData item)
		{
			for (float waited = 0f; waited < 3f; waited += 0.1f)
			{
				if (owner == null || owner.IsDead() || item == null) yield break;
				if (owner.IsItemEquiped(item)) yield break;
				if (!owner.GetInventory().ContainsItem(item)) yield break;   // moved or dropped meanwhile
				if (owner.GetRightItem() != null) yield break;               // you took something else in hand: leave it
				if (item.m_shared.m_useDurability && item.m_durability <= 0f)
				{
					owner.Message(MessageHud.MessageType.Center, Localization.instance.Localize(item.m_shared.m_name) + " is broken. Repair it to wield it again.");
					yield break;
				}
				if (owner.EquipItem(item)) yield break;
				yield return new UnityEngine.WaitForSeconds(0.1f);
			}
		}

		[HarmonyPatch(typeof(Game), nameof(Game.Start))]
		private static class Patch_Reset
		{
			private static void Postfix()
			{
				ClearZones();
				AuraSlowed.Clear();
			}
		}
	}

	/// <summary>Borrows Valheim's own effects by name, with the fireball explosion as the fallback.</summary>
	public static class Fx
	{
		private static readonly Dictionary<string, EffectList> Cache = new Dictionary<string, EffectList>();
		private const string Rpc = "LLA_Fx";

		/// <summary>Plays an effect on every player's game (so others see it too).</summary>
		public static void Broadcast(string kind, Vector3 point, float scale)
		{
			if (string.IsNullOrEmpty(kind) || kind == "none") return;
			if (ZRoutedRpc.instance == null) { Play(kind, point, scale); return; }
			ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, Rpc, kind, point, scale);
		}

		[HarmonyPatch(typeof(Game), nameof(Game.Start))]
		private static class Patch_Register
		{
			private static void Postfix()
			{
				ZRoutedRpc.instance.Register<string, Vector3, float>(Rpc, (sender, kind, point, scale) => Play(kind, point, scale));
			}
		}

		public static void Play(string kind, Vector3 point, float scale)
		{
			// Any of the game's own effect prefabs by name, e.g. "vfx_BloodDeath".
			if (kind != null && (kind.StartsWith("vfx_") || kind.StartsWith("fx_") || kind.StartsWith("sfx_")))
			{
				GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(kind) : null;
				if (prefab != null)
				{
					GameObject go = Object.Instantiate(prefab, point, Quaternion.identity);
					if (!Mathf.Approximately(scale, 1f)) go.transform.localScale *= scale;
					return;
				}
				Plugin.Debug($"Effect '{kind}' not found; using the fallback.");
			}
			EffectList list = Get(kind);
			if (list == null) return;
			try { list.Create(point, Quaternion.identity, null, scale); }
			catch (System.Exception ex) { Plugin.Debug($"Effect '{kind}' failed: {ex.Message}"); }
		}

		private static EffectList Get(string kind)
		{
			kind = (kind ?? "fire").ToLowerInvariant();
			if (Cache.TryGetValue(kind, out EffectList cached) && cached != null) return cached;
			EffectList found = null;
			switch (kind)
			{
				case "lightning": found = ProjectileHit("StaffLightning"); break;
				case "frost": found = ProjectileHit("StaffIceShards"); break;
				case "poison": found = ProjectileHit("BombOoze"); break;
				case "stomp": found = CreatureAttack("Eikthyr", "stomp"); break;
				case "none": return null;
			}
			if (found == null) found = ProjectileHit("StaffFireball");
			Plugin.Debug($"Effect '{kind}': {(found != null ? "found" : "missing")}");
			Cache[kind] = found;
			return found;
		}

		public static GameObject FindItem(params string[] names)
		{
			if (ObjectDB.instance == null) return null;
			foreach (string n in names)
			{
				GameObject go = ObjectDB.instance.GetItemPrefab(n);
				if (go != null) return go;
			}
			return null;
		}

		private static EffectList ProjectileHit(string itemName)
		{
			GameObject item = FindItem(itemName);
			GameObject proj = item?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_attack?.m_attackProjectile;
			Projectile p = proj != null ? proj.GetComponent<Projectile>() : null;
			return p != null && p.m_hitEffects != null && p.m_hitEffects.HasEffects() ? p.m_hitEffects : null;
		}

		/// <summary>An attack effect from a creature's own weapon, e.g. Eikthyr's stomp.</summary>
		private static EffectList CreatureAttack(string creature, string attackContains)
		{
			GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(creature) : null;
			Humanoid h = prefab != null ? prefab.GetComponent<Humanoid>() : null;
			if (h == null || h.m_defaultItems == null) return null;
			foreach (GameObject go in h.m_defaultItems)
			{
				if (go == null || go.name.IndexOf(attackContains, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
				Attack a = go.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_attack;
				if (a == null) continue;
				if (a.m_triggerEffect != null && a.m_triggerEffect.HasEffects()) return a.m_triggerEffect;
				if (a.m_hitEffect != null && a.m_hitEffect.HasEffects()) return a.m_hitEffect;
			}
			return null;
		}
	}

	/// <summary>A quick jagged lightning bolt between two points.</summary>
	public class Bolt : MonoBehaviour
	{
		private const float Life = 0.25f;
		private LineRenderer _line;
		private float _born;

		public static void Draw(Vector3 a, Vector3 b)
		{
			var go = new GameObject("LLA_Bolt");
			var line = go.AddComponent<LineRenderer>();
			const int points = 9;
			line.positionCount = points;
			Vector3 side = Vector3.Cross((b - a).normalized, Vector3.up);
			if (side.sqrMagnitude < 0.01f) side = Vector3.right;
			for (int i = 0; i < points; i++)
			{
				float t = i / (float)(points - 1);
				Vector3 p = Vector3.Lerp(a, b, t);
				if (i > 0 && i < points - 1) p += side * Random.Range(-0.35f, 0.35f) + Vector3.up * Random.Range(-0.25f, 0.25f);
				line.SetPosition(i, p);
			}
			line.startWidth = 0.12f;
			line.endWidth = 0.06f;
			line.material = Ring.Material();
			line.startColor = line.endColor = new Color(0.75f, 0.85f, 1f, 1f);
			go.AddComponent<Bolt>();
		}

		private void Awake()
		{
			_line = GetComponent<LineRenderer>();
			_born = Time.time;
		}

		private void Update()
		{
			float t = (Time.time - _born) / Life;
			if (t >= 1f) { Destroy(gameObject); return; }
			Color c = _line.startColor;
			c.a = 1f - t;
			_line.startColor = _line.endColor = c;
		}
	}

	/// <summary>
	/// Rune of Seasons: a few faint motes drifting up around the player, in the color of the element that's
	/// boosted right now, easing to the next color when the season turns.
	/// </summary>
	public class SeasonMotes : MonoBehaviour
	{
		private static SeasonMotes _instance;
		private ParticleSystem _ps;
		private Color _color = Color.white;

		/// <summary>Called every frame for the local player; period 0 = no Rune of Seasons equipped.</summary>
		public static void Tick(Player p, float period)
		{
			if (period <= 0f || p == null || p.IsDead())
			{
				if (_instance != null) _instance.gameObject.SetActive(false);
				return;
			}
			if (_instance == null || _instance.transform.parent != p.transform) _instance = Create(p);
			if (!_instance.gameObject.activeSelf) _instance.gameObject.SetActive(true);
			Color target = Areas.SeasonColor(Areas.CurrentSeason(period));
			_instance._color = Color.Lerp(_instance._color, target, 1f - Mathf.Exp(-3f * Time.deltaTime));
			ParticleSystem.MainModule main = _instance._ps.main;
			main.startColor = _instance._color;
		}

		private static SeasonMotes Create(Player p)
		{
			if (_instance != null) Destroy(_instance.gameObject);
			var go = new GameObject("LLA_SeasonMotes");
			go.transform.SetParent(p.transform, false);
			go.transform.localPosition = new Vector3(0f, 1.0f, 0f);
			var ps = go.AddComponent<ParticleSystem>();
			ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
			ParticleSystem.MainModule main = ps.main;
			main.loop = true;
			main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
			main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.35f);
			main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
			main.maxParticles = 40;
			main.simulationSpace = ParticleSystemSimulationSpace.World;
			main.gravityModifier = -0.04f; // drift upward
			ParticleSystem.EmissionModule em = ps.emission;
			em.rateOverTime = 7f;
			ParticleSystem.ShapeModule shape = ps.shape;
			shape.shapeType = ParticleSystemShapeType.Sphere;
			shape.radius = 0.55f;
			ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
			col.enabled = true;
			var fade = new Gradient();
			fade.SetKeys(
				new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
				new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.45f, 0.25f), new GradientAlphaKey(0.35f, 0.7f), new GradientAlphaKey(0f, 1f) });
			col.color = fade;
			var r = go.GetComponent<ParticleSystemRenderer>();
			r.material = LightShaft.GetSparkleMaterial();
			r.renderMode = ParticleSystemRenderMode.Billboard;
			r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			ps.Play();
			var m = go.AddComponent<SeasonMotes>();
			m._ps = ps;
			return m;
		}
	}

	/// <summary>
	/// A faint ring on the ground that follows the player (Frostgrip's aura edge). Drawn with a frost texture:
	/// a broken, uneven band with brighter ice flecks, slowly drifting so it shimmers like cold air.
	/// </summary>
	public class FollowRing : MonoBehaviour
	{
		private const int Points = 96;
		private const float TileLength = 1.2f; // metres of ring per repeat of the texture
		private static Material _frost;
		private LineRenderer _line;
		private Color _color;

		public static FollowRing Create(Color color)
		{
			var go = new GameObject("LLA_AuraRing");
			Object.DontDestroyOnLoad(go);
			// Lie flat on the ground. (By default a line turns to face the camera, and the stretch of ring
			// running toward the camera twists on edge, drawing streaks over the player.)
			go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
			var line = go.AddComponent<LineRenderer>();
			line.alignment = LineAlignment.TransformZ;
			line.positionCount = Points;
			line.loop = true;
			line.useWorldSpace = true;
			line.startWidth = line.endWidth = 0.14f;
			line.textureMode = LineTextureMode.Stretch;
			line.material = FrostMaterial();
			line.startColor = line.endColor = color;
			var r = go.AddComponent<FollowRing>();
			r._line = line;
			r._color = color;
			return r;
		}

		public void Follow(Transform who, float radius)
		{
			Vector3 c = who.position;
			for (int i = 0; i < Points; i++)
			{
				float a = i / (float)Points * Mathf.PI * 2f;
				Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
				p.y = ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(p, out float h) ? h + 0.06f : c.y + 0.06f;
				_line.SetPosition(i, p);
			}
			// Repeat the frost pattern around the ring, drifting slowly; a gentle shimmer in brightness.
			Material m = _line.material;
			m.mainTextureScale = new Vector2(Mathf.Max(1f, Mathf.Round(2f * Mathf.PI * radius / TileLength)), 1f);
			m.mainTextureOffset = new Vector2(Time.time * 0.22f, 0f); // wisps flow steadily around the ring
			Color col = _color;
			col.a = _color.a * (0.85f + 0.15f * Mathf.Sin(Time.time * 1.7f));
			_line.startColor = _line.endColor = col;
		}

		/// <summary>
		/// Frost mask: long wisps of cold mist running along the band, thinning and breaking up, with a faint
		/// steady line underneath so the edge always reads. Tiles around the ring and flows as it scrolls.
		/// </summary>
		private static Material FrostMaterial()
		{
			if (_frost != null) return _frost;
			const int W = 256, H = 32;
			var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
			var px = new Color32[W * H];
			for (int x = 0; x < W; x++)
			{
				float ang = x / (float)W * Mathf.PI * 2f;
				float cx = Mathf.Cos(ang), sy = Mathf.Sin(ang);
				for (int y = 0; y < H; y++)
				{
					float v = (y + 0.5f) / H * 2f - 1f;                         // -1..1 across the band
					float across = Mathf.Clamp01(1f - Mathf.Abs(v));
					across = across * across * (3f - 2f * across);
					// Wisps: noise stretched along the ring, drifting across the band as it goes (tileable).
					float drift = v * 0.9f;
					float wisp = Mathf.PerlinNoise(4f + cx * 3f + drift, 9f + sy * 3f - drift);
					wisp = Mathf.Clamp01((wisp - 0.38f) * 2.6f);
					float fine = Mathf.PerlinNoise(17f + cx * 9f + drift * 2f, 3f + sy * 9f);
					float mist = wisp * Mathf.Lerp(0.6f, 1f, fine);
					float baseLine = Mathf.Clamp01(1f - Mathf.Abs(v) / 0.25f) * 0.35f;  // faint steady core
					float a = Mathf.Clamp01(mist * across * 0.95f + baseLine);
					px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
				}
			}
			tex.SetPixels32(px);
			tex.Apply();
			Shader shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Sprites/Default");
			_frost = new Material(shader) { mainTexture = tex };
			return _frost;
		}
	}

	/// <summary>
	/// A ring on the ground that fades out as it expires. The fiery style (Seidr Ring) is a thin line of
	/// flame: a hot core with flickering tongues that race around the ring.
	/// </summary>
	public class Ring : MonoBehaviour
	{
		private static Material _material, _fire;
		private LineRenderer _line;
		private float _born, _life, _radius, _seed;
		private Color _color;
		private bool _fiery;

		public static Material Material()
		{
			if (_material != null) return _material;
			Shader s = Shader.Find("Sprites/Default") ?? Shader.Find("Legacy Shaders/Particles/Additive");
			_material = new Material(s);
			return _material;
		}

		public static GameObject Create(Vector3 center, float radius, Color color, float life, bool fiery = false)
		{
			var go = new GameObject("LLA_Ring");
			go.transform.position = center;
			go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // flat on the ground
			var line = go.AddComponent<LineRenderer>();
			line.alignment = LineAlignment.TransformZ;
			const int points = 96;
			line.positionCount = points;
			line.loop = true;
			line.useWorldSpace = true;
			for (int i = 0; i < points; i++)
			{
				float a = i / (float)points * Mathf.PI * 2f;
				Vector3 p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
				// Sit on the ground where it can.
				if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(p, out float h)) p.y = h + 0.08f;
				line.SetPosition(i, p);
			}
			if (fiery)
			{
				line.startWidth = line.endWidth = 0.16f;
				line.textureMode = LineTextureMode.Stretch;
				line.material = new Material(FireMaterial()); // own copy: each ring scrolls on its own
				line.material.mainTextureScale = new Vector2(Mathf.Max(1f, Mathf.Round(2f * Mathf.PI * radius / 0.9f)), 1f);
			}
			else
			{
				line.startWidth = line.endWidth = 0.12f;
				line.material = Material();
			}
			line.startColor = line.endColor = color;
			var ring = go.AddComponent<Ring>();
			ring._line = line;
			ring._born = Time.time;
			ring._life = life;
			ring._color = color;
			ring._radius = radius;
			ring._fiery = fiery;
			ring._seed = Random.value * 100f;
			return go;
		}

		private void Update()
		{
			float t = (Time.time - _born) / Mathf.Max(0.1f, _life);
			if (t >= 1f) { Destroy(gameObject); return; }
			// Ease in, then fade over the last third.
			float fade = Mathf.Clamp01(t / 0.08f) * (t < 0.66f ? 1f : 1f - (t - 0.66f) / 0.34f);
			Color c = _color;
			if (_fiery)
			{
				// Flames race around the ring and flicker in brightness and width.
				_line.material.mainTextureOffset = new Vector2(-Time.time * 0.55f, 0f);
				float flicker = Mathf.PerlinNoise(_seed, Time.time * 6f);
				c = Color.Lerp(_color, new Color(1f, 0.75f, 0.3f), flicker * 0.45f);
				c.a = Mathf.Lerp(0.7f, 1f, flicker) * fade;
				_line.startWidth = _line.endWidth = Mathf.Lerp(0.13f, 0.19f, Mathf.PerlinNoise(_seed + 9f, Time.time * 4f));
			}
			else
			{
				c.a = (0.65f + 0.25f * Mathf.Sin(Time.time * 4f)) * fade;
			}
			_line.startColor = _line.endColor = c;
		}

		/// <summary>Flame texture: a hot white core across the band, ragged flickering tongues along it. Drawn once.</summary>
		private static Material FireMaterial()
		{
			if (_fire != null) return _fire;
			const int W = 256, H = 32;
			var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
			var px = new Color32[W * H];
			for (int x = 0; x < W; x++)
			{
				float ang = x / (float)W * Mathf.PI * 2f;
				float cx = Mathf.Cos(ang), sy = Mathf.Sin(ang);
				float tongue = Mathf.PerlinNoise(5f + cx * 4f, 3f + sy * 4f);       // how tall the flame is here
				float lick = Mathf.PerlinNoise(21f + cx * 11f, 8f + sy * 11f);      // fine ragged edge
				float reach = Mathf.Lerp(0.3f, 1f, tongue * 0.75f + lick * 0.35f);
				for (int y = 0; y < H; y++)
				{
					float v = Mathf.Abs((y + 0.5f) / H * 2f - 1f);                     // 0 at the centre line
					float flame = Mathf.Clamp01(1f - v / reach);
					flame = flame * flame;
					float core = Mathf.Clamp01(1f - v / 0.22f);                         // thin hot line through the middle
					float a = Mathf.Clamp01(flame * 0.8f + core * 0.9f);
					// Hotter (whiter) toward the core; the line's colour tints the rest orange-red.
					byte w = (byte)(Mathf.Lerp(200f, 255f, core) );
					px[y * W + x] = new Color32(255, w, (byte)(core * 255f), (byte)(a * 255f));
				}
			}
			tex.SetPixels32(px);
			tex.Apply();
			Shader shader = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Additive") ?? Shader.Find("Sprites/Default");
			_fire = new Material(shader) { mainTexture = tex };
			return _fire;
		}
	}
}
