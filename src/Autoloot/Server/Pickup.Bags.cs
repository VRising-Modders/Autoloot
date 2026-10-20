// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using Autoloot.Engine;
using Progenitor.Ecs;
using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Entities;
using Unity.Transforms;

namespace Autoloot.Server
{
    /// <summary>
    /// Bags on the ground: death bags (what a player carried when they died) and dropped bags (a
    /// whole inventory dropped on purpose). Each is emptied into one player's bags with the game's
    /// own slot move, so gear keeps its stats and nothing is duplicated or lost; the game removes a
    /// bag once it is empty. Who may take one is decided by <see cref="DeathBags"/>, and a death bag
    /// never goes beyond what the game itself lets that player open.
    /// </summary>
    /// <remarks>
    /// A bag's kind, owner, access and place are read once, when it first appears; afterwards only
    /// bags near someone with autoloot on are looked at. The bag prefabs and a dropped bag's dropper
    /// come from Progenitor (<c>ctx.Drops.TryPrefabs</c>, <c>ctx.Inventory.OnDropAll</c>). Not live-verified:
    /// which component a dropped-inventory bag carries (assumed <c>DestroyWhenInventoryIsEmpty</c>), and that
    /// the game removes an emptied bag.
    /// </remarks>
    internal sealed partial class Pickup
    {
        const int BagsPerTick = 4;
        const double BagRetrySeconds = 5;

        enum BagKind { Other, Death, Dropped }

        sealed class GroundBag
        {
            public Entity Entity;
            public BagKind Kind;
            public ulong Owner;
            public Entity OwnerUser;
            public GameBagAccess Access;
            public List<ulong> Droppers = new();
            public float X, Y, Z;
            public double RetryAt;
            public int Seen;
        }

        readonly KeptQuery _deathQuery = new(ComponentIndex.Of<PlayerDeathContainer>(ComponentType.AccessMode.ReadOnly));
        readonly KeptQuery _emptyingQuery = new(ComponentIndex.Of<DestroyWhenInventoryIsEmpty>(ComponentType.AccessMode.ReadOnly));
        readonly Dictionary<long, GroundBag> _groundBags = new();
        readonly List<long> _goneBags = new();
        readonly Func<ulong, bool> _canLootAccount;
        GroundBag? _clanBag;
        readonly Func<ulong, bool> _sameClan;
        int _guidAnyone, _guidClan, _guidSelf, _guidDropped;
        bool _haveGuids;
        double _guidsRetryAt;
        int _deathBags, _droppedBags, _bagsEmptied;
        // Whole-inventory drops Progenitor matched to their container, by container key (kept a while).
        readonly Dictionary<long, (ulong Account, double At)> _droppedBy = new();
        readonly List<long> _oldDrops = new();

        void ForgetBags()
        {
            _groundBags.Clear();
            _droppedBy.Clear();
            _deathBags = _droppedBags = 0;
        }

        void ForgetBagPrefabs()
        {
            _haveGuids = false;
            _guidsRetryAt = 0;
        }

        void ReleaseBagQueries()
        {
            _deathQuery.Release(_ctx.Log.LogWarning);
            _emptyingQuery.Release(_ctx.Log.LogWarning);
        }

        bool CanLootAccount(ulong account) => _byAccount.TryGetValue(account, out var p) && CanLoot(p);

        bool SameClanAsBagOwner(ulong account)
        {
            var bag = _clanBag;
            if (bag == null || !_byAccount.TryGetValue(account, out var p)) return false;
            var mine = _ctx.Clans.Of(p.User);
            if (mine.IsNone) return false;
            var theirs = _ctx.Clans.Of(bag.OwnerUser);
            // The clan entity, not its id: an unreadable id falls back to empty for every clan.
            return !theirs.IsNone && theirs.Entity.Equals(mine.Entity);
        }

        /// <summary>Every tick while a bag setting is on, even with nobody looting: new bags are classed when they appear.</summary>
        void ObserveBags(EntityManager em, double now)
        {
            var world = _ctx.World!;
            if (!_haveGuids && now >= _guidsRetryAt) ReadGuids(em, now);
            var array = _deathQuery.Entities(world);
            try { for (var i = 0; i < array.Length; i++) See(em, array[i], BagKind.Death); }
            finally { array.Dispose(); }
            array = _emptyingQuery.Entities(world);
            try { for (var i = 0; i < array.Length; i++) See(em, array[i], BagKind.Dropped); }
            finally { array.Dispose(); }

            _deathBags = _droppedBags = 0;
            _goneBags.Clear();
            foreach (var kv in _groundBags)
            {
                if (kv.Value.Seen != _scan) { _goneBags.Add(kv.Key); continue; }
                if (kv.Value.Kind == BagKind.Death) _deathBags++;
                else if (kv.Value.Kind == BagKind.Dropped) _droppedBags++;
            }
            foreach (var key in _goneBags) _groundBags.Remove(key);
            if (_droppedBy.Count > 0)
            {
                _oldDrops.Clear();
                foreach (var kv in _droppedBy) if (now - kv.Value.At > 300 && !_groundBags.ContainsKey(kv.Key)) _oldDrops.Add(kv.Key);
                foreach (var key in _oldDrops) _droppedBy.Remove(key);
            }
        }

