// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Generic;

namespace Autoloot.Server
{
    /// <summary>
    /// Which players have Autoloot's client part this session (they said hello after joining): only
    /// they get pickup notices, and the server's rules for players without it do not apply to them.
    /// Everything else a player sets is kept by Progenitor (<see cref="Config.PlayerConfig"/>).
    /// </summary>
    internal sealed class PlayerStore
    {
        readonly HashSet<ulong> _clients = new();

        public void ClientSeen(ulong account) => _clients.Add(account);

        /// <summary>On joining and leaving: whether this login has the client part is found out again.</summary>
        public void ClientGone(ulong account) => _clients.Remove(account);

        public bool HasClient(ulong account) => _clients.Contains(account);
    }
}
