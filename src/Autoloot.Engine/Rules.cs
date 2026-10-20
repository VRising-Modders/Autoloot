// SPDX-License-Identifier: AGPL-3.0-only
namespace Autoloot.Engine
{
    /// <summary>Which drops a player may pick up (admin setting).</summary>
    public enum PickupRule
    {
        /// <summary>Any drop that is in range, including one the player walks up to later.</summary>
        ComesInRange = 0,

        /// <summary>Only a drop that landed while the player was already in range.</summary>
        LandsInRange = 1,
    }

    /// <summary>Who gets a drop that several players could pick up (admin setting).</summary>
    public enum ShareRule
    {
        /// <summary>The closest player.</summary>
        Closest = 0,

        /// <summary>Players take turns: the one who got a shared drop least recently.</summary>
        Even = 1,
    }

    /// <summary>Whose death bags are emptied into a player's bags (admin setting).</summary>
    public enum DeathBagRule
    {
        /// <summary>No death bag is ever touched.</summary>
        Off = 0,

        /// <summary>Only the player's own death bag (the default).</summary>
        Own = 1,

        /// <summary>Anyone's: the owner first, else the closest player in range.</summary>
        Anyone = 2,
    }

    /// <summary>Whether a whole inventory a player dropped on purpose is picked up (admin setting).</summary>
    public enum DroppedBagRule
    {
        /// <summary>Never (the default): it was dropped on purpose.</summary>
        Off = 0,

        /// <summary>By anyone except the player who dropped it.</summary>
        Others = 1,
    }

    /// <summary>The server-wide settings, as the engine reads them.</summary>
    public sealed class ServerRules
    {
        public const float MinRadius = 1;
        public const float MaxRadius = 30;
        public const float DefaultRadius = 10;

        /// <summary>Off: nobody picks anything up automatically.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Metres from the player, in any direction.</summary>
        public float Radius { get; set; } = DefaultRadius;

        public PickupRule Pickup { get; set; } = PickupRule.ComesInRange;

        public ShareRule Share { get; set; } = ShareRule.Closest;

        public DeathBagRule DeathBags { get; set; } = DeathBagRule.Own;

        public DroppedBagRule DroppedBags { get; set; } = DroppedBagRule.Off;

        /// <summary>
        /// Players without the client part get the basics: autoloot with the defaults (or what they
        /// set while they had it). Off: nothing is picked up for them.
        /// </summary>
        public bool NoClientAutoloot { get; set; } = true;

        /// <summary>Tell a player without the client part, once per login, what autoloot does and how to get the rest.</summary>
        public bool NoClientHint { get; set; } = true;
    }

}
