// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;

namespace Autoloot.Engine
{
    /// <summary>One pile on the ground, as one scan saw it.</summary>
    public sealed class Drop
    {
        /// <summary>Stable while the pile exists (the entity's index and version).</summary>
        public long Key { get; set; }
        public int Item { get; set; }
        public int Amount { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        /// <summary>A player put it there from their bags: never picked up automatically.</summary>
        public bool PlayerDropped { get; set; }

        /// <summary>The game has put it to sleep (nobody near). Still remembered, never picked up.</summary>
        public bool Asleep { get; set; }
    }

    /// <summary>One online player who may pick things up.</summary>
    public sealed class Looter
    {
        public ulong Account { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        /// <summary>The player's own switch (and not dead or otherwise unable).</summary>
        public bool On { get; set; }

        public float DistanceTo(Drop d)
        {
            float dx = d.X - X, dy = d.Y - Y, dz = d.Z - Z;
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    /// <summary>
    /// What the scans remember about each pile between ticks: who was in range when it first
    /// appeared (for <see cref="PickupRule.LandsInRange"/>), and whether a player dropped it.
    /// Piles that already lay there at the first scan count as seen by nobody.
    /// </summary>
    public sealed class DropMemory
    {
        /// <summary>
        /// A pile of the item that appears this close to a player's drop, this close in time, is theirs.
        /// The game throws a dropped stack a little way along the character's facing.
        /// </summary>
        public const float PlayerDropMetres = 4;
        public const double PlayerDropSeconds = 5;

        /// <summary>A pile may be seen this long before its mark arrives (the drop event comes a frame or so after the drop).</summary>
        public const double MarkLeadSeconds = 1;

        /// <summary>Marks for the same item this close in place and time are one mark.</summary>
        const float SameMarkMetres = 0.5f;
        const double SameMarkSeconds = 0.25;

        sealed class Known
        {
            /// <summary>Null when nobody was in range (most piles): no set is made for them.</summary>
            public HashSet<ulong>? Saw;
            public int Item;
            public float X, Y, Z;
            public double FirstSeen;
            public int FirstScan;
            public bool Old;
            public bool PlayerDropped;
        }

        readonly Dictionary<long, Known> _known = new();
        // Only piles that appeared recently can match a mark; marks never look at the rest.
        readonly List<Known> _recent = new();
        readonly List<(int Item, float X, float Y, float Z, double At)> _marks = new();
        readonly HashSet<long> _present = new();
        readonly List<long> _gone = new();
        bool _primed;
        bool _oldHandsOff;
        int _scan;
        readonly HashSet<long> _restored = new();

        public int Count => _known.Count;

        /// <summary>Changes whenever a pile is marked player-dropped or one is forgotten: time to save.</summary>
        public int Revision { get; private set; }

        /// <summary>The piles currently known to be a player's drop (to keep across a reload).</summary>
        public List<long> PlayerDroppedKeys()
        {
            var list = new List<long>();
            foreach (var kv in _known) if (kv.Value.PlayerDropped) list.Add(kv.Key);
            foreach (var key in _restored) if (!_known.ContainsKey(key)) list.Add(key);
            return list;
        }

        /// <summary>
        /// Start from a saved list (the same world, after a reload of this mod): those piles are a
        /// player's drop. Every other pile already lying there is old, as usual.
        /// </summary>
        public void Restore(IEnumerable<long> playerDropped)
        {
            foreach (var key in playerDropped) _restored.Add(key);
            Revision++;
        }

        /// <summary>
        /// Nothing saved to go on (a restart, a world reload): every pile already lying there may be
        /// someone's drop, so it is never taken. Piles that appear from now on are judged as usual.
        /// </summary>
        public void OldPilesAreHandsOff() => _oldHandsOff = true;

        public int MarkCount => _marks.Count;

        /// <summary>A player just dropped <paramref name="item"/> from their bags where they stand.</summary>
        public void MarkPlayerDrop(int item, float x, float y, float z, double now)
        {
            foreach (var m in _marks)
            {
                if (m.Item != item || now - m.At > SameMarkSeconds) continue;
                float dx = m.X - x, dy = m.Y - y, dz = m.Z - z;
                if (dx * dx + dy * dy + dz * dz <= SameMarkMetres * SameMarkMetres) return;
            }
            _marks.Add((item, x, y, z, now));
        }

        /// <summary>
        /// Call once per scan with every pile and every online player. Sets
        /// <see cref="Drop.PlayerDropped"/> and forgets piles that are gone. A drop mark may arrive a
        /// little before or after its pile is first seen; either way it matches.
        /// </summary>
        public void Observe(IReadOnlyList<Drop> drops, IReadOnlyList<Looter> looters, float radius, double now)
        {
            _scan++;
            _present.Clear();
            foreach (var d in drops)
            {
                _present.Add(d.Key);
                if (_known.TryGetValue(d.Key, out var seen))
                {
                    // While it is new, follow where it settles: a mark is matched against where it really lies.
                    if (!seen.Old && now - seen.FirstSeen <= PlayerDropSeconds) { seen.X = d.X; seen.Y = d.Y; seen.Z = d.Z; }
                    continue;
                }
                var k = new Known { Item = d.Item, X = d.X, Y = d.Y, Z = d.Z, FirstSeen = now, FirstScan = _scan, Old = !_primed };
                if (_restored.Remove(d.Key) || (!_primed && _oldHandsOff)) { k.PlayerDropped = true; Revision++; }
                if (_primed)
                {
                    foreach (var l in looters)
                        if (l.On && l.DistanceTo(d) <= radius) (k.Saw ??= new HashSet<ulong>()).Add(l.Account);
                    _recent.Add(k);
                }
                _known[d.Key] = k;
            }
            if (_known.Count != _present.Count)
            {
                _gone.Clear();
                foreach (var key in _known.Keys)
                    if (!_present.Contains(key)) _gone.Add(key);
                foreach (var key in _gone) _known.Remove(key);
                Revision++;
            }
            // Restored keys whose pile is gone by the first scan: nothing to keep.
            if (_primed) _restored.Clear();

            // A mark stays for its whole window, so two stacks dropped together are both claimed.
            for (var i = _marks.Count - 1; i >= 0; i--)
                if (now - _marks[i].At > PlayerDropSeconds) _marks.RemoveAt(i);
            var keep = 0;
            for (var i = 0; i < _recent.Count; i++)
                if (now - _recent[i].FirstSeen <= PlayerDropSeconds + MarkLeadSeconds) _recent[keep++] = _recent[i];
            _recent.RemoveRange(keep, _recent.Count - keep);
            if (_marks.Count > 0 && _recent.Count > 0)
                foreach (var m in _marks) Match(m);
            foreach (var d in drops) d.PlayerDropped = _known[d.Key].PlayerDropped;
            _primed = true;
        }

        /// <summary>Whether <paramref name="account"/> was in range when the pile appeared.</summary>
        public bool Saw(long key, ulong account) => _known.TryGetValue(key, out var k) && k.Saw != null && k.Saw.Contains(account);

        /// <summary>
        /// First seen by the latest scan. Such a pile waits one scan, so a bag drop reported a moment
        /// late still marks it before anyone takes it.
        /// </summary>
        public bool IsNew(long key) => _known.TryGetValue(key, out var k) && k.FirstScan == _scan && !k.Old;

        /// <summary>Start over, as after a world reload: every pile now lying there is old and hands-off.</summary>
        public void Reset()
        {
            _known.Clear();
            _recent.Clear();
            _marks.Clear();
            _restored.Clear();
            _primed = false;
            _oldHandsOff = true;
            Revision++;
        }

        /// <summary>A mark claims every new pile of its item near it that appeared from one scan before it on.</summary>
        void Match((int Item, float X, float Y, float Z, double At) m)
        {
            foreach (var k in _recent)
            {
                if (k.PlayerDropped || k.Item != m.Item) continue;
                if (k.FirstSeen < m.At - MarkLeadSeconds || k.FirstSeen > m.At + PlayerDropSeconds) continue;
                float dx = k.X - m.X, dy = k.Y - m.Y, dz = k.Z - m.Z;
                if (dx * dx + dy * dy + dz * dz > PlayerDropMetres * PlayerDropMetres) continue;
                k.PlayerDropped = true;
                Revision++;
            }
        }
    }
}
