using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using HarmonyLib;
using LegendaryLoot.Data;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryLoot.Vendors
{
	/// <summary>
	/// Buildable vendor stalls (vendors.yml "stalls"): Boogie's Bar and Grillo's General Store.
	/// Each is one hammer piece (Misc tab) built at startup from a blueprint shipped with the mod: copies of vanilla
	/// pieces with their behaviour stripped (pure looks and collision), fires lit, item stands holding their items,
	/// plus a vendor creature (Greyling, Fuling) wearing a hat and running a vanilla Trader. Placed stalls are normal
	/// network objects, so everyone sees them; the vendor and its shop live on every player's game.
	/// </summary>
	public static class Stalls
	{
		public const string Prefix = "LLA_Stall_";
		private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();
		private static GameObject _holder;
		private static bool _built;

		public static IEnumerable<GameObject> All => Prefabs.Values;

		// ---------- registration ----------

		// Built once both the piece list (ZNetScene) and the item list (ObjectDB, for hats and stand items) exist,
		// whichever wakes up second. Placed stalls are only created later, when the world's objects load.
		[HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
		private static class Patch_Scene
		{
			[HarmonyPriority(Priority.Low)]
			private static void Postfix() => TryBuild();
		}

		[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
		private static class Patch_Db
		{
			[HarmonyPriority(Priority.Low)]
			private static void Postfix() => TryBuild();
		}

		private static void TryBuild()
		{
			ZNetScene scene = ZNetScene.instance;
			if (scene == null || ObjectDB.instance == null || ObjectDB.instance.m_items.Count == 0) return;
			try { BuildAll(scene); }
			catch (Exception ex) { Plugin.Log.LogError($"Couldn't build the vendor stalls: {ex}"); }
			AddToHammer();
		}

		private static void BuildAll(ZNetScene scene)
		{
			if (!_built)
			{
				_built = true;
				_holder = new GameObject("LLA_StallPrefabs");
				_holder.SetActive(false); // nothing under here ever wakes up; copies made from it do
				Object.DontDestroyOnLoad(_holder);
				foreach (KeyValuePair<string, StallDef> kv in DataStore.Vendors.Stalls)
				{
					if (kv.Value == null) continue;
					GameObject prefab = Build(kv.Key, kv.Value, scene);
					if (prefab != null) Prefabs[kv.Key] = prefab;
				}
			}
			foreach (GameObject prefab in Prefabs.Values)
			{
				if (!scene.m_prefabs.Contains(prefab)) scene.m_prefabs.Add(prefab);
				scene.m_namedPrefabs[prefab.name.GetStableHashCode()] = prefab;
			}
		}

		private static void AddToHammer()
		{
			if (ObjectDB.instance == null || Prefabs.Count == 0) return;
			GameObject hammer = ObjectDB.instance.GetItemPrefab("Hammer");
			PieceTable table = hammer != null ? hammer.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces : null;
			if (table == null) return;
			foreach (GameObject prefab in Prefabs.Values)
			{
				Piece piece = prefab.GetComponent<Piece>();
				piece.m_category = CategoryOf(DefOf(prefab));
				piece.m_usage = UsageOf(DefOf(prefab));
				FillRequirements(piece);
				if (!table.m_pieces.Contains(prefab)) table.m_pieces.Add(prefab);
			}
		}

		private static readonly HashSet<string> _logged = new HashSet<string>();

		/// <summary>The build menu's tabs (Misc., Furniture, Decor...) come from the piece's usage tags.</summary>
		private static Piece.UsageTagFlags UsageOf(StallDef def)
		{
			Piece.UsageTagFlags flags = 0;
			foreach (string t in def?.Tags ?? new List<string>())
			{
				string name = string.Equals(t, "Decoration", StringComparison.OrdinalIgnoreCase) ? "Decor" : t;
				if (Enum.TryParse(name, true, out Piece.UsageTagFlags f)) flags |= f;
				else Plugin.Log.LogWarning($"vendors.yml: unknown build tab '{t}'.");
			}
			return flags == 0 ? Piece.UsageTagFlags.Misc : flags;
		}

		private static Piece.PieceCategory CategoryOf(StallDef def)
		{
			string name = def?.Category;
			if (string.Equals(name, "Decoration", StringComparison.OrdinalIgnoreCase)) name = "Furniture";
			return !string.IsNullOrEmpty(name) && Enum.TryParse(name, true, out Piece.PieceCategory c) && c != Piece.PieceCategory.Max ? c : Piece.PieceCategory.Misc;
		}

		/// <summary>
		/// Makes sure the stalls are listed under their hammer tab (vendors.yml category). (Some build-menu mods sort pieces into their
		/// own tabs, which left the stalls only under "Show All".)
		/// </summary>
		[HarmonyPatch(typeof(PieceTable), nameof(PieceTable.UpdateAvailable))]
		private static class Patch_MiscTab
		{
			[HarmonyPriority(Priority.Last)]
			private static void Postfix(PieceTable __instance)
			{
				if (Prefabs.Count == 0 || __instance.m_availablePiecesByCategory == null || __instance.m_availablePiecesByCategory.Count == 0) return;
				foreach (GameObject prefab in Prefabs.Values)
				{
					Piece piece = prefab.GetComponent<Piece>();
					if (piece == null) continue;
					bool available = __instance.m_availablePieces.Contains(piece);
					if (Plugin.DebugLogging.Value && _logged.Add(prefab.name + available))
						Plugin.Log.LogInfo($"Stall {piece.m_name}: in this hammer={__instance.m_pieces.Contains(prefab)}, buildable now={available} " +
						                   "(needs every material picked up once), tabs=" + UsageOf(DefOf(prefab)));
					if (!available) continue;
					Piece.PieceCategory cat = CategoryOf(DefOf(prefab));
					piece.m_category = cat;
					int i = (int)cat;
					if (i < 0 || i >= __instance.m_availablePiecesByCategory.Count) continue;
					List<Piece> list = __instance.m_availablePiecesByCategory[i];
					if (!list.Contains(piece)) list.Add(piece);
				}
			}
		}

		private static void FillRequirements(Piece piece)
		{
			if (piece == null || ObjectDB.instance == null) return;
			StallDef def = DefOf(piece.gameObject);
			if (def == null) return;
			var reqs = new List<Piece.Requirement>();
			foreach (KeyValuePair<string, int> kv in def.Requirements)
			{
				GameObject item = ObjectDB.instance.GetItemPrefab(kv.Key);
				if (item == null) { Plugin.Log.LogWarning($"vendors.yml: stall material {kv.Key} not found."); continue; }
				reqs.Add(new Piece.Requirement { m_resItem = item.GetComponent<ItemDrop>(), m_amount = kv.Value, m_recover = true });
			}
			piece.m_resources = reqs.ToArray();
			if (piece.m_icon == null) piece.m_icon = Icon(KeyOf(piece.gameObject), def);
		}

		public static string KeyOf(GameObject go)
		{
			string name = Utils.GetPrefabName(go);
			return name.StartsWith(Prefix, StringComparison.Ordinal) ? name.Substring(Prefix.Length) : null;
		}

		public static StallDef DefOf(GameObject go)
		{
			string key = KeyOf(go);
			return key != null && DataStore.Vendors.Stalls.TryGetValue(key, out StallDef d) ? d : null;
		}

		// ---------- building the prefab ----------

		private static GameObject Build(string key, StallDef def, ZNetScene scene)
		{
			string text = ReadBlueprint(def.Blueprint);
			if (text == null)
			{
				Plugin.Log.LogWarning($"Stall {key}: blueprint '{def.Blueprint}' not found.");
				return null;
			}
			var root = new GameObject(Prefix + key);
			root.transform.SetParent(_holder.transform, false);
			root.layer = LayerMask.NameToLayer("piece");
			ZNetView nview = root.AddComponent<ZNetView>();
			nview.m_persistent = true;
			Piece piece = root.AddComponent<Piece>();
			piece.m_name = def.Name;
			piece.m_description = def.Description;
			piece.m_category = CategoryOf(def);
			piece.m_usage = UsageOf(def);
			piece.m_canBeRemoved = true;
			piece.m_icon = Icon(key, def);
			GameObject floor = scene.GetPrefab("wood_floor_1x1");
			Piece floorPiece = floor != null ? floor.GetComponent<Piece>() : null;
			if (floorPiece != null) piece.m_placeEffect = floorPiece.m_placeEffect;

			int count = 0, missing = 0;
			bool inPieces = false;
			foreach (string raw in text.Split('\n'))
			{
				string line = raw.Trim();
				if (line.StartsWith("#Pieces")) { inPieces = true; continue; }
				if (line.StartsWith("#")) { inPieces = false; continue; }
				if (!inPieces || line.Length == 0) continue;
				if (AddPiece(root.transform, line, scene)) count++;
				else missing++;
			}
			AddVendor(root.transform, key, def, scene);
			Plugin.Log.LogInfo($"Stall {def.Name}: built from {count} pieces" + (missing > 0 ? $" ({missing} not found)" : "") + ".");
			return root;
		}

		private static string ReadBlueprint(string name)
		{
			if (string.IsNullOrEmpty(name)) return null;
			// A copy in the config folder wins, so builders can swap the building.
			string custom = Path.Combine(DataStore.Folder, name + ".blueprint");
			if (File.Exists(custom)) return File.ReadAllText(custom);
			using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("LegendaryLoot.Blueprints." + name + ".blueprint"))
			{
				if (s == null) return null;
				using (var r = new StreamReader(s)) return r.ReadToEnd();
			}
		}

		private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

		/// <summary>One blueprint line: prefab;?;x;y;z;qx;qy;qz;qw;info;sx;sy;sz;data</summary>
		private static bool AddPiece(Transform root, string line, ZNetScene scene)
		{
			string[] p = line.Split(';');
			if (p.Length < 9) return false;
			string name = p[0];
			GameObject src = scene.GetPrefab(name);
			// Pieces from Symmetrical Building Pieces fall back to the vanilla look when that mod isn't installed.
			if (src == null && name.StartsWith("SBP_", StringComparison.Ordinal)) src = scene.GetPrefab(name.Substring(4));
			if (src == null)
			{
				Plugin.Debug($"Stall: piece {name} not found, skipped.");
				return false;
			}
			GameObject copy;
			bool was = ZNetView.m_forceDisableInit;
			ZNetView.m_forceDisableInit = true;
			try { copy = Object.Instantiate(src, root, false); }
			finally { ZNetView.m_forceDisableInit = was; }
			copy.name = name;
			copy.transform.localPosition = new Vector3(F(p[2]), F(p[3]), F(p[4]));
			copy.transform.localRotation = new Quaternion(F(p[5]), F(p[6]), F(p[7]), F(p[8]));
			if (p.Length >= 13 && float.TryParse(p[10], NumberStyles.Float, CultureInfo.InvariantCulture, out float sx))
				copy.transform.localScale = new Vector3(sx, F(p[11]), F(p[12]));

			// Fires, candles and lanterns stay lit.
			Fireplace fire = copy.GetComponent<Fireplace>();
			if (fire != null)
			{
				foreach (GameObject on in new[] { fire.m_enabledObject, fire.m_enabledObjectHigh, fire.m_fullObject })
					if (on != null) on.SetActive(true);
				foreach (GameObject off in new[] { fire.m_emptyObject, fire.m_halfObject })
					if (off != null) off.SetActive(false);
			}
			// Item stands keep the item they held in the blueprint.
			ItemStand stand = copy.GetComponent<ItemStand>();
			if (stand != null && p.Length >= 14) ShowStandItem(stand, p[13]);

			Strip(copy);
			return true;
		}

		/// <summary>The item on an item stand, from the blueprint's saved data (its "item" value is the item's hash).</summary>
		private static void ShowStandItem(ItemStand stand, string data)
		{
			int hash = ReadInt(data, "item".GetStableHashCode());
			if (hash == 0 || ObjectDB.instance == null) return;
			GameObject item = ObjectDB.instance.GetItemPrefab(hash);
			if (item == null) return;
			try
			{
				ItemDrop.ItemData itemData = item.GetComponent<ItemDrop>().m_itemData;
				GameObject attachPrefab = ItemStand.GetAttachPrefab(item);
				if (attachPrefab == null) return;
				Transform attach = stand.GetAttach(itemData) ?? stand.m_attachOther ?? stand.transform;
				GameObject visual = Object.Instantiate(ItemStand.GetAttachGameObject(attachPrefab), attach.position, attach.rotation, attach);
				visual.transform.localPosition = attachPrefab.transform.localPosition;
				visual.transform.localRotation = attachPrefab.transform.localRotation;
				visual.transform.localScale = Vector3.Scale(attachPrefab.transform.localScale, itemData.GetScale());
			}
			catch (Exception ex)
			{
				Plugin.Debug($"Stall: couldn't show {item.name} on a stand: {ex.Message}");
			}
		}

		/// <summary>Finds an int value in Infinity Hammer's saved ZDO data (key hash, then the value).</summary>
		private static int ReadInt(string base64, int key)
		{
			if (string.IsNullOrEmpty(base64)) return 0;
			byte[] b;
			try { b = Convert.FromBase64String(base64); }
			catch { return 0; }
			byte[] k = BitConverter.GetBytes(key);
			for (int i = 0; i + 8 <= b.Length; i++)
				if (b[i] == k[0] && b[i + 1] == k[1] && b[i + 2] == k[2] && b[i + 3] == k[3])
					return BitConverter.ToInt32(b, i + 4);
			return 0;
		}

		private static readonly HashSet<string> KeepBehaviours = new HashSet<string> { "LightFlicker", "LightLod", "StallVendor", "Trader" };

		/// <summary>Leaves only looks and collision: every script goes (several passes for ones that depend on each other).</summary>
		private static void Strip(GameObject go)
		{
			for (int pass = 0; pass < 4; pass++)
			{
				bool left = false;
				MonoBehaviour[] all = go.GetComponentsInChildren<MonoBehaviour>(true);
				for (int i = all.Length - 1; i >= 0; i--)
				{
					MonoBehaviour mb = all[i];
					if (mb == null || KeepBehaviours.Contains(mb.GetType().Name)) continue;
					Object.DestroyImmediate(mb);
					if (mb != null) left = true;
				}
				if (!left) break;
			}
			foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
		}

		// ---------- the vendor ----------

		private static Vector3 V(List<float> l, Vector3 fallback) =>
			l != null && l.Count >= 3 ? new Vector3(l[0], l[1], l[2]) : fallback;

		private static void AddVendor(Transform root, string key, StallDef def, ZNetScene scene)
		{
			GameObject src = string.IsNullOrEmpty(def.Npc) ? null : scene.GetPrefab(def.Npc);
			if (src == null)
			{
				Plugin.Log.LogWarning($"Stall {key}: vendor creature '{def.Npc}' not found.");
				return;
			}
			GameObject npc;
			bool was = ZNetView.m_forceDisableInit;
			ZNetView.m_forceDisableInit = true;
			try { npc = Object.Instantiate(src, root, false); }
			finally { ZNetView.m_forceDisableInit = was; }
			npc.name = "LLA_StallNpc_" + key;
			npc.transform.localPosition = V(def.NpcPosition, Vector3.zero);
			npc.transform.localRotation = Quaternion.Euler(0f, def.NpcRotation, 0f);
			npc.transform.localScale = Vector3.one * Mathf.Max(0.1f, def.NpcScale);

			// The hat: on the creature's helmet joint if it has one (Fulings), else on its head bone (tuned by hand).
			Transform joint = npc.GetComponentInChildren<VisEquipment>(true)?.m_helmet;
			if (joint == null) joint = FindHead(npc.transform);
			const bool handTuned = true; // helmet joints carry the skeleton's scale too: always place in body space
			if (!string.IsNullOrEmpty(def.Hat) && joint != null && ObjectDB.instance != null)
			{
				GameObject hatItem = ObjectDB.instance.GetItemPrefab(def.Hat);
				Transform attach = hatItem != null ? hatItem.transform.Find("attach") : null;
				if (attach != null)
				{
					GameObject hat = Object.Instantiate(attach.gameObject, joint, false);
					hat.name = "LLA_Hat";
					if (handTuned)
					{
						// Head bones carry odd scales and axes, so the hat is placed in the vendor's own space: position
						// and rotation relative to the body (+Y up, +Z forward), and its true size whatever the bone's scale.
						Quaternion body = npc.transform.rotation;
						hat.transform.position = joint.position + body * V(def.HatPosition, Vector3.zero);
						hat.transform.rotation = body * Quaternion.Euler(V(def.HatRotation, Vector3.zero));
						Vector3 ls = joint.lossyScale;
						float want = Mathf.Max(0.05f, def.HatScale) * npc.transform.lossyScale.x;
						hat.transform.localScale = new Vector3(want / Mathf.Max(1e-4f, Mathf.Abs(ls.x)), want / Mathf.Max(1e-4f, Mathf.Abs(ls.y)), want / Mathf.Max(1e-4f, Mathf.Abs(ls.z)));
					}
					else
					{
						// A real helmet joint: worn exactly like equipment, with the tweaks on top.
						hat.transform.localPosition = V(def.HatPosition, Vector3.zero);
						hat.transform.localRotation = Quaternion.Euler(V(def.HatRotation, Vector3.zero));
						hat.transform.localScale = Vector3.one * Mathf.Max(0.05f, def.HatScale);
					}
					foreach (Collider c in hat.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
					foreach (Light l in hat.GetComponentsInChildren<Light>(true)) l.intensity *= Mathf.Max(0f, def.HatLight);
				}
				else Plugin.Log.LogWarning($"Stall {key}: hat '{def.Hat}' not found.");
			}
			else if (!string.IsNullOrEmpty(def.Hat)) Plugin.Log.LogWarning($"Stall {key}: no head found on {def.Npc} for the hat.");
			if (handTuned) Plugin.Debug($"Stall {key}: hat on bone '{joint?.name}'.");

			Strip(npc);
			foreach (Animator a in npc.GetComponentsInChildren<Animator>(true))
			{
				a.applyRootMotion = false;
				a.fireEvents = false; // footstep and attack events have no listener without the creature's scripts
			}
			int layer = LayerMask.NameToLayer("piece");
			foreach (Collider c in npc.GetComponentsInChildren<Collider>(true)) c.gameObject.layer = layer;

			Trader trader = npc.AddComponent<Trader>();
			trader.m_name = def.Name;
			trader.m_standRange = 12f;
			trader.m_greetRange = 5f;
			trader.m_byeRange = 8f;
			trader.m_hoverOffset = 0f;
			trader.m_items = new List<Trader.TradeItem>();
			trader.m_useItems = new List<Trader.TraderUseItem>();
			trader.m_hideDialogDelay = 6f;
			trader.m_randomTalkInterval = 40f;
			trader.m_dialogHeight = 1.6f;
			trader.m_randomTalk = new List<string>(def.Talk ?? new List<string>());
			trader.m_randomGreets = new List<string> { "Hello!" };
			trader.m_randomGoodbye = new List<string> { "Come back soon!" };
			trader.m_randomStartTrade = new List<string> { "What'll it be?" }; // vanilla picks one when you start trading
			trader.m_randomBuy = new List<string> { "Enjoy!" };
			trader.m_randomSell = new List<string> { "Pleasure doing business." };
			trader.m_randomGiveItemNo = new List<string> { "I don't need that." };
			trader.m_randomUseItemAlreadyRecieved = new List<string> { "You already gave me that." };
			trader.m_randomTalkConditionals = new List<Trader.ConditionalDialog>();
			trader.m_randomTalkFX = new EffectList();
			trader.m_randomGreetFX = new EffectList();
			trader.m_randomGoodbyeFX = new EffectList();
			trader.m_randomStartTradeFX = new EffectList();
			trader.m_randomBuyFX = new EffectList();
			trader.m_randomSellFX = new EffectList();
			npc.AddComponent<StallVendor>().Key = key;
		}

		private static Transform FindHead(Transform t)
		{
			Transform best = null;
			foreach (Transform c in t.GetComponentsInChildren<Transform>(true))
			{
				string n = c.name.ToLowerInvariant();
				if (n == "head") return c;
				if (best == null && n.Contains("head") && !n.Contains("point") && !n.Contains("end")) best = c;
			}
			return best;
		}

		private static Sprite Icon(string key, StallDef def)
		{
			Sprite custom = (!string.IsNullOrEmpty(def.IconFile) ? Weathering.LoadOverride(def.IconFile, 0) : null)
			                ?? Weathering.LoadOverride(key.ToLowerInvariant() + ".png", 0);
			if (custom != null) return custom;
			GameObject item = ObjectDB.instance != null && !string.IsNullOrEmpty(def.Icon) ? ObjectDB.instance.GetItemPrefab(def.Icon) : null;
			ItemDrop drop = item != null ? item.GetComponent<ItemDrop>() : null;
			return drop != null && drop.m_itemData.m_shared.m_icons.Length > 0 ? drop.m_itemData.m_shared.m_icons[0] : null;
		}
	}
}
