// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;

namespace Autoloot.Engine
{
    /// <summary>Who may open a player's death bag, by the game's own server setting (the bag's prefab).</summary>
    public enum GameBagAccess
    {
        OnlySelf = 0,
        ClanMembers = 1,
        Anyone = 2,
    }

    /// <summary>
    /// Who empties a bag on the ground. Both kinds are taken whole (everything that fits, gear with
    /// its stats included), never limited by filters or caps, and the pickup rule does not apply.
    /// </summary>
    /// <remarks>
    /// <b>Death bags</b>: what a player carried when they died. The owner always comes first; others only
    /// with <see cref="DeathBagRule.Anyone"/>, and never beyond what the game itself lets them open.
    /// <b>Dropped bags</b>: a player's whole inventory dropped on purpose. Never the player who dropped it
    /// (it would go straight back); others only with <see cref="DroppedBagRule.Others"/>.
    /// </remarks>
    public static class DeathBags
    {
        /// <summary>The account that empties this death bag now, or null.</summary>
        /// <param name="canLoot">False for a player who is dead, downed or otherwise unable right now.</param>
        /// <param name="sameClan">Whether a player is in the owner's clan (for <see cref="GameBagAccess.ClanMembers"/>).</param>
        public static ulong? Taker(ulong owner, float x, float y, float z, IReadOnlyList<Looter> looters, ServerRules rules,
            GameBagAccess access, Func<ulong, bool> canLoot, Func<ulong, bool> sameClan)
        {
            if (!rules.Enabled || rules.DeathBags == DeathBagRule.Off) return null;
            bool Allowed(ulong account) =>
                account == owner
                || (rules.DeathBags == DeathBagRule.Anyone
                    && (access == GameBagAccess.Anyone || (access == GameBagAccess.ClanMembers && sameClan(account))));
            return Closest(x, y, z, looters, rules.Radius, owner, Allowed, canLoot);
        }

        /// <summary>
        /// The account that empties this dropped bag now, or null. <paramref name="droppers"/> are who
        /// dropped it, as the game's drop event says; with none known nobody takes it, since it could
        /// go straight back to the player who dropped it.
        /// </summary>
        public static ulong? DroppedTaker(IReadOnlyCollection<ulong> droppers, float x, float y, float z, IReadOnlyList<Looter> looters,
            ServerRules rules, Func<ulong, bool> canLoot)
        {
            if (!rules.Enabled || rules.DroppedBags == DroppedBagRule.Off || droppers.Count == 0) return null;
            return Closest(x, y, z, looters, rules.Radius, 0, a => !Contains(droppers, a), canLoot);
        }

        static bool Contains(IReadOnlyCollection<ulong> set, ulong account)
        {
            foreach (var a in set) if (a == account) return true;
            return false;
        }

        /// <summary>The closest allowed player in range; <paramref name="first"/> wins outright when allowed and in range.</summary>
        static ulong? Closest(float x, float y, float z, IReadOnlyList<Looter> looters, float radius, ulong first,
            Func<ulong, bool> allowed, Func<ulong, bool> canLoot)
        {
            var at = new Drop { X = x, Y = y, Z = z };
            ulong? best = null;
            var bestDistance = float.MaxValue;
            foreach (var l in looters)
            {
                if (!l.On) continue;
                var d = l.DistanceTo(at);
                if (d > radius || !allowed(l.Account) || !canLoot(l.Account)) continue;
                if (first != 0 && l.Account == first) return first;
                if (d < bestDistance || (d == bestDistance && l.Account < best)) { best = l.Account; bestDistance = d; }
            }
            return best;
        }

    }
}
