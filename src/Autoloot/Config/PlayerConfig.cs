// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using Autoloot.Engine;
using BepInEx.Configuration;
using Progenitor.Players;
using Progenitor.Plugins;
using Progenitor.Ui;

namespace Autoloot.Config
{
    /// <summary>
    /// The player's own settings. Everything the server acts on is kept by the server, per player
    /// (<c>ctx.Settings.PerPlayer</c>, <c>PerPlayerItemList</c>): the three switches and the pickup
    /// filter. Progenitor shows them on Autoloot's page in the Mods menu for every player with
    /// Progenitor, also without Autoloot's client part, and the filter is edited there with the
    /// game's item picker. Showing the range ring is a preference on the player's own PC.
    /// </summary>
    public sealed class PlayerConfig
    {
        const string Section = "Autoloot";

        public PlayerSetting<bool> On { get; private set; } = null!;
        public PlayerSetting<bool> UseSatisvamporyCaps { get; private set; } = null!;
        public PlayerSetting<bool> Notices { get; private set; } = null!;

        /// <summary>Items (and Satisvampory categories) with the most of each the player wants in their bags.</summary>
        public PlayerItemListSetting Filter { get; private set; } = null!;

        /// <summary>Local only (never sent): draw the pickup range on the ground around me.</summary>
        public ConfigEntry<bool> ShowRange { get; private set; } = null!;

        /// <summary>Server: a player's filter changed (cached numbers for them are stale).</summary>
        public event Action<ulong>? FilterChanged;

        // Server: each player's filter, re-read at most every FilterRecheckSeconds (the setting parses on every read,
        // and an edit to the players file made outside the game raises no Changed, so the stored text is compared).
        readonly Dictionary<ulong, (double At, string Text, IReadOnlyList<FilterEntry> Lines)> _filters = new();
        readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        const double FilterRecheckSeconds = 2;

        PlayerConfig()
        {
        }

        public const int MaxFilterLines = 200;

        public static PlayerConfig Bind(PluginContext ctx)
        {
            var c = new PlayerConfig();
            c.On = ctx.Settings.PerPlayer(Section, "Autoloot", true, "Pick up drops near you automatically.");
            c.UseSatisvamporyCaps = ctx.Settings.PerPlayer(Section, "UseSatisvamporyCaps", true,
                "With Satisvampory on this server: stop picking up an item once your island holds its cap (your bags count too).");
            c.Notices = ctx.Settings.PerPlayer(Section, "PickupNotices", true,
                "A short banner every few seconds while autoloot picks things up: each item, how many, and how many you now have in your bags. Needs Autoloot's client part.");
            c.Filter = ctx.Settings.PerPlayerItemList(Section, "PickupFilter",
                "The most of each item you want in your bags; autoloot stops picking it up there. 0 means never. Items not listed are always picked up while there is room.",
                new ItemListRules
                {
                    MaxEntries = MaxFilterLines,
                    MaxCount = Limits.MaxCount,
                    AllowCategories = true,
                    // Server: Satisvampory's categories, when it runs here (the built-in set; a player's own island edits still decide membership).
                    Categories = Server.Satis.PickerCategories,
                    CountLabel = "Most in bags",
                });
            if (ctx.IsServer)
            {
                c.Filter.Changed += c.OnFilterChanged;
                ctx.Track(new OnDispose(() => c.Filter.Changed -= c.OnFilterChanged));
            }
            c.ShowRange = ctx.Bind("Me", "ShowRange", false, "Draw the pickup range on the ground around you.");
            c.ShowRange.SettingChanged += OnShowRange;
            ctx.Track(new OnDispose(() => c.ShowRange.SettingChanged -= OnShowRange));
            return c;
        }

        void OnFilterChanged(PlayerAccountId player, ItemLimitList list)
        {
            _filters.Remove(player.Value);
            FilterChanged?.Invoke(player.Value);
        }

        static void OnShowRange(object? sender, EventArgs e)
        {
            // The file also holds the server's synced settings, so while joined Progenitor keeps it
            // from autosaving; an explicit save of the player's own value is safe.
            try { (sender as ConfigEntryBase)?.ConfigFile.Save(); }
            catch (Exception) { } // saved at the next change or on quit
        }

        // ---- server ----

        public bool OnFor(ulong account) => On.For(PlayerAccountId.From(account));
        public bool CapsFor(ulong account) => UseSatisvamporyCaps.For(PlayerAccountId.From(account));
        public bool NoticesFor(ulong account) => Notices.For(PlayerAccountId.From(account));

        /// <summary>The player's filter as the engine reads it, parsed again only when its stored text changes.</summary>
        public IReadOnlyList<FilterEntry> FilterFor(ulong account)
        {
            var now = _clock.Elapsed.TotalSeconds;
            if (_filters.TryGetValue(account, out var known) && now - known.At < FilterRecheckSeconds) return known.Lines;
            var lines = Filter.For(PlayerAccountId.From(account));
            var text = lines.ToString();
            if (known.Lines != null && known.Text == text)
            {
                _filters[account] = (now, text, known.Lines);
                return known.Lines;
            }
            var list = new List<FilterEntry>();
            foreach (var line in lines)
                list.Add(new FilterEntry { Item = line.Item, Category = line.IsCategory ? line.Category.ToLowerInvariant() : "", Max = line.Max });
            if (_filters.Count > 512) _filters.Clear();
            _filters[account] = (now, text, list);
            if (known.Lines != null) FilterChanged?.Invoke(account);
            return list;
        }

        // ---- client: this player's value as the joined server sent it (the default until then) ----

        public bool MyOn => On.HasMine ? On.Mine : true;
    }

    /// <summary>A disposable that runs one action once.</summary>
    internal sealed class OnDispose : IDisposable
    {
        Action? _action;

        internal OnDispose(Action action) => _action = action;

        public void Dispose()
        {
            var a = _action;
            _action = null;
            a?.Invoke();
        }
    }
}
