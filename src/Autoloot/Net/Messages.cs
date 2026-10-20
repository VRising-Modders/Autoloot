// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Generic;
using Progenitor.Networking;

namespace Autoloot.Net
{
    // Messages over Progenitor's ctx.Requests and ctx.Messages. Field numbers are permanent: never
    // reuse or retype one. Players' settings and filter are not here: Progenitor keeps and sends them.

    /// <summary>
    /// The client part says it is there, after every join and once a minute: the server then sends
    /// it pickup notices and treats the player as having the client part.
    /// </summary>
    [ModMessage("al.hello")]
    public sealed class HelloReq
    {
    }

    [ModMessage("al.hello.reply")]
    public sealed class HelloReply
    {
    }

    /// <summary>
    /// Server to client, every few seconds while something was picked up: each item, how many were
    /// picked up, and how many the player now has in their bags; then the ground bags emptied.
    /// </summary>
    [ModMessage("al.picked")]
    public sealed class PickedMsg
    {
        [Field(1)] public List<int> Items = new();
        [Field(2)] public List<int> Amounts = new();
        [Field(3)] public List<int> Totals = new();
        [Field(4)] public int DeathBags;
        [Field(5)] public int DroppedBags;
        /// <summary>Items moved out of those bags, all together.</summary>
        [Field(6)] public int BagItems;
    }
}
