// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Generic;
using Autoloot.Net;

namespace Autoloot.Server
{
    /// <summary>
    /// Pickup notices: what each player picked up is gathered for <see cref="NoticeSeconds"/>, then
    /// sent as one message (the client shows it as a banner). Only for players whose "Pickup
    /// notices" is on; a client without the mod never gets one.
    /// </summary>
    internal sealed partial class Pickup
    {
        const double NoticeSeconds = 3;

        sealed class Batch
        {
            public double Since;
            public readonly List<int> Order = new();
            public readonly Dictionary<int, (int Got, int Total)> Items = new();
            public int DeathBags, DroppedBags, BagItems;
        }

        readonly Dictionary<ulong, Batch> _batches = new();
        readonly List<ulong> _sent = new();
        bool _loggedSend;

        Batch? BatchFor(Player p)
        {
            if (!_players.HasClient(p.Account) || !_settings.NoticesFor(p.Account)) return null;
            if (!_batches.TryGetValue(p.Account, out var b)) _batches[p.Account] = b = new Batch { Since = _now };
            return b;
        }

        /// <param name="total">How many the player now has in their bags (read back after the add).</param>
        void Picked(Player p, int item, int got, int total)
        {
            if (BatchFor(p) is not { } b) return;
            if (!b.Items.TryGetValue(item, out var had)) { b.Order.Add(item); had = (0, 0); }
            b.Items[item] = (had.Got + got, total);
        }

        void BagEmptied(Player p, bool death, int items)
        {
            if (BatchFor(p) is not { } b) return;
            if (death) b.DeathBags++;
            else b.DroppedBags++;
            b.BagItems += items;
        }

        void SendNotices(double now)
        {
            if (_batches.Count == 0) return;
            _sent.Clear();
            foreach (var kv in _batches)
            {
                var b = kv.Value;
                if (now - b.Since < NoticeSeconds) continue;
                _sent.Add(kv.Key);
                if (!_byAccount.TryGetValue(kv.Key, out var p)) continue;
                var msg = new PickedMsg { DeathBags = b.DeathBags, DroppedBags = b.DroppedBags, BagItems = b.BagItems };
                foreach (var item in b.Order)
                {
                    var (got, total) = b.Items[item];
                    msg.Items.Add(item);
                    msg.Amounts.Add(got);
                    msg.Totals.Add(total);
                }
                var sent = _ctx.Messages.SendTo(p.User, msg);
                if (!_loggedSend) { _loggedSend = true; _ctx.Log.LogInfo("Autoloot: first pickup notice sent to " + p.Account + ": " + sent); }
            }
            foreach (var account in _sent) _batches.Remove(account);
        }
    }
}
