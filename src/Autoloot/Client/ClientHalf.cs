// SPDX-License-Identifier: AGPL-3.0-only
using Autoloot.Config;
using Autoloot.Net;
using Progenitor.Dev;

namespace Autoloot.Client
{
    /// <summary>What the settings page needs from the client part.</summary>
    public sealed class ClientHooks
    {
        /// <summary>Draw the pickup range around the player for a while (the settings page's Show button).</summary>
        public System.Action? ShowRange { get; set; }
    }

    /// <summary>
    /// The client part. Tells the server it is there after every join (and once a minute), shows the
    /// pickup notices and draws the range ring. The player's switches and filter are per-player
    /// settings that Progenitor keeps and draws.
    /// </summary>
    internal sealed class ClientHalf
    {
        readonly AutolootContext _ctx;
        readonly PlayerConfig _player;
        readonly ClientHooks _holder;
        bool _stopped;
        bool _sent;

        readonly ServerConfig _server;

        internal ClientHalf(AutolootContext ctx, PlayerConfig player, ServerConfig server, ClientHooks holder)
        {
            _ctx = ctx;
            _player = player;
            _server = server;
            _holder = holder;
        }

        public const string ShowRangeKey = "ShowRange";

        /// <summary>The ring's key (unbound by default). Client only.</summary>
        internal static void RegisterKeys(AutolootContext ctx, PlayerConfig player)
        {
            if (!ctx.IsClient) return;
            ctx.Keybinds.Register(ShowRangeKey, Progenitor.Ui.KeyChord.None, "Show my pickup range (toggle)",
                () => player.ShowRange.Value = !player.ShowRange.Value);
        }

        internal void Attach()
        {
            var ring = new RangeRing(_ctx, _server, _player);
            ring.Attach();
            _holder.ShowRange = ring.Flash;
            _ctx.Track(new OnDispose(() => _holder.ShowRange = null));
            _ctx.Track(_ctx.Messages.On<PickedMsg>((_, m, _) => Notices.Show(_ctx, _player, m)));
            _ctx.Track(new OnDispose(() => _stopped = true));
            KeepHelloSent();
        }

        void SendHello()
        {
            _ctx.Requests.Send<HelloReq, HelloReply>(new HelloReq(), r =>
            {
                if (r.Ok != _sent) _ctx.Log.LogInfo(r.Ok ? "Autoloot: the server knows this client has the client part." : "Autoloot: hello to the server failed: " + r.Status + " " + r.Reason);
                _sent = r.Ok;
            });
        }

        /// <summary>
        /// At load there is often no connection yet: retry every 3 s until the server answers, then
        /// refresh once a minute so a rejoin (or a server restart) gets the switches again.
        /// </summary>
        void KeepHelloSent()
        {
            if (_stopped) return;
            if (_ctx.Handshake.ServerPart == Progenitor.Plugins.CounterpartPresence.Running) SendHello();
            else _sent = false;
            MainThread.PostDelayed(_sent ? 60 : 3, KeepHelloSent, _ctx.Owner);
        }
    }
}
