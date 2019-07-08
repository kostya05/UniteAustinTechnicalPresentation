using System.ComponentModel;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Entities;
using ReadOnly = Unity.Collections.ReadOnlyAttribute;

[UpdateAfter(typeof(FormationMaintenanceSystem))]
public class PrepareMinionTargetsSystem : JobComponentSystem
{
    private EntityQuery minionsQuery;

    protected override void OnCreate()
    {
        minionsQuery = GetEntityQuery(ComponentType.ReadOnly<UnitTransformData>(),
            ComponentType.ReadWrite<MinionTarget>(),
            ComponentType.ReadOnly<MinionData>(),
            ComponentType.ReadWrite<MinionBitmask>(),
            ComponentType.ReadOnly<IndexInFormationData>(),
            ComponentType.ReadWrite<MinionPathData>());
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var length = minionsQuery.CalculateLength();
        if (length == 0) 
            return inputDeps;

        var prepareTargetsJob = new PrepareMinonTargets
        {
            formations = GetComponentDataFromEntity<FormationData>(),
            baseMinionSpeed = SimulationSettings.Instance.MinionSpeed
        };

        var PrepareTargetsFence = prepareTargetsJob.Schedule(minionsQuery, inputDeps);
        return PrepareTargetsFence;
    }

    [BurstCompile]
    private struct PrepareMinonTargets : IJobForEach<UnitTransformData,MinionData, MinionBitmask, MinionTarget, MinionPathData, IndexInFormationData>
    {
        [ReadOnly]
        public ComponentDataFromEntity<FormationData> formations;

        [ReadOnly]
        public float baseMinionSpeed;

        public void Execute([ReadOnly] ref UnitTransformData transform, [ReadOnly]ref MinionData c1, ref MinionBitmask bitmask, ref MinionTarget minionTarget,
            ref MinionPathData pathInfo, [ReadOnly]ref IndexInFormationData indicesInFormation)
        {
            var formation = formations[transform.FormationEntity];

            var target = transform.Position;

            float distance;
            var unitCanMove = indicesInFormation.IndexInFormation >= formation.UnitCount - formation.SpawnedCount;
            if (unitCanMove)
            {
                target = formation.Position + formation.GetOffsetFromCenter(indicesInFormation.IndexInFormation);
                bitmask.IsSpawned = true;
                distance = math.length(target - transform.Position);
            }
            else
                distance = 0;

            minionTarget.Target = target;
            minionTarget.speed = formation.SpawnedCount == formation.UnitCount ? baseMinionSpeed : baseMinionSpeed * 1.75f;
                
            if (distance < FormationPathFindSystem.FarDistance)
            {
                pathInfo.bitmasks = 0;
                //pathInfo.targetPosition = new float3(100000f, 0, 100000f);
                //pathInfo.pathFoundToPosition = -pathInfo.targetPosition;
            }
            else
            {
                pathInfo.bitmasks |= 1;
                pathInfo.targetPosition = target;
            }
        }
    }
}