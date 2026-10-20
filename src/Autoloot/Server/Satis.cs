// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;

namespace Autoloot.Server
{
    /// <summary>
    /// The optional link to Satisvampory on the same server. Satisvampory publishes three plain
    /// delegates under an AppDomain key while it runs (and clears it when it unloads); nothing here
    /// references Satisvampory's assembly, so either mod loads, reloads and unloads alone.
    /// Delegates are looked up on every use and never kept, so a reloaded Satisvampory is never pinned.
    /// </summary>
    /// <remarks>
    /// Key <c>satisvampory.autoloot.v1</c>: <c>object[] { int version = 1,
    /// Func&lt;ulong, int, long[]&gt; storage, Func&lt;ulong, string[]&gt; categories, Func&lt;ulong, string, int[]&gt; members }</c>.
    /// <c>storage(platformId, item)</c> is <c>{ island total, cap or -1 }</c> for the player's home island
    /// (null when they have no castle). With no cap the total is 0 because nothing is counted, so it
    /// is only ever used together with a cap (<see cref="Autoloot.Engine.Limits.Room"/> ignores it otherwise); <c>categories(platformId)</c> are category keys; <c>members</c> the items in one.
    /// </remarks>
    internal sealed class Satis
    {
        public const string Key = "satisvampory.autoloot.v1";
        /// <summary>A capped item's total is asked again after this; an item with no cap only after <see cref="UncappedSeconds"/>.</summary>
        const double StorageSeconds = 10;
        const double UncappedSeconds = 15;
        const double MembersSeconds = 60;

        readonly Dictionary<(ulong, int), (double At, long Total, int? Cap)> _storage = new();
        readonly Dictionary<(ulong, string), (double At, HashSet<int> Items)> _members = new();

        static object[]? Api => AppDomain.CurrentDomain.GetData(Key) is object[] { Length: >= 4 } a && a[0] is int v && v >= 1 ? a : null;

        public static bool Present => Api != null;

        /// <summary>Island total and cap for the player's home island; null when unknown.</summary>
        public (long Total, int? Cap)? Storage(ulong account, int item, double now)
        {
            if (_storage.TryGetValue((account, item), out var hit) && now - hit.At < (hit.Cap == null ? UncappedSeconds : StorageSeconds))
                return (hit.Total, hit.Cap);
            if (Api?[1] is not Func<ulong, int, long[]> f) return null;
            long[]? r;
            try { r = f(account, item); }
            catch (Exception) { return null; }
            if (r is not { Length: >= 2 }) return null;
            int? cap = r[1] < 0 ? null : (int)Math.Min(int.MaxValue, r[1]);
            _storage[(account, item)] = (now, r[0], cap);
            return (r[0], cap);
        }

        public static IReadOnlyList<string> Categories(ulong account)
        {
            if (Api?[2] is not Func<ulong, string[]> f) return Array.Empty<string>();
            try { return f(account) ?? Array.Empty<string>(); }
            catch (Exception) { return Array.Empty<string>(); }
        }

        public bool InCategory(ulong account, string category, int item, double now) => Members(account, category, now).Contains(item);

        public HashSet<int> Members(ulong account, string category, double now)
        {
            if (_members.TryGetValue((account, category), out var hit) && now - hit.At < MembersSeconds) return hit.Items;
            var set = new HashSet<int>();
            if (Api?[3] is Func<ulong, string, int[]> f)
            {
                try { foreach (var i in f(account, category) ?? Array.Empty<int>()) set.Add(i); }
                catch (Exception) { }
            }
            _members[(account, category)] = (now, set);
            return set;
        }

        /// <summary>Forget cached numbers for a player (after their filter changes).</summary>
        public void Forget(ulong account)
        {
            foreach (var k in new List<(ulong, string)>(_members.Keys)) if (k.Item1 == account) _members.Remove(k);
            foreach (var k in new List<(ulong, int)>(_storage.Keys)) if (k.Item1 == account) _storage.Remove(k);
        }

        public void Clear()
        {
            _storage.Clear();
            _members.Clear();
        }

        static IReadOnlyList<KeyValuePair<string, string>> _picker = Array.Empty<KeyValuePair<string, string>>();
        static double _pickerAt = double.NegativeInfinity;
        static readonly System.Diagnostics.Stopwatch PickerClock = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>
        /// The categories the filter's item picker offers (id, name): Satisvampory's built-in set when
        /// it runs here, else none. Progenitor asks about once a second; the answer is kept 30 s.
        /// </summary>
        public static IReadOnlyList<KeyValuePair<string, string>> PickerCategories()
        {
            var now = PickerClock.Elapsed.TotalSeconds;
            if (now - _pickerAt < 30) return _picker;
            _pickerAt = now;
            var list = new List<KeyValuePair<string, string>>();
            var keys = new List<string>(Categories(0));
            keys.Sort(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                if (list.Count >= 64) break; // the most Progenitor sends
                if (key.Length == 0 || key.Length > 32) continue;
                list.Add(new KeyValuePair<string, string>(key.ToLowerInvariant(), TitleCase(key)));
            }
            return _picker = list;
        }

        /// <summary>"heavy armour" to "Heavy Armour".</summary>
        public static string TitleCase(string key)
        {
            var chars = key.ToCharArray();
            var start = true;
            for (var i = 0; i < chars.Length; i++)
            {
                if (start && char.IsLetter(chars[i])) chars[i] = char.ToUpperInvariant(chars[i]);
                start = chars[i] == ' ' || chars[i] == '-';
            }
            return new string(chars);
        }
    }
}
