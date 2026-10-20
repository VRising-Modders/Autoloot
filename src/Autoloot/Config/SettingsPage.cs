// SPDX-License-Identifier: AGPL-3.0-only
using Autoloot.Client;
using Progenitor.Plugins;
using Progenitor.Ui;

namespace Autoloot.Config
{
    /// <summary>
    /// Autoloot's page in Progenitor's Mods menu: the player's own settings, the server settings
    /// (admins edit them, everyone else sees them), and help. The Help button at the top of the
    /// page opens the guide with the range drawn to scale.
    /// </summary>
    public static class SettingsPage
    {
        public static void Build(PluginContext ctx, ServerConfig server, PlayerConfig player, ClientHooks window)
        {
            ctx.Settings.Page(page =>
            {
                // Kept by the server per player: these rows also give the labels, order and conditions that
                // Progenitor uses to draw them for players without Autoloot's client part.
                page.Section("Me");
                page.Toggle(player.On, label: "Autoloot", help: "Pick up drops near you automatically.")
                    .EnableWhen(SettingCondition.Is(server.Enabled, true));
                page.ItemList(player.Filter, label: "Pickup filter",
                    help: "Items you want less of: the most of each to carry, 0 for never. Items not listed are always picked up while there is room. " +
                          "With Satisvampory you can add its categories too: the number applies to each item in the category, an item line beats a category line, and the smaller of two categories wins.");
                page.Toggle(player.UseSatisvamporyCaps, label: "Respect Satisvampory caps",
                    help: "Stop picking up an item once your island holds its Satisvampory cap. Your bags count too.")
                    .ShowWhen(SettingCondition.ServerHasMod("satisvampory"));
                page.Toggle(player.Notices, label: "Pickup notices",
                    help: "A short banner every few seconds while autoloot picks things up: each item, how many were picked up, and how many you now have in your bags. Needs Autoloot's client part.");
                page.Toggle(player.ShowRange, label: "Show my pickup range",
                    help: "Draws the range on the ground around you: a red ring at the range. Grey while autoloot is off for you.");
                if (ctx.IsClient)
                    page.Keybind(Client.ClientHalf.ShowRangeKey, label: "Show my pickup range (key)", help: "Turns the ring on and off. Unbound until you pick a key.");

                page.Section("Server");
                page.Toggle(server.Enabled, label: "Autoloot for players", help: "Off: nothing is picked up for anyone.", scope: SettingScope.Server);
                page.Slider(server.Radius, min: Engine.ServerRules.MinRadius, max: Engine.ServerRules.MaxRadius, step: 1, units: "m",
                    label: "Pickup range", help: "How far from a player drops are picked up. The guide (Help) draws it to scale.", scope: SettingScope.Server);
                page.Info("How far that is", () => ServerConfig.RangeWords(server.Radius.Value), refreshSeconds: 0.5)
                    .InScope(SettingScope.ServerWide);
                page.Button("Show in game", () => window.ShowRange?.Invoke(), label: "See the range around you",
                    help: "Draws the range on the ground around your character for 20 seconds. It also shows for 20 seconds, for everyone online, whenever the range changes: close this page to see a new value.")
                    .InScope(SettingScope.ServerWide);
                page.Radio(server.Pickup, ServerConfig.PickupChoices, ServerConfig.PickupLabels, label: "What is picked up",
                    help: "Any drop in range, or only drops that landed while the player was already in range (walking up to an old pile does not take it).",
                    scope: SettingScope.Server);
                page.Radio(server.Share, ServerConfig.ShareChoices, ServerConfig.ShareLabels, label: "Several players in range",
                    help: "Closest: the nearest player gets the drop. Take turns: players in range get drops one after another.",
                    scope: SettingScope.Server);

                page.Radio(server.DeathBags, ServerConfig.DeathBagChoices, ServerConfig.DeathBagLabels, label: "Death bags",
                    help: "What a player carried when they died. It is taken whole (everything that fits, gear too), filters and caps do not apply, and the owner always comes first. " +
                          "The game's own setting for who may open death bags always applies on top: with it set to only the owner, nobody else ever takes one.",
                    scope: SettingScope.Server);
                page.Radio(server.DroppedBags, ServerConfig.DroppedBagChoices, ServerConfig.DroppedBagLabels, label: "Dropped inventory bags",
                    help: "A whole inventory a player dropped on purpose. Never picked up by default. When on, anyone in range except the player who dropped it takes it whole. " +
                          "The game tells Autoloot who dropped it; a bag it cannot match to its dropper (for example one left from before a restart) is never taken.",
                    scope: SettingScope.Server);

                page.Section("Players without the client part");
                page.Toggle(server.NoClientAutoloot, label: "Autoloot for them",
                    help: "Players who have not installed Autoloot's client part get autoloot too. With Progenitor they still set everything on this page (on or off, their pickup filter, Satisvampory caps) and see these server settings; " +
                          "only pickup notices and the range ring need the client part. Off: nothing is picked up for them.",
                    scope: SettingScope.Server);
                page.Toggle(server.NoClientHint, label: "Tell them on joining",
                    help: "One chat line per login, shortly after they join: what autoloot does here and that installing the Autoloot mod lets them turn it off, set limits and see what they picked up.",
                    scope: SettingScope.Server);

                page.Section("Help");
                page.Info("What it does", () => Words.WhatItDoes, refreshSeconds: 0);
                page.Info("Never picked up", () => Words.Never, refreshSeconds: 0);
                page.Info("Seeing the range", () => "Turn on Show my pickup range, or press See the range around you: the range is drawn on the ground around your character as a red ring. Help at the top of this page has a drawing too.", refreshSeconds: 0);
            });
        }
    }

    /// <summary>Help text, one source for the page and the guide.</summary>
    public static class Words
    {
        public const string WhatItDoes =
            "Drops near you go into your bags: loot from kills, nodes and anything else that lands on the ground. " +
            "Your filter stops an item at the number you choose. With Satisvampory you can also stop at your island's caps and filter whole categories.";

        public const string Never =
            "Items a player dropped from their bags, and soul shards. Gear and jewels are picked up with their stats and durability, like a hand pickup. " +
            "Death bags and dropped inventory bags follow their own server settings and are taken whole, gear included. " +
            "Loot still flying from a kill or a node is picked up once it lands.";
    }
}
