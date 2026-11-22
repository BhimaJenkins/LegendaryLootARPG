using System.Collections.Generic;
using UnityEngine;

namespace LegendaryLoot.Vendors
{
	/// <summary>
	/// The one dungeon you're in. Valheim also loads the interiors of nearby dungeons (they all sit high above the map),
	/// so rooms, spawners and chests are only counted when their nearest room belongs to this dungeon's generator.
	/// </summary>
	public class Dungeon
	{
		public readonly List<Room> Rooms = new List<Room>();
		public string Name = "?";
		public Room Entrance;
		private readonly HashSet<Room> _mine = new HashSet<Room>();
		private Room[] _all = new Room[0];

		public static Dungeon At(Vector3 pos)
		{
			var d = new Dungeon();
			d._all = Object.FindObjectsOfType<Room>();
			Room nearest = null;
			float best = float.MaxValue;
			foreach (Room r in d._all)
			{
				if (r == null) continue;
				float dist = (r.transform.position - pos).sqrMagnitude;
				if (dist < best) { best = dist; nearest = r; }
			}
			DungeonGenerator gen = nearest != null ? nearest.GetComponentInParent<DungeonGenerator>() : null;
			if (gen != null)
			{
				d.Name = gen.name;
				foreach (Room r in gen.GetComponentsInChildren<Room>())
				{
					d.Rooms.Add(r);
					d._mine.Add(r);
					if (r.m_entrance && d.Entrance == null) d.Entrance = r;
				}
			}
			else if (nearest != null)
			{
				d.Rooms.Add(nearest);
				d._mine.Add(nearest);
			}
			return d;
		}

		/// <summary>True when the point's nearest room is one of this dungeon's (and not too far from it).</summary>
		public bool Contains(Vector3 pos)
		{
			Room nearest = null;
			float best = float.MaxValue;
			foreach (Room r in _all)
			{
				if (r == null) continue;
				float dist = (r.transform.position - pos).sqrMagnitude;
				if (dist < best) { best = dist; nearest = r; }
			}
			return nearest != null && _mine.Contains(nearest) && best < 40f * 40f;
		}
	}
}
