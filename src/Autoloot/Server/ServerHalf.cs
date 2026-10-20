// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Autoloot.Config;
using Autoloot.Net;
using Progenitor.Networking;
using ProjectM.Network;
using Unity.Entities;

namespace Autoloot.Server
{
    /// <summary>
    /// The server part: runs the pickup loop, notes which players have the client part, and tells
    /// players without it what autoloot does. Each player's switches and filter are kept by
    /// Progenitor (<see cref="PlayerConfig"/>).
    /// </summary>
    internal sealed class ServerHalf
    {
        readonly AutolootContext _ctx;
        readonly ServerConfig _config;
        readonly PlayerConfig _settings;
        readonly PlayerStore _players = new();
        Pickup _pickup = null!;

        internal ServerHalf(AutolootContext ctx, ServerConfig config, PlayerConfig settings)
        {
            _ctx = ctx;
            _config = config;
            _settings = settings;
        }

        internal void Attach()
        {
            var small = new RequestOptions { Burst = 10, PerSecond = 2 };
            _ctx.Track(_ctx.Requests.Handle<HelloReq, HelloReply>((u, q, i) => Hello(Account(u)), small));
            _pickup = new Pickup(_ctx, _config, _players, _settings);
            _pickup.Attach();
            _ctx.Track(_ctx.Events.On<Progenitor.Events.PlayerConnected>(e => OnJoined(e.UserEntity, e.User.PlatformId)));
            _ctx.Track(_ctx.Events.On<Progenitor.Events.PlayerDisconnected>(e => _players.ClientGone(e.User.PlatformId)));
        }

        /// <summary>How long a joining client has to report in before the player counts as having no client part (loading can be slow).</summary>
        const double ClientGraceSeconds = 45;

        /// <summary>
        /// A player with the client part reports in within seconds of joining. One who has not after
        /// <see cref="ClientGraceSeconds"/> gets the one-line hint (when the server sends it).
        /// </summary>
        void OnJoined(Entity userEntity, ulong account)
        {
            // Not cleared here: the client's hello can arrive before this event (leaving clears it).
            Progenitor.Dev.MainThread.PostDelayed(ClientGraceSeconds, () => Hint(userEntity, account), _ctx.Owner);
        }

        void Hint(Entity userEntity, ulong account)
        {
            var rules = _config.Snapshot();
            if (!rules.NoClientHint || _players.HasClient(account)) return;
            try
            {
                var em = _ctx.World!.EntityManager;
                if (!em.Exists(userEntity)) return;
                var user = em.GetComponentData<User>(userEntity);
                if (!user.IsConnected || user.PlatformId != account) return;
            }
            catch (Exception) { return; }
            var text = rules.Enabled && rules.NoClientAutoloot
                ? $"Autoloot is on here: drops within {(int)rules.Radius} m of you go into your bags. With Progenitor you can turn it off on Autoloot's page in the Mods menu; install the Autoloot mod to set a limit per item and see what you picked up."
                : "This server runs Autoloot. Install the Autoloot mod (it needs Progenitor) to have drops near you picked up automatically.";
            _ctx.Chat.SendTo(userEntity, text);
        }

        ulong Account(Entity userEntity)
        {
            var id = _ctx.World!.EntityManager.GetComponentData<User>(userEntity).PlatformId;
            if (id == 0) throw new RequestRefusedException("Your account is not known yet.");
            return id;
        }

        /// <summary>The client part is there: it gets notices, and the no-client rules do not apply to this player.</summary>
        HelloReply Hello(ulong account)
        {
            if (!_players.HasClient(account)) _ctx.Log.LogInfo("Autoloot: player " + account + " has the client part.");
            _players.ClientSeen(account);
            return new HelloReply();
        }
    }
}
