// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autoloot.Config;
using Autoloot.Engine;
using Progenitor.Dev;
using Progenitor.Ecs;
using Progenitor.Events;
using Progenitor.Inventory;
using Progenitor.Players;
using ProjectM;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace Autoloot.Server
{
    /// <summary>
    /// The pickup loop. Twice a second: see which piles lie on the ground and where the online
    /// players are, decide who takes what (<see cref="Assigner"/>), then move the items. Everything is taken
    /// except soul shards. A pile on the ground is itself a small inventory holding the real item:
    /// gear, jewels and other single items are moved out of it with the game's own slot move, so their
    /// rolled stats and durability come along; stackables use Progenitor's take.
    /// </summary>
    /// <remarks>
    /// Built for a busy server where everyone runs it. A pile's item and place are read once, when it
    /// first appears; after that only piles near a player with autoloot on are read again. The player
    /// list is rebuilt on joins and leaves (and every <see cref="RosterSeconds"/>), not every tick; a
    /// bag is read only for a player who could take something, once per tick. Pickups per tick have a
    /// budget, so a big burst of loot spreads over a few ticks. The log carries a summary once a
    /// minute and warns about slow ticks.
    /// </remarks>
    internal sealed partial class Pickup
    {
        const double TickSeconds = 0.5;
        const double RosterSeconds = 10;
        /// <summary>Piles this far beyond a player's range are read too, so one walking in finds them current.</summary>
        const float NearMargin = 2;
        const int MovesPerTick = 40;
        const double MoveBudgetMs = 4;
        const double SlowTickMs = 8;
        const double ReportSeconds = 60;
        /// <summary>A pile this young may still be settling: its place is read again each tick.</summary>
        const double SettleSeconds = 2;
        /// <summary>A pile whose item or place could not be read yet is tried again after this.</summary>
        const double RetrySeconds = 2;
        /// <summary>Rebuilding the player list scans every account: at most this often, even when asked.</summary>
        const double RosterMinSeconds = 2;

        sealed class Pile
        {
            public Drop Drop = null!;
            public Entity Entity;
            /// <summary>Not for autoloot (a soul shard, unreadable), or emptied by it and waiting to be removed.</summary>
            public bool Ignore;
            /// <summary>A single item with its own state (gear, a jewel, a book): moved out of the pile's inventory whole.</summary>
            public bool Unique;
            public int Seen;
            public double Born;
        }

        sealed class Player
        {
            public ulong Account;
            public Entity User;
            public Entity Character;
            public readonly Looter Looter = new();
            /// <summary>The character is in the world this scan.</summary>
            public bool Present;
            /// <summary>Bag counts, read at most once per scan (<see cref="CountScan"/>).</summary>
            public readonly Dictionary<int, int> Counts = new();
            public int CountScan = -1;
            public int StateScan = -1;
            public bool CanLoot;
        }

        readonly AutolootContext _ctx;
        readonly ServerConfig _config;
        readonly PlayerStore _players;
        readonly PlayerConfig _settings;
        readonly Satis _satis = new();
        readonly DropMemory _memory = new();
        readonly ShareLedger _ledger = new();
        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly Dictionary<int, ItemKind> _kinds = new();

        readonly Dictionary<long, Pile> _piles = new();
        readonly List<long> _gonePiles = new();
        readonly List<Drop> _all = new();
        readonly List<Drop> _near = new();
        readonly List<Player> _roster = new();
        readonly Dictionary<ulong, Player> _byAccount = new();
        readonly List<Looter> _looters = new();
        readonly List<Looter> _on = new();
        readonly Dictionary<long, double> _retry = new();
        readonly List<long> _goneRetry = new();
        readonly Assigner _assigner = new();
        readonly Func<ulong, int, int> _room;
        readonly Func<string, int, bool> _inCategory;
        ulong _roomAccount;
        double _now;
        InventorySlot[] _slots = new InventorySlot[64];

        readonly KeptQuery _pileQuery = new(ComponentIndex.Of<ItemPickup>(ComponentType.AccessMode.ReadOnly));
        int _epoch;
        int _scan;
        bool _rosterDirty = true;
        double _rosterAt = double.NegativeInfinity;
        bool _stopped;
        bool _failedOnce;

        // Player-dropped piles kept in the world save, so a reload of this mod (same world) still knows them.
        const string PilesKey = "piles";
        const double PilesSaveSeconds = 5;
        Progenitor.Persistence.IModStore? _store;
        int _savedRevision = -1;
        double _pilesSavedAt;

        /// <summary>What is kept: the world load it belongs to and the player-dropped pile keys.</summary>
        public sealed class SavedPiles
        {
            public int Epoch { get; set; }
            public List<long> Keys { get; set; } = new();
        }

        // Tick timer, reported once a minute.
        readonly Stopwatch _tick = new();
        readonly Stopwatch _phase = new();
        int _ticks, _moves;
        double _sumMs, _maxMs, _maxPiles, _maxBags, _maxAssign, _maxMoves, _maxBagMoves;
        int _maxNear;
        // Inside "assign", per tick: the filter (with Satisvampory categories), bag reads, Satisvampory caps.
        double _filterMs, _bagReadMs, _capsMs, _maxFilter, _maxBagRead, _maxCaps;
        double _nextReport;
        double _nextSlowWarning;

        internal Pickup(AutolootContext ctx, ServerConfig config, PlayerStore players, PlayerConfig settings)
        {
            _ctx = ctx;
            _config = config;
            _players = players;
            _settings = settings;
            // Made once: no closures per tick or per pile.
            _room = (account, item) => Room(account, item);
            _inCategory = (category, item) => _satis.InCategory(_roomAccount, category, item, _now);
            _canLootAccount = CanLootAccount;
            _sameClan = SameClanAsBagOwner;
        }

        double Now => _clock.Elapsed.TotalSeconds;

        internal void Attach()
        {
            _epoch = _ctx.WorldState.LoadEpoch;
            _nextReport = Now + ReportSeconds;
            _ctx.Track(new OnDispose(() => _stopped = true));
            _ctx.Track(new OnDispose(ReleaseQueries));
            _ctx.Track(_ctx.Save.WhenReady(OnStoreReady));
            _ctx.Track(new OnDispose(() => SavePiles(force: true)));
            // A changed filter: cached category members and storage counts for that player are stale.
            _settings.FilterChanged += Forget;
            _ctx.Track(new OnDispose(() => _settings.FilterChanged -= Forget));
            try { _ctx.Track(_ctx.Inventory.OnPlayerMove(OnMove)); }
            catch (Exception e) { _ctx.Log.LogWarning("Autoloot: cannot watch chest-window drops (" + e.Message + ")."); }
            // A player's own bag to the ground, and whole-inventory drops: the game's own events.
            try { _ctx.Track(_ctx.Inventory.OnGroundDrop(OnGroundDrop)); }
            catch (Exception e) { _ctx.Log.LogWarning("Autoloot: cannot watch bag drops (" + e.Message + "); a pile a player drops may be picked up again."); }
            try { _ctx.Track(_ctx.Inventory.OnDropAll(OnDropAll)); }
            catch (Exception e) { _ctx.Log.LogWarning("Autoloot: cannot watch whole-inventory drops (" + e.Message + "); dropped bags are left alone."); }
            _ctx.Track(_ctx.Events.On<PlayerConnected>(_ => OnJoinOrLeave()));
            _ctx.Track(_ctx.Events.On<PlayerDisconnected>(_ => OnJoinOrLeave()));
            MainThread.PostDelayed(TickSeconds, Tick, _ctx.Owner);
        }

        /// <summary>The player's filter changed: drop cached category members and storage counts.</summary>
        internal void Forget(ulong account) => _satis.Forget(account);

        void OnJoinOrLeave()
        {
            _rosterDirty = true;
            // A joining player's character can arrive a moment after the connect.
            MainThread.PostDelayed(3, () => _rosterDirty = true, _ctx.Owner);
        }

        void OnMove(PlayerInventoryMove m)
        {
            if (m.Kind != PlayerInventoryMoveKind.Drop || m.Direction != PlayerInventoryMoveDirection.Out) return;
            var em = _ctx.World!.EntityManager;
            if (!m.Character.TryRead<Translation>(em, out var t)) return;
            _memory.MarkPlayerDrop(m.Item, t.Value.x, t.Value.y, t.Value.z, Now);
        }

        void Tick()
        {
            if (_stopped) return;
            _tick.Restart();
            try { Scan(); }
            catch (Exception e)
            {
                // Keep running; say why once, not twice a second.
                if (!_failedOnce) _ctx.Log.LogWarning("Autoloot: pickup tick failed: " + e);
                _failedOnce = true;
            }
            _tick.Stop();
            Measure(_tick.Elapsed.TotalMilliseconds);
            MainThread.PostDelayed(TickSeconds, Tick, _ctx.Owner);
        }

        /// <summary>
        /// The same world as the saved list (a reload of this mod): those piles stay a player's drop.
        /// Anything else (a restart, another world): every pile already lying there is left alone.
        /// </summary>
        void OnStoreReady(Progenitor.Persistence.IModStore store)
        {
            var saved = store.Get<SavedPiles>(PilesKey);
            if (saved != null && saved.Epoch == _ctx.WorldState.LoadEpoch && saved.Keys != null)
            {
                _memory.Restore(saved.Keys);
                _ctx.Log.LogInfo("Autoloot: kept " + saved.Keys.Count + " player-dropped pile(s) from before the reload.");
            }
            else _memory.OldPilesAreHandsOff();
            _store = store;
        }

        void SavePiles(bool force)
        {
            if (_store == null || _memory.Revision == _savedRevision) return;
            if (!force && Now - _pilesSavedAt < PilesSaveSeconds) return;
            try
            {
                _store.Set(PilesKey, new SavedPiles { Epoch = _ctx.WorldState.LoadEpoch, Keys = _memory.PlayerDroppedKeys() });
                _savedRevision = _memory.Revision;
                _pilesSavedAt = Now;
            }
            catch (Exception e) { _ctx.Log.LogWarning("Autoloot: player-dropped piles not saved: " + e.Message); }
        }

        void Scan()
        {
            var world = _ctx.World;
            if (world == null || !world.IsCreated) return;
            // Until the save is open nothing is judged: whether old piles are hands-off depends on it.
            if (_store == null) return;
            if (_ctx.WorldState.IsStale(_epoch))
            {
                _epoch = _ctx.WorldState.LoadEpoch;
                ReleaseQueries();
                ForgetBags();
                ForgetBagPrefabs();
                _memory.Reset();
                _satis.Clear();
                _kinds.Clear();
                _piles.Clear();
                _retry.Clear();
                _roster.Clear();
                _byAccount.Clear();
                _batches.Clear();
                _droppedBy.Clear();
                _rosterDirty = true;
            }
            _scan++;
            var rules = _config.Snapshot();
            var em = world.EntityManager;
            var now = Now;

            _now = now;

            _phase.Restart();
            Players(em, rules, now);
            var bags = _phase.Elapsed.TotalMilliseconds;

            _phase.Restart();
            // Bags only while one of their settings is on.
            if (rules.Enabled && (rules.DeathBags != DeathBagRule.Off || rules.DroppedBags != DroppedBagRule.Off)) ObserveBags(em, now);
            else ForgetBags();
            AllPiles(em, now);
            _memory.Observe(_all, _looters, rules.Radius, now);
            var piles = _phase.Elapsed.TotalMilliseconds;
            if (!rules.Enabled || _on.Count == 0)
            {
                Phases(piles, bags, 0, 0, 0);
                SendNotices(now);
                SavePiles(force: false);
                return;
            }

            _phase.Restart();
            _filterMs = _bagReadMs = _capsMs = 0;
            NearPiles(em, rules);
            var awards = _assigner.Run(_near, _looters, rules, _memory, _ledger, _room, MovesPerTick);
            var assign = _phase.Elapsed.TotalMilliseconds;

            _phase.Restart();
            foreach (var a in awards)
            {
                Move(em, a);
                _moves++;
                if (_phase.Elapsed.TotalMilliseconds > MoveBudgetMs) break; // the rest next tick
            }
            // Death and dropped bags get what is left of the move budget (their own figure in the timer).
            var moves = _phase.Elapsed.TotalMilliseconds;
            Bags(em, rules, now);
            _maxBagMoves = Math.Max(_maxBagMoves, _phase.Elapsed.TotalMilliseconds - moves);
            Phases(piles, bags, assign, moves, _near.Count);
            SavePiles(force: false);
            _maxFilter = Math.Max(_maxFilter, _filterMs);
            _maxBagRead = Math.Max(_maxBagRead, _bagReadMs);
            _maxCaps = Math.Max(_maxCaps, _capsMs);
            SendNotices(now);
        }

        // ---------------- players ----------------

        /// <summary>
        /// Positions every tick from the characters already known (three reads each); the list
        /// itself comes from Progenitor's player directory, which scans every account the server has
        /// seen, so that runs only on a join or a leave and every <see cref="RosterSeconds"/>.
        /// </summary>
        void Players(EntityManager em, ServerRules rules, double now)
        {
            if ((_rosterDirty && now - _rosterAt >= RosterMinSeconds) || now - _rosterAt >= RosterSeconds) Roster(now);
            _looters.Clear();
            _on.Clear();
            foreach (var p in _roster)
            {
                p.Present = p.Character.TryRead<Translation>(em, out var t);
                if (!p.Present)
                {
                    _rosterDirty = true; // dead, respawning or gone: the list is rebuilt (at most every RosterMinSeconds)
                    continue;
                }
                var l = p.Looter;
                l.Account = p.Account;
                l.X = t.Value.x;
                l.Y = t.Value.y;
                l.Z = t.Value.z;
                // Without the client part: the basics, if the server allows them (their stored settings still apply).
                l.On = rules.Enabled && _settings.OnFor(p.Account) && (rules.NoClientAutoloot || _players.HasClient(p.Account));
                _looters.Add(l);
                if (l.On) _on.Add(l);
            }
        }

        void Roster(double now)
        {
            _rosterDirty = false;
            _rosterAt = now;
            var online = new HashSet<ulong>();
            var withCharacter = new HashSet<ulong>();
            foreach (var info in _ctx.Players.Online())
            {
                // Online without a character (on the death screen): kept, so turns and caches survive the respawn.
                online.Add(info.PlatformId);
                if (!_byAccount.TryGetValue(info.PlatformId, out var p))
                    _byAccount[info.PlatformId] = p = new Player { Account = info.PlatformId };
                var s = info.Session;
                p.User = new Entity { Index = s.UserIndex, Version = s.UserVersion };
                if (!s.HasCharacter) continue;
                withCharacter.Add(info.PlatformId);
                var character = new Entity { Index = s.CharacterIndex, Version = s.CharacterVersion };
                if (character != p.Character) p.CountScan = -1;
                p.Character = character;
            }
            _roster.Clear();
            var left = new List<ulong>();
            foreach (var kv in _byAccount)
            {
                if (withCharacter.Contains(kv.Key)) _roster.Add(kv.Value);
                else if (!online.Contains(kv.Key)) left.Add(kv.Key);
            }
            foreach (var account in left)
            {
                _byAccount.Remove(account);
                _satis.Forget(account);
                _ledger.Forget(account);
            }
        }

        /// <summary>Dead or downed players pick nothing up. Read only for players who could take something, once per scan.</summary>
        bool CanLoot(Player p)
        {
            if (p.StateScan == _scan) return p.CanLoot;
            p.StateScan = _scan;
            var st = _ctx.Players.State(p.User);
            p.CanLoot = !(st.Status == PlayerStateStatus.Ok && (st.Dead || st.Downed));
            return p.CanLoot;
        }

        // ---------------- piles ----------------

        /// <summary>
        /// Every pile in the world, sleeping areas included (forgetting a sleeping pile would forget
        /// that a player dropped it). Only a pile seen for the first time is read; the rest are
        /// known by their id.
        /// </summary>
        void AllPiles(EntityManager em, double now)
        {
            _all.Clear();
            var hits = 0;
            var array = _pileQuery.Entities(_ctx.World!);
            try
            {
                for (var i = 0; i < array.Length; i++)
                {
                    var e = array[i];
                    var key = ((long)e.Index << 32) | (uint)e.Version;
                    if (!_piles.TryGetValue(key, out var pile))
                    {
                        if (_retry.TryGetValue(key, out var at) && now < at) continue;
                        pile = Read(em, e, key, now, out var retry);
                        if (retry) { _retry[key] = now + RetrySeconds; continue; } // not readable yet: again shortly
                        _retry.Remove(key);
                        _piles[key] = pile;
                    }
                    else if (!pile.Ignore && now - pile.Born < SettleSeconds && TryPosition(em, e, out var settled))
                    {
                        // Loot can still be flying or rolling just after it appears.
                        pile.Drop.X = settled.x;
                        pile.Drop.Y = settled.y;
                        pile.Drop.Z = settled.z;
                    }
                    hits++;
                    pile.Seen = _scan;
                    if (!pile.Ignore) _all.Add(pile.Drop);
                }
            }
            finally { array.Dispose(); }

            if (_piles.Count != hits)
            {
                _gonePiles.Clear();
                foreach (var kv in _piles) if (kv.Value.Seen != _scan) _gonePiles.Add(kv.Key);
                foreach (var key in _gonePiles) _piles.Remove(key);
            }
            if (_retry.Count > 64)
            {
                // Retry keys whose pile is gone: the backoff time is long past.
                _goneRetry.Clear();
                foreach (var kv in _retry) if (now - kv.Value > 30) _goneRetry.Add(kv.Key);
                foreach (var key in _goneRetry) _retry.Remove(key);
            }
        }

        Pile Read(EntityManager em, Entity e, long key, double now, out bool retry)
        {
            retry = false;
            var pile = new Pile { Entity = e, Drop = new Drop { Key = key }, Born = now };
            if (!e.TryRead<ItemPickup>(em, out var item) || item.ItemAmount <= 0 || !TryPosition(em, e, out var pos))
            {
                // Possibly caught before the game filled it in: look again shortly.
                retry = true;
                return pile;
            }
            switch (Kind(item.ItemId.GuidHash))
            {
                case ItemKind.Unknown:
                    // An unknown item may become known once the catalog fills: look again later.
                    retry = true;
                    return pile;
                case ItemKind.Never:
                    pile.Ignore = true;
                    return pile;
                case ItemKind.Single:
                    pile.Unique = true;
                    break;
            }
            pile.Drop.Item = item.ItemId.GuidHash;
            pile.Drop.Amount = item.ItemAmount;
            pile.Drop.X = pos.x;
            pile.Drop.Y = pos.y;
            pile.Drop.Z = pos.z;
            return pile;
        }

        /// <summary>The piles someone with autoloot on could reach, read fresh: amount, still there, asleep.</summary>
        void NearPiles(EntityManager em, ServerRules rules)
        {
            _near.Clear();
            var reach = rules.Radius + NearMargin;
            var reach2 = reach * reach;
            foreach (var d in _all)
            {
                if (d.PlayerDropped || !Within(d, reach2)) continue;
                var pile = _piles[d.Key];
                if (!_ctx.Drops.TryGet(Progenitor.Drops.GroundPileId.For(pile.Entity), out var now, out _)) continue;
                // Gear with its own stats, or anything that does not stack: moved out of the pile whole.
                if (now.HasItemEntity || (now.MaxStack > 0 && now.MaxStack <= 1)) pile.Unique = true;
                else if (now.MaxStack == 0) continue; // stack size not known yet: next tick
                // Still flying from the kill or the node: next tick.
                if (now.InFlight) continue;
                d.Amount = now.Amount;
                d.Asleep = now.Disabled;
                d.X = now.X;
                d.Y = now.Y;
                d.Z = now.Z;
                _near.Add(d);
            }
        }

        bool Within(Drop d, float reach2)
        {
            foreach (var l in _on)
            {
                float dx = d.X - l.X, dy = d.Y - l.Y, dz = d.Z - l.Z;
                if (dx * dx + dy * dy + dz * dz <= reach2) return true;
            }
            return false;
        }

        static bool TryPosition(EntityManager em, Entity e, out Unity.Mathematics.float3 pos)
        {
            pos = default;
            if (e.TryRead<Translation>(em, out var t)) { pos = t.Value; return true; }
            if (e.TryRead<LocalToWorld>(em, out var l)) { pos = l.Position; return true; }
            return false;
        }

        enum ItemKind { Unknown, Stack, Single, Never }

        /// <summary>
        /// Soul shards are never taken (there is one of each on a server, and it belongs to whoever
        /// carries it). Anything that does not stack is moved whole; the rest stacks.
        /// </summary>
        ItemKind Kind(int item)
        {
            if (_kinds.TryGetValue(item, out var known)) return known;
            // Not cached while the catalog does not know the item yet (it can be empty early on).
            if (_ctx.Items.CatalogEntry(item) is not { } entry) return ItemKind.Unknown;
            var kind = (entry.PrefabName ?? "").StartsWith("Item_MagicSource_SoulShard", StringComparison.Ordinal) ? ItemKind.Never
                : entry.MaxAmount > 1 ? ItemKind.Stack : ItemKind.Single;
            return _kinds[item] = kind;
        }

        // ---------------- own drops and bag counts ----------------

        /// <summary>
        /// A stack a player dropped from their own bag (Progenitor's ground-drop event, with where it
        /// landed): a new pile of that item there is theirs and is never picked up (<see cref="DropMemory"/>).
        /// </summary>
        void OnGroundDrop(Progenitor.Drops.PlayerGroundDrop d)
        {
            float x = d.X, y = d.Y, z = d.Z;
            if (!d.HasPosition)
            {
                // No position from the event: where the character is, else where the scan last saw them.
                if (d.Character.TryRead<Translation>(_ctx.World!.EntityManager, out var t)) { x = t.Value.x; y = t.Value.y; z = t.Value.z; }
                else if (Last(d.User) is { } l) { x = l.X; y = l.Y; z = l.Z; }
                else return;
            }
            _memory.MarkPlayerDrop(d.Item, x, y, z, Now);
        }

        Looter? Last(Entity user)
        {
            foreach (var p in _roster) if (p.User == user) return p.Looter;
            return null;
        }

        /// <summary>How many of the item the player carries, from one bag reading per scan (only for players who could take something).</summary>
        int BagCount(Player p, int item)
        {
            if (p.CountScan != _scan)
            {
                p.CountScan = _scan;
                p.Counts.Clear();
                var n = Read(p.Character);
                for (var i = 0; i < n && i < _slots.Length; i++)
                {
                    var slot = _slots[i];
                    if (slot.Item != 0 && slot.Amount > 0) p.Counts[slot.Item] = (p.Counts.TryGetValue(slot.Item, out var c) ? c : 0) + slot.Amount;
                }
            }
            return p.Counts.TryGetValue(item, out var have) ? have : 0;
        }

        /// <summary>The bag into the reused slot buffer; the slot count, or -1 when unreadable.</summary>
        int Read(Entity character)
        {
            var bag = _ctx.Inventory.Of(character);
            var n = _ctx.Inventory.ReadInto(bag, _slots);
            if (n > _slots.Length)
            {
                _slots = new InventorySlot[n];
                n = _ctx.Inventory.ReadInto(bag, _slots);
            }
            return n;
        }

        // ---------------- room and moves ----------------

        int Room(ulong account, int item)
        {
            var now = _now;
            if (!_byAccount.TryGetValue(account, out var p) || !CanLoot(p)) return 0;
            _roomAccount = account;
            var t0 = _phase.Elapsed.TotalMilliseconds;
            var max = Limits.MaxFor(item, _settings.FilterFor(account), _inCategory);
            var t1 = _phase.Elapsed.TotalMilliseconds;
            _filterMs += t1 - t0;
            if (max == 0) return 0;
            var fits = _ctx.Inventory.FreeSpace(_ctx.Inventory.Of(p.Character), item);
            var inBags = fits <= 0 ? 0 : BagCount(p, item);
            var t2 = _phase.Elapsed.TotalMilliseconds;
            _bagReadMs += t2 - t1;
            if (fits <= 0) return 0;
            int? cap = null;
            long stored = 0;
            if (_settings.CapsFor(account) && _satis.Storage(account, item, now) is { } s)
            {
                cap = s.Cap;
                stored = s.Total;
            }
            _capsMs += _phase.Elapsed.TotalMilliseconds - t2;
            return Limits.Room(max, inBags, cap, stored, fits);
        }

        /// <summary>
        /// Progenitor's take: it adds to the bag first and shrinks the pile only by what the bag took,
        /// removing an emptied pile with replication.
        /// </summary>
        void Move(EntityManager em, Award a)
        {
            if (a.Take <= 0 || !_byAccount.TryGetValue(a.Account, out var player) || !_piles.TryGetValue(a.Drop.Key, out var known)) return;
            if (known.Unique) { MoveWhole(em, a, player, known); return; }
            var result = _ctx.Drops.TakeInto(Progenitor.Drops.GroundPileId.For(known.Entity), _ctx.Inventory.Of(player.Character), a.Take);
            if (result.Outcome == InventoryOutcome.Inconsistent)
            {
                // The bag could not be read back: the items may be in it already. Leave this pile alone for good.
                known.Ignore = true;
                _ctx.Log.LogWarning($"Autoloot: a take did not balance ({result.Done} of {a.Take} reached the bag; {result.Reason}); that pile is left alone.");
                if (result.Done <= 0) return;
                Picked(player, a.Drop.Item, result.Done, result.Add?.TargetAfter ?? 0);
                return;
            }
            var done = Math.Max(0, result.Done);
            if (done <= 0) return;
            // The bag grew by this: keep this scan's count right for the next pile of the same item.
            if (player.CountScan == _scan) player.Counts[a.Drop.Item] = (player.Counts.TryGetValue(a.Drop.Item, out var had) ? had : 0) + done;
            Picked(player, a.Drop.Item, done, result.Add?.TargetAfter ?? BagCount(player, a.Drop.Item));
            // Only a drop that really moved uses the player's turn.
            if (a.Shared) _ledger.Got(a.Account);
            if (result.PileRemoved || result.PileAfter <= 0) known.Ignore = true; // removed: never touched again
            else a.Drop.Amount = result.PileAfter;
        }

        /// <summary>
        /// A single item with its own state: the game's own slot move takes it out of the pile's
        /// inventory into the bag (the same move a hand pickup makes, never a copy), and the pile is
        /// removed once its inventory is empty. A full bag leaves it where it is.
        /// </summary>
        void MoveWhole(EntityManager em, Award a, Player player, Pile known)
        {
            var e = known.Entity;
            if (!em.Exists(e) || e.Has<DestroyTag>(em)) { known.Ignore = true; return; }
            var bag = _ctx.Inventory.Of(player.Character);
            var result = _ctx.Inventory.TransferAll(_ctx.Inventory.Of(e), bag);
            if (result.Moved <= 0)
            {
                if (!result.Ok) _ctx.Log.LogWarning("Autoloot: could not move a single item out of its pile (" + result.Reason + ").");
                return; // full bags, or the game refused: it stays on the ground
            }
            if (player.CountScan == _scan) player.Counts[a.Drop.Item] = (player.Counts.TryGetValue(a.Drop.Item, out var had) ? had : 0) + result.Moved;
            Picked(player, a.Drop.Item, result.Moved, BagCount(player, a.Drop.Item));
            if (a.Shared) _ledger.Got(a.Account);
            if (!result.Ok || result.Left > 0)
            {
                // Something stayed in the pile: leave the pile, and its amount, to the game.
                if (!result.Ok) _ctx.Log.LogWarning("Autoloot: a single-item move stopped part-way (" + result.Reason + "); the pile is left alone.");
                known.Ignore = !result.Ok;
                return;
            }
            // Empty now: never touch it again and let the game remove it, as after a hand pickup.
            known.Ignore = true;
            if (e.TryRead<ItemPickup>(em, out var pile))
            {
                pile.ItemAmount = 0;
                e.Write(em, pile);
            }
            try { BuffSweep.DestroyUnitReplicated(em, e, DestroyDebugReason.None, "Autoloot: picked up"); }
            catch (Exception ex) { _ctx.Log.LogWarning("Autoloot: could not remove an emptied pile: " + ex.Message); }
        }

        void ReleaseQueries()
        {
            _pileQuery.Release(_ctx.Log.LogWarning);
            ReleaseBagQueries();
        }

        // ---------------- tick timer ----------------

        void Phases(double piles, double bags, double assign, double moves, int near)
        {
            _maxPiles = Math.Max(_maxPiles, piles);
            _maxBags = Math.Max(_maxBags, bags);
            _maxAssign = Math.Max(_maxAssign, assign);
            _maxMoves = Math.Max(_maxMoves, moves);
            _maxNear = Math.Max(_maxNear, near);
        }

        void Measure(double ms)
        {
            _ticks++;
            _sumMs += ms;
            _maxMs = Math.Max(_maxMs, ms);
            var now = Now;
            if (ms > SlowTickMs && now >= _nextSlowWarning)
            {
                _nextSlowWarning = now + ReportSeconds;
                _ctx.Log.LogWarning($"Autoloot: slow tick {ms:0.0} ms ({Counts()}).");
            }
            if (now < _nextReport) return;
            _nextReport = now + ReportSeconds;
            _ctx.Log.LogInfo($"Autoloot: last minute {_ticks} ticks, avg {(_ticks > 0 ? _sumMs / _ticks : 0):0.00} ms, max {_maxMs:0.00} ms " +
                $"(piles {_maxPiles:0.00}, bags {_maxBags:0.00}, assign {_maxAssign:0.00} [filter {_maxFilter:0.00}, bag reads {_maxBagRead:0.00}, caps {_maxCaps:0.00}], moves {_maxMoves:0.00}, ground bags {_maxBagMoves:0.00}); " +
                $"{Counts()}, {_moves} pickups, {_bagsEmptied} ground bags emptied.");
            _ticks = _moves = _bagsEmptied = 0;
            _sumMs = _maxMs = _maxPiles = _maxBags = _maxAssign = _maxMoves = _maxBagMoves = 0;
            _maxNear = 0;
            _maxFilter = _maxBagRead = _maxCaps = 0;
        }

        string Counts() =>
            $"{_piles.Count} piles, {_maxNear} near, {_deathBags} death bags, {_droppedBags} dropped bags, {_roster.Count} players, " +
            $"{_on.Count} with autoloot on, {_memory.MarkCount} drop marks";
    }
}
