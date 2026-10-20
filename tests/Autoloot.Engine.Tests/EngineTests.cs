// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Autoloot.Engine.Tests
{
    public class LimitsTests
    {
        static bool Flowers(string cat, int item) => cat == "flowers" && item is 10 or 11;

        [Fact]
        public void NoLineMeansNoLimit() =>
            Assert.Null(Limits.MaxFor(10, new List<FilterEntry>(), Flowers));

        [Fact]
        public void ItemLineWinsOverCategory()
        {
            var f = new List<FilterEntry> { new() { Category = "flowers", Max = 5 }, new() { Item = 10, Max = 200 } };
            Assert.Equal(200, Limits.MaxFor(10, f, Flowers));
            Assert.Equal(5, Limits.MaxFor(11, f, Flowers));
            Assert.Null(Limits.MaxFor(12, f, Flowers));
        }

        [Fact]
        public void SmallestCategoryWins()
        {
            bool Both(string cat, int item) => item == 10;
            var f = new List<FilterEntry> { new() { Category = "a", Max = 50 }, new() { Category = "b", Max = 0 } };
            Assert.Equal(0, Limits.MaxFor(10, f, Both));
        }

        [Theory]
        [InlineData(null, 0, null, 0L, 500, 500)]   // only space limits
        [InlineData(100, 40, null, 0L, 500, 60)]    // filter leaves 60
        [InlineData(100, 140, null, 0L, 500, 0)]    // already over the filter
        [InlineData(null, 10, 300, 250L, 500, 40)]  // cap counts bags and storage
        [InlineData(0, 0, null, 0L, 500, 0)]        // 0 = never
        [InlineData(null, 0, null, 0L, 0, 0)]       // bags full
        public void Room(int? max, int bags, int? cap, long storage, int fits, int expected) =>
            Assert.Equal(expected, Limits.Room(max, bags, cap, storage, fits));
    }

    public class AssignerTests
    {
        static readonly ServerRules Rules = new() { Radius = 10 };

        static Drop Pile(long key, float x, int amount = 5, int item = 1) => new() { Key = key, Item = item, Amount = amount, X = x };
        static Looter At(ulong account, float x) => new() { Account = account, X = x, On = true };

        static List<Award> Run(IReadOnlyList<Drop> drops, IReadOnlyList<Looter> looters, ServerRules? rules = null,
            DropMemory? memory = null, ShareLedger? ledger = null, System.Func<ulong, int, int>? room = null)
        {
            var r = rules ?? Rules;
            memory ??= Primed(drops, looters, r.Radius);
            ledger ??= new ShareLedger();
            var awards = Assigner.Assign(drops, looters, r, memory, ledger, room ?? ((_, _) => 1000));
            foreach (var a in awards) if (a.Shared) ledger.Got(a.Account); // as the plugin does once the items moved
            return awards;
        }

        // The piles appear after the first scan, so whoever is in range saw them land.
        static DropMemory Primed(IReadOnlyList<Drop> drops, IReadOnlyList<Looter> looters, float radius)
        {
            var m = new DropMemory();
            m.Observe(new List<Drop>(), looters, radius, 0);
            m.Observe(drops, looters, radius, 1);
            m.Observe(drops, looters, radius, 1.5); // a pile waits one scan
            return m;
        }

        [Fact]
        public void ClosestPlayerGetsIt()
        {
            var a = Run(new[] { Pile(1, 5) }, new[] { At(1, 0), At(2, 6) });
            Assert.Equal(2UL, Assert.Single(a).Account);
        }

        [Fact]
        public void OutOfRangeIsLeft() =>
            Assert.Empty(Run(new[] { Pile(1, 11) }, new[] { At(1, 0) }));

        [Fact]
        public void PlayerOffOrServerOffTakesNothing()
        {
            var off = At(1, 0);
            off.On = false;
            Assert.Empty(Run(new[] { Pile(1, 1) }, new[] { off }));
            Assert.Empty(Run(new[] { Pile(1, 1) }, new[] { At(1, 0) }, new ServerRules { Enabled = false }));
        }

        [Fact]
        public void PlayerDropsAreNeverTaken()
        {
            var m = new DropMemory();
            var looters = new[] { At(1, 0) };
            m.Observe(new List<Drop>(), looters, 10, 0);
            m.MarkPlayerDrop(1, 0, 0, 0, 1);
            var pile = Pile(7, 0.5f);
            m.Observe(new[] { pile }, looters, 10, 1.2);
            m.Observe(new[] { pile }, looters, 10, 1.7);
            Assert.True(pile.PlayerDropped);
            Assert.Empty(Run(new[] { pile }, looters, memory: m));
        }

        [Fact]
        public void ALateDropMarkStillMarksThePile()
        {
            var m = new DropMemory();
            var looters = new[] { At(1, 0) };
            m.Observe(new List<Drop>(), looters, 10, 0);
            var pile = Pile(7, 0.5f);
            m.Observe(new[] { pile }, looters, 10, 1);
            m.MarkPlayerDrop(1, 0, 0, 0, 1.3); // reported after the scan saw the pile
            m.Observe(new[] { pile }, looters, 10, 1.5);
            Assert.True(pile.PlayerDropped);
            Assert.Empty(Run(new[] { pile }, looters, memory: m));
        }

        [Fact]
        public void APileWaitsOneScan()
        {
            var m = new DropMemory();
            var looters = new[] { At(1, 0) };
            m.Observe(new List<Drop>(), looters, 10, 0);
            var pile = Pile(1, 1);
            m.Observe(new[] { pile }, looters, 10, 1);
            Assert.Empty(Run(new[] { pile }, looters, memory: m));
            m.Observe(new[] { pile }, looters, 10, 1.5);
            Assert.Single(Run(new[] { pile }, looters, memory: m));
        }

        [Fact]
        public void AnOtherItemOrFarPileIsNotAPlayerDrop()
        {
            var m = new DropMemory();
            var looters = new[] { At(1, 0) };
            m.Observe(new List<Drop>(), looters, 10, 0);
            m.MarkPlayerDrop(1, 0, 0, 0, 1);
            var other = Pile(7, 0.5f, item: 2);
            var far = Pile(8, 6);
            m.Observe(new[] { other, far }, looters, 10, 1.2);
            Assert.False(other.PlayerDropped);
            Assert.False(far.PlayerDropped);
        }

        [Fact]
        public void APlayerDropSurvivesAReload()
        {
            var looters = new[] { At(1, 0) };
            var before = new DropMemory();
            before.Observe(new List<Drop>(), looters, 10, 0);
            before.MarkPlayerDrop(1, 0, 0, 0, 1);
            var mine = Pile(7, 0.5f);
            var loot = Pile(8, 1f, item: 2);
            before.Observe(new[] { mine, loot }, looters, 10, 1);
            var saved = before.PlayerDroppedKeys();
            Assert.Equal(new long[] { 7 }, saved.ToArray());

            // The mod reloads in the same world: the saved drop is still hands-off, the old loot is fair game.
            var after = new DropMemory();
            after.Restore(saved);
            var mine2 = Pile(7, 0.5f);
            var loot2 = Pile(8, 1f, item: 2);
            after.Observe(new[] { mine2, loot2 }, looters, 10, 5);
            Assert.True(mine2.PlayerDropped);
            Assert.False(loot2.PlayerDropped);
            Assert.Single(Run(new[] { mine2, loot2 }, looters, memory: after));
        }

        [Fact]
        public void AfterARestartOldPilesAreHandsOff()
        {
            var looters = new[] { At(1, 0) };
            var m = new DropMemory();
            m.OldPilesAreHandsOff();
            var old = Pile(7, 0.5f);
            m.Observe(new[] { old }, looters, 10, 0);
            Assert.True(old.PlayerDropped);
            // New loot after that is judged as usual.
            var fresh = Pile(9, 1f, item: 2);
            m.Observe(new[] { old, fresh }, looters, 10, 1);
            m.Observe(new[] { old, fresh }, looters, 10, 1.5);
            Assert.False(fresh.PlayerDropped);
        }

        [Fact]
        public void AMarkFollowsWhereANewPileSettles()
        {
            var m = new DropMemory();
            var looters = new[] { At(1, 0) };
            m.Observe(new List<Drop>(), looters, 10, 0);
            var pile = Pile(7, 9f); // first seen mid-throw, 9 m off
            m.Observe(new[] { pile }, looters, 10, 1);
            pile.X = 2;              // lands 2 m from where the player stood
            m.Observe(new[] { pile }, looters, 10, 1.5);
            m.MarkPlayerDrop(1, 0, 0, 0, 1.6);
            m.Observe(new[] { pile }, looters, 10, 2);
            Assert.True(pile.PlayerDropped);
        }

        [Fact]
        public void OneMarkClaimsBothStacksDroppedTogether()
        {
            var m = new DropMemory();
            var looters = new[] { At(1, 0) };
            m.Observe(new List<Drop>(), looters, 10, 0);
            m.MarkPlayerDrop(1, 0, 0, 0, 1);
            var a = Pile(7, 0.5f);
            var b = Pile(8, 1f);
            m.Observe(new[] { a, b }, looters, 10, 1);
            Assert.True(a.PlayerDropped);
            Assert.True(b.PlayerDropped);
        }

        [Fact]
        public void AMarkLeavesAPileThatWasAlreadyThere()
        {
            var m = new DropMemory();
            var looters = new[] { At(1, 0) };
            m.Observe(new List<Drop>(), looters, 10, 0);
            var loot = Pile(7, 1f);
            m.Observe(new[] { loot }, looters, 10, 1);       // mob loot lands
            m.MarkPlayerDrop(1, 0, 0, 0, 3);                   // two seconds later the player drops the same item
            var dropped = Pile(8, 0.5f);
            m.Observe(new[] { loot, dropped }, looters, 10, 3);
            Assert.False(loot.PlayerDropped);
            Assert.True(dropped.PlayerDropped);
        }

        [Fact]
        public void AnAsleepPileIsRememberedButNotTaken()
        {
            var pile = Pile(1, 1);
            pile.Asleep = true;
            Assert.Empty(Run(new[] { pile }, new[] { At(1, 0) }));
        }

        [Fact]
        public void LandsInRangeNeedsTheWitness()
        {
            var rules = new ServerRules { Radius = 10, Pickup = PickupRule.LandsInRange };
            var m = new DropMemory();
            var away = At(1, 50);
            m.Observe(new List<Drop>(), new[] { away }, 10, 0);
            var pile = Pile(1, 0);
            m.Observe(new[] { pile }, new[] { away }, 10, 1);
            m.Observe(new[] { pile }, new[] { away }, 10, 1.5);
            // The player walks up afterwards: not theirs.
            Assert.Empty(Run(new[] { pile }, new[] { At(1, 0) }, rules, m));
            // Comes-in-range takes it.
            Assert.Single(Run(new[] { pile }, new[] { At(1, 0) }, new ServerRules { Radius = 10 }, m));
        }

        [Fact]
        public void OldPilesAtFirstScanAreSeenByNobody()
        {
            var m = new DropMemory();
            var looters = new[] { At(1, 0) };
            var pile = Pile(1, 0);
            m.Observe(new[] { pile }, looters, 10, 0);
            Assert.False(m.Saw(1, 1));
        }

        [Fact]
        public void FullPlayerPassesToTheNextClosest()
        {
            var a = Run(new[] { Pile(1, 1) }, new[] { At(1, 0), At(2, 3) }, room: (acct, _) => acct == 1 ? 0 : 99);
            Assert.Equal(2UL, Assert.Single(a).Account);
        }

        [Fact]
        public void TakeIsCappedByRoomAndPromisesAddUp()
        {
            var piles = new[] { Pile(1, 1, 30), Pile(2, 1, 30) };
            var a = Run(piles, new[] { At(1, 0) }, room: (_, _) => 40);
            Assert.Equal(new[] { 30, 10 }, a.Select(x => x.Take).ToArray());
        }

        [Fact]
        public void EvenTakesTurns()
        {
            var rules = new ServerRules { Radius = 10, Share = ShareRule.Even };
            var piles = Enumerable.Range(1, 6).Select(i => Pile(i, 1)).ToArray();
            var a = Run(piles, new[] { At(1, 0), At(2, 5) }, rules);
            Assert.Equal(3, a.Count(x => x.Account == 1));
            Assert.Equal(3, a.Count(x => x.Account == 2));
        }

        [Fact]
        public void EvenRemembersAcrossScans()
        {
            var rules = new ServerRules { Radius = 10, Share = ShareRule.Even };
            var ledger = new ShareLedger();
            var looters = new[] { At(1, 0), At(2, 5) };
            var first = Run(new[] { Pile(1, 1) }, looters, rules, ledger: ledger);
            var second = Run(new[] { Pile(2, 1) }, looters, rules, ledger: ledger);
            Assert.NotEqual(first[0].Account, second[0].Account);
        }

        [Fact]
        public void TheAwardCapLeavesTheRestForLater()
        {
            var rules = new ServerRules { Radius = 10, Share = ShareRule.Even };
            var piles = Enumerable.Range(1, 10).Select(i => Pile(i, 1)).ToArray();
            var looters = new[] { At(1, 0), At(2, 5) };
            var ledger = new ShareLedger();
            var memory = Primed(piles, looters, 10);
            var a = Assigner.Assign(piles, looters, rules, memory, ledger, (_, _) => 1000, maxAwards: 3);
            Assert.Equal(3, a.Count);
            Assert.Equal(new long[] { 1, 2, 3 }, a.Select(x => x.Drop.Key).ToArray());
        }

        [Fact]
        public void ATurnIsOnlyUsedOnceTheItemsMove()
        {
            var rules = new ServerRules { Radius = 10, Share = ShareRule.Even };
            var looters = new[] { At(1, 0), At(2, 5) };
            var ledger = new ShareLedger();
            var piles = new[] { Pile(1, 1) };
            var memory = Primed(piles, looters, 10);
            // Awarded, but the move failed: nobody's turn is used, so the same player is first again.
            var first = Assigner.Assign(piles, looters, rules, memory, ledger, (_, _) => 1000);
            var again = Assigner.Assign(piles, looters, rules, memory, ledger, (_, _) => 1000);
            Assert.Equal(first[0].Account, again[0].Account);
            Assert.Equal(0, ledger.Turn);
        }


        [Fact]
        public void AnUncontestedPileDoesNotUseATurn()
        {
            var rules = new ServerRules { Radius = 10, Share = ShareRule.Even };
            var ledger = new ShareLedger();
            Run(new[] { Pile(1, 1) }, new[] { At(1, 0) }, rules, ledger: ledger);
            Assert.Equal(0, ledger.LastTurn(1));
        }
    }
}
