using System;
using System.Collections.Generic;

namespace LegendaryLoot.Effects
{
	/// <summary>
	/// Uniques whose named power has working code. A Unique only drops once it's listed here; until then the
	/// roll falls back to a Legendary, exactly like when no Unique fits the item. Unique powers are built from
	/// triggers and effects in step 2, after the core loot loop is tested.
	/// </summary>
	public static class UniqueEffects
	{
		private static readonly HashSet<string> Implemented = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			// Stat-only Uniques can be switched on as soon as their stats are wired up. Step 2 adds the rest.
		};

		public static bool IsImplemented(string id) => Implemented.Contains(id) || Powers.PowerStore.HasUnique(id) || Plugin.AllUniquesForTesting;

		public static void Register(string id) => Implemented.Add(id);
	}
}