        void See(EntityManager em, Entity e, BagKind query)
        {
            var key = ((long)e.Index << 32) | (uint)e.Version;
            if (_groundBags.TryGetValue(key, out var bag)) { bag.Seen = _scan; return; }
            // Until the prefab table is read a bag cannot be classed (dropped or not, who may open a death bag): next tick.
            if (!_haveGuids) return;
            // Not filled in yet (just spawned): next tick, rather than classing it for good.
            if (!TryPosition(em, e, out var at)) return;
            bag = new GroundBag { Entity = e, Seen = _scan };
            _groundBags[key] = bag;
            bag.X = at.x;
            bag.Y = at.y;
            bag.Z = at.z;
            var prefab = e.TryRead<PrefabGUID>(em, out var guid) ? guid.GuidHash : 0;

            if (e.TryRead<PlayerDeathContainer>(em, out var death))
            {
                // The death query and the emptying query can both list a death bag: it is one bag.
                bag.OwnerUser = death.DeadUserEntity;
                try { bag.Owner = em.Exists(death.DeadUserEntity) ? em.GetComponentData<User>(death.DeadUserEntity).PlatformId : 0; }
                catch (Exception) { bag.Owner = 0; }
                if (bag.Owner == 0)
                {
                    // Owner not readable yet: try again next tick (a deleted owner stays unreadable and the bag is never touched).
                    _groundBags.Remove(key);
                    return;
                }
                bag.Kind = BagKind.Death;
                // Unknown prefab: the safest of the game's rules.
                bag.Access = prefab == _guidAnyone && prefab != 0 ? GameBagAccess.Anyone
                    : prefab == _guidClan && prefab != 0 ? GameBagAccess.ClanMembers
                    : GameBagAccess.OnlySelf;
                return;
            }
            if (query == BagKind.Dropped && prefab != 0 && prefab == _guidDropped)
            {
                bag.Kind = BagKind.Dropped;
                // The dropper from the game's own drop event; unknown means nobody ever takes it.
                if (_droppedBy.TryGetValue(key, out var by)) bag.Droppers = new List<ulong> { by.Account };
            }
        }

        /// <summary>The bag prefabs from the game's prefab table, through Progenitor.</summary>
        void ReadGuids(EntityManager em, double now)
        {
            _guidsRetryAt = now + 10;
            if (!_ctx.Drops.TryPrefabs(out var prefabs, out var why))
            {
                if (now > 60) _ctx.Log.LogInfo("Autoloot: bag prefabs not readable yet: " + why);
                return;
            }
            _guidAnyone = prefabs.PlayerDeathContainerAnyone;
            _guidClan = prefabs.PlayerDeathContainerClanMembers;
            _guidSelf = prefabs.PlayerDeathContainerOnlySelf;
            _guidDropped = prefabs.PlayerInventoryDropContainer;
            _haveGuids = _guidDropped != 0 && _guidSelf != 0;
        }

        /// <summary>
        /// A whole inventory dropped: Progenitor names the container it went into and who dropped it.
        /// Only a clear match counts; a bag whose dropper is not known this way is never handed to anyone.
        /// </summary>
        void OnDropAll(Progenitor.Drops.PlayerDropAll d)
        {
            if (!d.HasContainer || d.Ambiguous || d.Epoch != _ctx.WorldState.LoadEpoch) return;
            ulong account;
            try { account = _ctx.World!.EntityManager.GetComponentData<User>(d.User).PlatformId; }
            catch (Exception) { return; }
            if (account == 0) return;
            var key = ((long)d.ContainerIndex << 32) | (uint)d.ContainerVersion;
            _droppedBy[key] = (account, Now);
            if (_groundBags.TryGetValue(key, out var bag) && bag.Kind == BagKind.Dropped) bag.Droppers = new List<ulong> { account };
        }

        /// <summary>Empty up to <see cref="BagsPerTick"/> bags, within what is left of the move budget.</summary>
        void Bags(EntityManager em, ServerRules rules, double now)
        {
            if (rules.DeathBags == DeathBagRule.Off && rules.DroppedBags == DroppedBagRule.Off) return;
            var reach2 = rules.Radius * rules.Radius;
            var done = 0;
            foreach (var bag in _groundBags.Values)
            {
                if (done >= BagsPerTick || _phase.Elapsed.TotalMilliseconds > MoveBudgetMs) break;
                if (bag.Kind == BagKind.Other || now < bag.RetryAt || !AnyOnWithin(bag.X, bag.Y, bag.Z, reach2)) continue;
                ulong? taker;
                if (bag.Kind == BagKind.Death)
                {
                    _clanBag = bag;
                    taker = DeathBags.Taker(bag.Owner, bag.X, bag.Y, bag.Z, _looters, rules, bag.Access, _canLootAccount, _sameClan);
                    _clanBag = null;
                }
                else taker = DeathBags.DroppedTaker(bag.Droppers, bag.X, bag.Y, bag.Z, _looters, rules, _canLootAccount);
                if (taker is not { } account || !_byAccount.TryGetValue(account, out var player)) continue;
                if (!em.Exists(bag.Entity) || bag.Entity.Has<DestroyTag>(em)) continue;

                done++;
                var result = _ctx.Inventory.TransferAll(_ctx.Inventory.Of(bag.Entity), _ctx.Inventory.Of(player.Character));
                // Emptied only when the whole move went through; anything else (full bags, a move the game
                // refused part-way) waits before the next try.
                if (result.Ok && result.Moved > 0 && result.Left <= 0) _bagsEmptied++;
                else bag.RetryAt = now + BagRetrySeconds;
                if (result.Moved > 0) BagEmptied(player, bag.Kind == BagKind.Death, result.Moved);
            }
        }

        bool AnyOnWithin(float x, float y, float z, float reach2)
        {
            foreach (var l in _on)
            {
                float dx = x - l.X, dy = y - l.Y, dz = z - l.Z;
                if (dx * dx + dy * dy + dz * dz <= reach2) return true;
            }
            return false;
        }
    }
}
