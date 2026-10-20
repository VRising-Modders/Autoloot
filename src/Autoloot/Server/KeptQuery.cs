// SPDX-License-Identifier: AGPL-3.0-only
using System;
using Progenitor.Ecs;
using Unity.Collections;
using Unity.Entities;

namespace Autoloot.Server
{
    /// <summary>
    /// An entity query built once and reused every tick, bound to the world it was built in. It is
    /// disposed only while that same world is alive: disposing a query whose world is gone faults
    /// inside the game, where no catch can stop it. Otherwise the handle is just forgotten.
    /// </summary>
    internal sealed class KeptQuery
    {
        readonly ComponentType _type;
        EntityQuery _query;
        World? _world;
        ulong _seq;
        bool _built;

        internal KeptQuery(ComponentType type) => _type = type;

        /// <summary>This tick's matching entities, sleeping ones included. The caller disposes the array.</summary>
        internal NativeArray<Entity> Entities(World world)
        {
            if (_built && (!ReferenceEquals(_world, world) || Seq(world) != _seq)) Release(null);
            if (!_built)
            {
                _query = Query.Build(world.EntityManager, includeDisabled: true, includeSystems: false, _type);
                _world = world;
                _seq = Seq(world);
                _built = true;
            }
            return _query.ToEntityArray(Allocator.Temp);
        }

        internal void Release(Action<string>? warn)
        {
            if (!_built) return;
            var world = _world;
            var seq = _seq;
            _built = false;
            _world = null;
            bool alive;
            try { alive = world != null && world.IsCreated && world.SequenceNumber == seq; }
            catch (Exception) { alive = false; }
            if (!alive) return;
            try { _query.Dispose(); }
            catch (Exception e) { warn?.Invoke("Autoloot: query dispose threw: " + e.Message); }
        }

        static ulong Seq(World world)
        {
            try { return world.SequenceNumber; }
            catch (Exception) { return 0; }
        }
    }
}
