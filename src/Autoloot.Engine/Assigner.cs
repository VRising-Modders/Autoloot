// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;

namespace Autoloot.Engine
{
    /// <summary>One decision: this player takes this many from this pile.</summary>
    public readonly struct Award
    {
        public Award(Drop drop, ulong account, int take, bool shared)
        {
            Drop = drop;
            Account = account;
            Take = take;
            Shared = shared;
        }

        public Drop Drop { get; }
        public ulong Account { get; }
        public int Take { get; }

        /// <summary>Someone else could have taken it too: once it really moves, it uses the player's turn (<see cref="ShareLedger.Got"/>).</summary>
        public bool Shared { get; }
    }

    /// <summary>When each player last got a drop that someone else could also have taken (for <see cref="ShareRule.Even"/>).</summary>
    public sealed class ShareLedger
    {
        readonly Dictionary<ulong, long> _last = new();
        long _turn;

        /// <summary>The latest turn handed out.</summary>
        public long Turn => _turn;

        /// <summary>Lower is longer ago; a player who never got a shared drop is 0.</summary>
        public long LastTurn(ulong account) => _last.TryGetValue(account, out var t) ? t : 0;

        /// <summary>A shared drop really reached the player's bag.</summary>
        public void Got(ulong account) => _last[account] = ++_turn;

        public void Forget(ulong account) => _last.Remove(account);
    }

    /// <summary>
    /// Decides, for one scan, who takes which pile. Every pile is decided before anything moves, so
    /// the order players are listed in never matters. Keep one and reuse it: its working lists are
    /// kept between scans.
    /// </summary>
    public sealed class Assigner
    {
        readonly List<Award> _awards = new();
        readonly Dictionary<(ulong, int), int> _promised = new();
        readonly Dictionary<(ulong, int), int> _room = new();
        // Turns handed out this scan, before they are real (see Award.Shared).
        readonly Dictionary<ulong, long> _turns = new();
        readonly List<Drop> _ordered = new();
        readonly List<(Looter L, float D)> _candidates = new();
        static readonly Comparison<Drop> ByKey = (a, b) => a.Key.CompareTo(b.Key);

        /// <summary>One-off use (tests): a fresh assigner.</summary>
        public static List<Award> Assign(IReadOnlyList<Drop> drops, IReadOnlyList<Looter> looters, ServerRules rules,
            DropMemory memory, ShareLedger ledger, Func<ulong, int, int> room, int maxAwards = int.MaxValue) =>
            new List<Award>(new Assigner().Run(drops, looters, rules, memory, ledger, room, maxAwards));

        /// <param name="room">How many more of an item a player may take right now (see <see cref="Limits.Room"/>).</param>
        /// <param name="maxAwards">Stop after this many: the rest wait for the next scan.</param>
        /// <returns>This assigner's own list, valid until the next call.</returns>
        public IReadOnlyList<Award> Run(IReadOnlyList<Drop> drops, IReadOnlyList<Looter> looters, ServerRules rules,
            DropMemory memory, ShareLedger ledger, Func<ulong, int, int> room, int maxAwards = int.MaxValue)
        {
            _awards.Clear();
            if (!rules.Enabled || drops.Count == 0 || looters.Count == 0) return _awards;
            _promised.Clear();
            _room.Clear();
            _turns.Clear();
            var nextTurn = ledger.Turn;

            _ordered.Clear();
            for (var i = 0; i < drops.Count; i++) _ordered.Add(drops[i]);
            _ordered.Sort(ByKey);
            foreach (var d in _ordered)
            {
                if (_awards.Count >= maxAwards) break;
                if (d.PlayerDropped || d.Asleep || d.Amount <= 0 || memory.IsNew(d.Key)) continue;
                _candidates.Clear();
                foreach (var l in looters)
                {
                    if (!l.On) continue;
                    var dist = l.DistanceTo(d);
                    if (dist > rules.Radius) continue;
                    if (rules.Pickup == PickupRule.LandsInRange && !memory.Saw(d.Key, l.Account)) continue;
                    if (Left(l.Account, d.Item, room) <= 0) continue;
                    _candidates.Add((l, dist));
                }
                if (_candidates.Count == 0) continue;

                var best = _candidates[0];
                for (var i = 1; i < _candidates.Count; i++)
                    if (Better(_candidates[i], best, rules.Share, ledger)) best = _candidates[i];

                var take = Math.Min(d.Amount, Left(best.L.Account, d.Item, room));
                var key = (best.L.Account, d.Item);
                _promised[key] = (_promised.TryGetValue(key, out var p) ? p : 0) + take;
                var shared = _candidates.Count > 1;
                if (shared) _turns[best.L.Account] = ++nextTurn;
                _awards.Add(new Award(d, best.L.Account, take, shared));
            }
            return _awards;
        }

        int Left(ulong account, int item, Func<ulong, int, int> room)
        {
            var key = (account, item);
            if (!_room.TryGetValue(key, out var r)) _room[key] = r = Math.Max(0, room(account, item));
            return r - (_promised.TryGetValue(key, out var p) ? p : 0);
        }

        long TurnOf(ulong account, ShareLedger ledger) => _turns.TryGetValue(account, out var t) ? t : ledger.LastTurn(account);

        bool Better((Looter L, float D) a, (Looter L, float D) b, ShareRule share, ShareLedger ledger)
        {
            if (share == ShareRule.Even)
            {
                var ta = TurnOf(a.L.Account, ledger);
                var tb = TurnOf(b.L.Account, ledger);
                if (ta != tb) return ta < tb;
            }
            if (a.D != b.D) return a.D < b.D;
            return a.L.Account < b.L.Account;
        }
    }
}
