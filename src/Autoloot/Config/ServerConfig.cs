// SPDX-License-Identifier: AGPL-3.0-only
using System;
using BepInEx.Configuration;
using Progenitor.Plugins;
using Progenitor.Ui;
using Autoloot.Engine;

namespace Autoloot.Config
{
    /// <summary>
    /// The admin settings, as server-wide settings (<c>scope: SettingScope.ServerWide</c>): the server's
    /// value wins on every client while joined, admins edit them from the mod's page, and Progenitor
    /// shows them (read-only for non-admins) even to players without Autoloot's client part.
    /// </summary>
    public sealed class ServerConfig
    {
        const string Section = "Server";

        public static readonly string[] PickupChoices = { nameof(PickupRule.ComesInRange), nameof(PickupRule.LandsInRange) };
        public static readonly string[] PickupLabels = { "Any drop that comes in range", "Only drops that land in range" };
        public static readonly string[] ShareChoices = { nameof(ShareRule.Closest), nameof(ShareRule.Even) };
        public static readonly string[] ShareLabels = { "Closest player first", "Take turns (even)" };
        public static readonly string[] DeathBagChoices = { nameof(DeathBagRule.Off), nameof(DeathBagRule.Own), nameof(DeathBagRule.Anyone) };
        public static readonly string[] DeathBagLabels = { "Never", "Only the player's own", "Anyone the game lets open it" };
        public static readonly string[] DroppedBagChoices = { nameof(DroppedBagRule.Off), nameof(DroppedBagRule.Others) };
        public static readonly string[] DroppedBagLabels = { "Never", "Anyone except the player who dropped it" };

        public ConfigEntry<bool> Enabled { get; private set; } = null!;
        public ConfigEntry<int> Radius { get; private set; } = null!;
        public ConfigEntry<string> Pickup { get; private set; } = null!;
        public ConfigEntry<string> Share { get; private set; } = null!;
        public ConfigEntry<string> DeathBags { get; private set; } = null!;
        public ConfigEntry<string> DroppedBags { get; private set; } = null!;
        public ConfigEntry<bool> NoClientAutoloot { get; private set; } = null!;
        public ConfigEntry<bool> NoClientHint { get; private set; } = null!;

        /// <summary>Raised when any server setting changes.</summary>
        public event Action? Changed;

        /// <summary>Raised when the pickup range changes (an admin's edit, or the server's value arriving).</summary>
        public event Action? RadiusChanged;

        ServerConfig()
        {
        }

        public static ServerConfig Bind(PluginContext ctx)
        {
            var c = new ServerConfig();
            c.Enabled = ctx.Bind(Section, "Enabled", true, SettingScope.ServerWide, description: "Players may use autoloot. Off: nothing is picked up for anyone.");
            c.Radius = ctx.Bind(Section, "Radius", (int)ServerRules.DefaultRadius, SettingScope.ServerWide, description: "How far from a player drops are picked up, in metres.", acceptableValues: new AcceptableValueRange<int>((int)ServerRules.MinRadius, (int)ServerRules.MaxRadius));
            c.Pickup = ctx.Bind(Section, "Pickup", PickupChoices[0], SettingScope.ServerWide, description: "Which drops are picked up: ComesInRange or LandsInRange.", acceptableValues: new AcceptableValueList<string>(PickupChoices));
            c.Share = ctx.Bind(Section, "Share", ShareChoices[0], SettingScope.ServerWide, description: "Who gets a drop several players could take: Closest or Even.", acceptableValues: new AcceptableValueList<string>(ShareChoices));
            c.DeathBags = ctx.Bind(Section, "DeathBags", DeathBagChoices[1], SettingScope.ServerWide, description: "Whose death bags are emptied into a player's bags: Off, Own or Anyone (never beyond the game's own rule).", acceptableValues: new AcceptableValueList<string>(DeathBagChoices));
            c.DroppedBags = ctx.Bind(Section, "DroppedBags", DroppedBagChoices[0], SettingScope.ServerWide, description: "Whether a whole inventory a player dropped is picked up: Off, or Others (anyone except the dropper).", acceptableValues: new AcceptableValueList<string>(DroppedBagChoices));
            c.NoClientAutoloot = ctx.Bind(Section, "NoClientAutoloot", true, SettingScope.ServerWide, description: "Players without the Autoloot client part get autoloot with the defaults (on, no filter).");
            c.NoClientHint = ctx.Bind(Section, "NoClientHint", true, SettingScope.ServerWide, description: "Tell a player without the client part, once per login, what autoloot does and how to get the rest.");
            // Every entry is in the plugin's one file: one handler, removed on unload.
            c.Enabled.ConfigFile.SettingChanged += c.OnFileChanged;
            ctx.Track(new OnDispose(() => c.Enabled.ConfigFile.SettingChanged -= c.OnFileChanged));
            return c;
        }

        void OnFileChanged(object? sender, SettingChangedEventArgs e)
        {
            if (e.ChangedSetting.Definition.Section != Section) return;
            Changed?.Invoke();
            if (ReferenceEquals(e.ChangedSetting, Radius)) RadiusChanged?.Invoke();
        }

        /// <summary>The current values as the engine reads them.</summary>
        public ServerRules Snapshot() => new()
        {
            Enabled = Enabled.Value,
            Radius = Math.Clamp(Radius.Value, ServerRules.MinRadius, ServerRules.MaxRadius),
            Pickup = Enum.TryParse<PickupRule>(Pickup.Value, out var p) ? p : PickupRule.ComesInRange,
            Share = Enum.TryParse<ShareRule>(Share.Value, out var s) ? s : ShareRule.Closest,
            DeathBags = Enum.TryParse<DeathBagRule>(DeathBags.Value, out var d) ? d : DeathBagRule.Own,
            DroppedBags = Enum.TryParse<DroppedBagRule>(DroppedBags.Value, out var b) ? b : DroppedBagRule.Off,
            NoClientAutoloot = NoClientAutoloot.Value,
            NoClientHint = NoClientHint.Value,
        };

        /// <summary>The range in words: "10 m (2 castle floor tiles)". A floor tile is 5 m across.</summary>
        public static string RangeWords(int metres)
        {
            var tiles = metres / 5.0;
            var t = tiles == Math.Floor(tiles) ? ((int)tiles).ToString() : tiles.ToString("0.#");
            return $"{metres} m from you in every direction, about {t} castle floor tile{(tiles == 1 ? "" : "s")}. A full circle is {metres * 2} m across.";
        }
    }
}
