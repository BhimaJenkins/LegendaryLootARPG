using System;
using System.Collections.Generic;
using System.Globalization;

namespace LegendaryLoot.Powers
{
	/// <summary>
	/// powers.yml: what affixes and Uniques actually do, built from a trigger, optional conditions, and effects.
	///
	///   affixes:
	///     LifeSteal:                       # affix ID from the Sheet
	///       - trigger: onHit
	///         effects:
	///           - { type: heal, percentOfDamage: X }   # X = the affix's rolled value
	///   uniques:
	///     JarlSlayer:                      # Unique ID (its name without spaces)
	///       - trigger: onHit
	///         conditions: { targetMinStars: 1 }
	///         effects:
	///           - { type: addDamage, percent: 25 }
	///
	/// Numbers can be plain (25), the rolled value (X), or simple math on it (X*2, X/2, X+5).
	/// </summary>
	public class PowersFile
	{
		public Dictionary<string, List<PowerDef>> Affixes = new Dictionary<string, List<PowerDef>>();
		public Dictionary<string, List<PowerDef>> Uniques = new Dictionary<string, List<PowerDef>>();
	}

	public class PowerDef
	{
		/// <summary>passive, onHit, onKill, whenHit, onBlock, onParry, onLethal, every</summary>
		public string Trigger = "passive";
		/// <summary>Seconds between checks, for trigger "every".</summary>
		public string Every;
		/// <summary>Percent chance to fire each time the trigger happens (blank = always).</summary>
		public string Chance;
		/// <summary>Seconds before it can fire again.</summary>
		public string Cooldown;
		/// <summary>Times it can fire before the cooldown starts (blank = 1). E.g. charges 2, cooldown 180:
		/// two uses, then a 3-minute wait.</summary>
		public string Charges;
		public Dictionary<string, string> Conditions = new Dictionary<string, string>();
		public List<Dictionary<string, string>> Effects = new List<Dictionary<string, string>>();

		[NonSerialized] public string Key; // unique key for cooldowns, set when loaded
	}

	/// <summary>Evaluates "X", "25", "X*2", "X/2", "X+5", "X-1" with the rolled value.</summary>
	public static class PowerMath
	{
		public static float Eval(string text, float x, float fallback = 0f)
		{
			if (string.IsNullOrWhiteSpace(text)) return fallback;
			string s = text.Trim().Replace(" ", "");
			if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float plain)) return plain;
			if (s.Equals("X", StringComparison.OrdinalIgnoreCase)) return x;
			if (s.Length > 2 && (s[0] == 'X' || s[0] == 'x'))
			{
				char op = s[1];
				if (float.TryParse(s.Substring(2), NumberStyles.Float, CultureInfo.InvariantCulture, out float n))
				{
					switch (op)
					{
						case '*': return x * n;
						case '/': return n != 0f ? x / n : x;
						case '+': return x + n;
						case '-': return x - n;
					}
				}
			}
			Plugin.Log.LogWarning($"powers.yml: can't read the number '{text}'. Using {fallback}.");
			return fallback;
		}

		public static float Get(Dictionary<string, string> map, string key, float x, float fallback = 0f)
		{
			return map != null && map.TryGetValue(key, out string v) ? Eval(v, x, fallback) : fallback;
		}

		public static string GetText(Dictionary<string, string> map, string key, string fallback = null)
		{
			return map != null && map.TryGetValue(key, out string v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;
		}

		public static bool Has(Dictionary<string, string> map, string key) => map != null && map.ContainsKey(key);
	}
}
