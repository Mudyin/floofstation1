﻿using Content.Server.GameTicking;
using Content.Server.Spawners.Components;
using Content.Server.Station.Systems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Server.Containers;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Spawners.EntitySystems;

public sealed class ContainerSpawnPointSystem : EntitySystem
{
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly StationSpawningSystem _stationSpawning = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawningEvent>(HandlePlayerSpawning, before: new []{ typeof(SpawnPointSystem) });
    }

    public void HandlePlayerSpawning(PlayerSpawningEvent args)
    {
        if (args.SpawnResult != null)
            return;

        // If it's just a spawn pref check if it's for cryo (silly).
        if (args.HumanoidCharacterProfile?.SpawnPriority != SpawnPriorityPreference.Cryosleep &&
            (!_proto.TryIndex(args.Job?.Prototype, out var jobProto) || jobProto.JobEntity == null))
            return;

        var query = EntityQueryEnumerator<ContainerSpawnPointComponent, ContainerManagerComponent, TransformComponent>();
        var possibleContainers = new List<Entity<ContainerSpawnPointComponent, ContainerManagerComponent, TransformComponent>>();

        while (query.MoveNext(out var uid, out var spawnPoint, out var container, out var xform))
        {
            if (args.Station != null && _station.GetOwningStation(uid, xform) != args.Station)
                continue;

            // DeltaV - Custom override for override spawnpoints, only used for prisoners currently. This shouldn't run for any other jobs
            if (args.DesiredSpawnPointType == SpawnPointType.Job
                && spawnPoint.SpawnType == SpawnPointType.Job
                && args.Job is not null
                && spawnPoint.Job is not ""
                && spawnPoint.Job == args.Job.Prototype)
            {
                possibleContainers.Add((uid, spawnPoint, container, xform));
                continue;
            }

            // If it's unset, then we allow it to be used for both roundstart and midround joins
            if (spawnPoint.SpawnType == SpawnPointType.Unset)
            {
                // make sure we also check the job here for various reasons.
                if (spawnPoint.Job == null || spawnPoint.Job == args.Job?.Prototype)
                    possibleContainers.Add((uid, spawnPoint, container, xform));
                continue;
            }

            if (_gameTicker.RunLevel == GameRunLevel.InRound && spawnPoint.SpawnType == SpawnPointType.LateJoin)
            {
                possibleContainers.Add((uid, spawnPoint, container, xform));
            }

            if (_gameTicker.RunLevel != GameRunLevel.InRound &&
                spawnPoint.SpawnType == SpawnPointType.Job &&
                (args.Job == null || spawnPoint.Job == args.Job.Prototype))
            {
                possibleContainers.Add((uid, spawnPoint, container, xform));
            }
        }

        if (possibleContainers.Count == 0)
            return;
        // we just need some default coords so we can spawn the player entity.
        var baseCoords = possibleContainers[0].Comp3.Coordinates;

        args.SpawnResult = _stationSpawning.SpawnPlayerMob(
            baseCoords,
            args.Job,
            args.HumanoidCharacterProfile,
            args.Station);

        _random.Shuffle(possibleContainers);
        foreach (var (uid, spawnPoint, manager, xform) in possibleContainers)
        {
            if (!_container.TryGetContainer(uid, spawnPoint.ContainerId, out var container, manager))
                continue;

            if (!_container.Insert(args.SpawnResult.Value, container, containerXform: xform))
                continue;

            return;
        }

        Del(args.SpawnResult);
        args.SpawnResult = null;
    }
}
