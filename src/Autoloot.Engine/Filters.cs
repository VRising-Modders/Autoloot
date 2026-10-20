// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;

namespace Autoloot.Engine
{
    /// <summary>
    /// One line of a player's filter: an item, or a Satisvampory category, and the most of it the
    /// player wants in their bags. 0 means never pick it up.
    /// </summary>
    public sealed class FilterEntry
    {
        /// <summary>The item's prefab id, or 0 for a category line.</summary>
        public int Item { get; set; }

        /// <summary>The Satisvampory category key when <see cref="Item"/> is 0.</summary>
        public string Category { get; set; } = "";

        public int Max { get; set; }

        public bool IsCategory => Item == 0 && Category.Length > 0;
    }

    public static class Limits
    {
        public const int MaxCount = 100_000;

        /// <summary>
        /// The most of <paramref name="item"/> the player wants in their bags, or null for no limit.
        /// An item line wins over category lines; among categories that hold the item the smallest wins.
        /// </summary>
        public static int? MaxFor(int item, IReadOnlyList<FilterEntry> filters, Func<string, int, bool> inCategory)
        {
            int? fromCategory = null;
            foreach (var f in filters)
            {
                if (f.Item == item && item != 0) return Math.Max(0, f.Max);
                if (!f.IsCategory || !inCategory(f.Category, item)) continue;
                var m = Math.Max(0, f.Max);
                if (fromCategory is not { } c || m < c) fromCategory = m;
            }
            return fromCategory;
        }

        /// <summary>
        /// How many more of an item the player may pick up now: the least of what their filter
        /// leaves, what the Satisvampory cap leaves (bags plus island storage count against it),
        /// and what fits in their bags. Never negative.
        /// </summary>
        /// <param name="filterMax">From <see cref="MaxFor"/>; null for no limit.</param>
        /// <param name="inBags">How many the player carries now.</param>
        /// <param name="cap">The island cap, or null for none (or when caps are not used).</param>
        /// <param name="inStorage">How many the island holds.</param>
        /// <param name="fits">How many more fit in the bags.</param>
        public static int Room(int? filterMax, int inBags, int? cap, long inStorage, int fits)
        {
            long room = Math.Max(0, fits);
            if (filterMax is { } m) room = Math.Min(room, (long)m - inBags);
            if (cap is { } c) room = Math.Min(room, c - inStorage - inBags);
            return (int)Math.Max(0, room);
        }
    }
}
