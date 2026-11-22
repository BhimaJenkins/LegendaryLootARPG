using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using HarmonyLib;
using LegendaryLoot.Core;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Networking;

namespace LegendaryLoot
{
	/// <summary>
	/// Loads .ogg files from the mod's audio folder (same folder layouts as Keen Combat, so r2modman and manual
	/// installs both work) and plays them in the world on the game's sound-effects volume.
	/// </summary>
	public static class SoundLibrary
	{
		private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
		private static AudioMixerGroup _sfxGroup;

		public static void Load(MonoBehaviour host, params string[] files)
		{
			foreach (string f in files) host.StartCoroutine(LoadClip(f));
		}

		public static AudioClip Get(string file) => Clips.TryGetValue(file, out AudioClip c) ? c : null;

		/// <summary>Loads every .ogg in the audio folder whose name starts with prefix (boogie1.ogg, boogie2.ogg...).</summary>
		public static void LoadPrefix(MonoBehaviour host, string prefix)
		{
			string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
			foreach (string dir in new[] { Path.Combine(pluginDir, "audio"), pluginDir })
			{
				if (!Directory.Exists(dir)) continue;
				foreach (string file in Directory.GetFiles(dir, prefix + "*.ogg"))
					host.StartCoroutine(LoadClip(Path.GetFileName(file)));
			}
		}

		/// <summary>Loaded clips whose name starts with prefix.</summary>
		public static List<string> FindPrefix(string prefix)
		{
			var list = new List<string>();
			foreach (string k in Clips.Keys)
				if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) list.Add(k);
			return list;
		}

		public static void PlayAt(string file, Vector3 position, float volume = 1f, float maxDistance = 40f, bool dry = false)
		{
			AudioClip clip = Get(file);
			if (clip == null) return;
			var go = new GameObject("LLA_Audio_" + clip.name);
			go.transform.position = position;
			var src = go.AddComponent<AudioSource>();
			src.spatialBlend = 1f;
			src.maxDistance = maxDistance;
			src.rolloffMode = AudioRolloffMode.Linear;
			if (dry)
			{
				// Voices: no room echo. The game's sound-effects channel adds reverb, so voices go through the
				// interface channel (still heard from where the vendor stands) and skip reverb zones.
				src.outputAudioMixerGroup = GetGroup("GUI") ?? GetSfxGroup();
				src.bypassReverbZones = true;
				src.reverbZoneMix = 0f;
			}
			else src.outputAudioMixerGroup = GetSfxGroup();
			src.PlayOneShot(clip, volume);
			UnityEngine.Object.Destroy(go, clip.length + 0.5f);
		}

		private static readonly Dictionary<string, AudioMixerGroup> Groups = new Dictionary<string, AudioMixerGroup>();

		private static AudioMixerGroup GetGroup(string name)
		{
			if (Groups.TryGetValue(name, out AudioMixerGroup g) && g != null) return g;
			if (AudioMan.instance == null || AudioMan.instance.m_masterMixer == null) return null;
			AudioMixerGroup[] found = AudioMan.instance.m_masterMixer.FindMatchingGroups(name);
			g = found != null && found.Length > 0 ? found[0] : null;
			Groups[name] = g;
			return g;
		}

		private static AudioMixerGroup GetSfxGroup()
		{
			if (_sfxGroup != null || AudioMan.instance == null || AudioMan.instance.m_masterMixer == null) return _sfxGroup;
			AudioMixerGroup[] groups = AudioMan.instance.m_masterMixer.FindMatchingGroups("SFX");
			if (groups != null && groups.Length > 0) _sfxGroup = groups[0];
			return _sfxGroup;
		}

		public static string FindAudioFile(string filename)
		{
			string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
			string[] candidates =
			{
				Path.Combine(pluginDir, "audio", filename),
				Path.Combine(pluginDir, filename),
				Path.Combine(pluginDir, "LegendaryLootARPG", "audio", filename),
				Path.Combine(pluginDir, "LegendaryLootARPG", filename),
			};
			foreach (string c in candidates)
				if (File.Exists(c)) return c;
			return null;
		}

		private static IEnumerator LoadClip(string filename)
		{
			string path = FindAudioFile(filename);
			if (path == null)
			{
				Plugin.Log.LogWarning($"Audio file not found: {filename} (put it in the mod's audio folder)");
				yield break;
			}
			using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace("\\", "/"), AudioType.OGGVORBIS))
			{
				yield return request.SendWebRequest();
				if (request.result != UnityWebRequest.Result.Success)
				{
					Plugin.Log.LogWarning($"Couldn't load {filename}: {request.error}");
					yield break;
				}
				AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
				clip.name = Path.GetFileNameWithoutExtension(filename);
				Clips[filename] = clip;
				Plugin.Log.LogInfo($"Sound loaded: {filename} ({clip.length:0.00} s) from {path}");
			}
		}
	}

	/// <summary>
	/// Plays magicdrop.ogg when a magic item drops. The drop time is stored on the item, so every nearby player
	/// hears it once when the item first appears, but not when an old drop loads in later.
	/// </summary>
	public static class DropSound
	{
		public const string File = "magicdrop.ogg";
		public const string UniqueFile = "uniqueitemsound.ogg";
		public const string DropTimeKey = "LLA.dropTime";
		private const double FreshSeconds = 5.0;

		public static void MarkFresh(ItemDrop.ItemData item)
		{
			if (ZNet.instance == null) return;
			item.m_customData[DropTimeKey] = ZNet.instance.GetTimeSeconds().ToString("0.0", CultureInfo.InvariantCulture);
		}

		private static void TryPlay(ItemDrop drop)
		{
			ItemDrop.ItemData item = drop.m_itemData;
			if (item?.m_customData == null || ZNet.instance == null || !item.IsMagic()) return;
			if (!item.m_customData.TryGetValue(DropTimeKey, out string text)) return;
			if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double t)) return;
			if (ZNet.instance.GetTimeSeconds() - t > FreshSeconds) return;
			// Uniques get their own sound (falls back to the normal one if uniqueitemsound.ogg is missing).
			bool unique = item.GetMagic()?.Tier == Tier.Unique;
			SoundLibrary.PlayAt(unique && SoundLibrary.Get(UniqueFile) != null ? UniqueFile : File, drop.transform.position);
		}

		[HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.Start))]
		private static class Patch_Start
		{
			private static void Postfix(ItemDrop __instance) => TryPlay(__instance);
		}
	}
}
