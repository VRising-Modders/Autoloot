// SPDX-License-Identifier: AGPL-3.0-only
using System.Linq;
using Xunit;

namespace Autoloot.Engine.Tests
{
    public class DeathBagTests
    {
        static Looter At(ulong account, float x) => new() { Account = account, X = x, On = true };
        static ServerRules Rules(DeathBagRule rule) => new() { Radius = 10, DeathBags = rule };
        static bool Able(ulong _) => true;
        static bool NoClan(ulong _) => false;

        static ulong? Death(ulong owner, Looter[] looters, DeathBagRule rule, GameBagAccess access = GameBagAccess.Anyone,
            System.Func<ulong, bool>? able = null, System.Func<ulong, bool>? clan = null) =>
            DeathBags.Taker(owner, 0, 0, 0, looters, Rules(rule), access, able ?? Able, clan ?? NoClan);

        [Fact]
        public void Defaults()
        {
            Assert.Equal(DeathBagRule.Own, new ServerRules().DeathBags);
            Assert.Equal(DroppedBagRule.Off, new ServerRules().DroppedBags);
        }

        [Fact]
        public void OwnTakesOnlyTheirOwn()
        {
            var looters = new[] { At(1, 5), At(2, 1) };
            Assert.Equal(1UL, Death(1, looters, DeathBagRule.Own));
            Assert.Null(Death(3, looters, DeathBagRule.Own));
        }

        [Fact]
        public void OffTakesNothing() => Assert.Null(Death(1, new[] { At(1, 0) }, DeathBagRule.Off));

        [Fact]
        public void AnyoneLetsTheOwnerGoFirstThenTheClosest()
        {
            var looters = new[] { At(2, 1), At(1, 9), At(3, 2) };
            Assert.Equal(1UL, Death(1, looters, DeathBagRule.Anyone));
            Assert.Equal(2UL, Death(4, looters, DeathBagRule.Anyone));
        }

        [Fact]
        public void TheGamesOwnRuleAlwaysApplies()
        {
            var looters = new[] { At(2, 1), At(3, 2) };
            Assert.Null(Death(4, looters, DeathBagRule.Anyone, GameBagAccess.OnlySelf));
            Assert.Equal(3UL, Death(4, looters, DeathBagRule.Anyone, GameBagAccess.ClanMembers, clan: a => a == 3));
            Assert.Null(Death(4, looters, DeathBagRule.Anyone, GameBagAccess.ClanMembers));
            // The owner may always open their own.
            Assert.Equal(4UL, Death(4, new[] { At(4, 1) }, DeathBagRule.Own, GameBagAccess.OnlySelf));
        }

        [Fact]
        public void OutOfRangeOffOrUnableIsSkipped()
        {
            var off = At(1, 0);
            off.On = false;
            Assert.Null(Death(1, new[] { off }, DeathBagRule.Own));
            Assert.Null(Death(1, new[] { At(1, 0) }, DeathBagRule.Own, able: _ => false));
            Assert.Null(Death(1, new[] { At(1, 20) }, DeathBagRule.Own));
            Assert.Equal(2UL, Death(5, new[] { At(1, 1), At(2, 2) }, DeathBagRule.Anyone, able: a => a != 1));
        }

        [Fact]
        public void ADroppedBagNeverGoesBackToItsDropper()
        {
            var rules = new ServerRules { Radius = 10, DroppedBags = DroppedBagRule.Others };
            var looters = new[] { At(1, 0.5f), At(2, 4) };
            var dropper = new[] { 1UL };
            Assert.Equal(2UL, DeathBags.DroppedTaker(dropper, 0, 0, 0, looters, rules, Able));
            Assert.Null(DeathBags.DroppedTaker(dropper, 0, 0, 0, new[] { At(1, 0.5f) }, rules, Able));
            Assert.Null(DeathBags.DroppedTaker(dropper, 0, 0, 0, looters, new ServerRules { Radius = 10 }, Able)); // off by default
        }

        [Fact]
        public void AnUnknownDropperMeansNobodyTakesIt() =>
            Assert.Null(DeathBags.DroppedTaker(new ulong[0], 0, 0, 0, new[] { At(2, 1) },
                new ServerRules { Radius = 10, DroppedBags = DroppedBagRule.Others }, Able));

    }
}
