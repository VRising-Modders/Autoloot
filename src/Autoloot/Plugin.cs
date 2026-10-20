// SPDX-License-Identifier: AGPL-3.0-only
using BepInEx;
using Autoloot.Config;
using Progenitor.Plugins;

namespace Autoloot
{
    /// <summary>
    /// Autoloot: drops near a player go into their bags. One DLL on both sides: the server decides
    /// and moves every item; the client draws the settings and the filter window. Works alone, and
    /// uses Satisvampory's caps and categories when Satisvampory runs on the same server.
    /// </summary>
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency("progenitor")]
    [ProgenitorSide(Side.Both)]
    // Either side may run without the other; peers must share a minor version.
    [NetworkCompatibility(NetworkCompatibilityLevel.NoNeedForSync, NetworkVersionRule.Minor)]
    [Counterpart(CounterpartNeed.Limited, Features = new[] { "Your autoloot switch", "Pickup filter" })]
    [ModHelp("help/index.html")]
    public sealed class Plugin : ProgenitorPlugin<AutolootContext>
    {
        protected override void OnLoad(AutolootContext ctx)
        {
            var server = ServerConfig.Bind(ctx);
            var player = PlayerConfig.Bind(ctx);
            var window = new Client.ClientHooks();
            Client.ClientHalf.RegisterKeys(ctx, player);
            SettingsPage.Build(ctx, server, player, window);

            if (ctx.IsServer) new Server.ServerHalf(ctx, server, player).Attach();
            else new Client.ClientHalf(ctx, player, server, window).Attach();
        }
    }

    /// <summary>Empty, as every Progenitor context should be.</summary>
    public sealed class AutolootContext : PluginContext
    {
    }
}
