using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Entities;

[UpdateAfter(typeof(PrepareMinionTargetsSystem))]
public class FormationIntegritySystem : JobComponentSystem
{
    private EntityQuery formationsQuery;

    //[Inject]
    //public ComponentDataFromEntity<MinionData> minionData;

    //[Inject]
    //public ComponentDataFromEntity<UnitTransformData> minionTransforms;
    
    //[Inject]
    //public ComponentDataFromEntity<MinionTarget> minionTargets;
    
    protected override void OnCreate()
    {
        formationsQuery = GetEntityQuery(ComponentType.ChunkComponent<EntityRef>(),
            ComponentType.ReadOnly<FormationData>(),
            ComponentType.ReadWrite<FormationIntegrityData>());
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var count = formationsQuery.CalculateLength();
        if (count == 0)
            return inputDeps;

        var calculateIntegrityDataJob = new CalculateIntegrityDataJob
        {
            minionData = GetComponentDataFromEntity<MinionData>(),
            transforms = GetComponentDataFromEntity<UnitTransformData>(),
            minionTargets = GetComponentDataFromEntity<MinionTarget>()
        };

        var CalculateIntegrityFence = calculateIntegrityDataJob.Schedule(formationsQuery, inputDeps);

        return CalculateIntegrityFence;
    }

    [BurstCompile]
    private struct CalculateIntegrityDataJob : IJobForEachWithEntity_EBCC<EntityRef, FormationIntegrityData, FormationData>
    {
        [ReadOnly]
        public ComponentDataFromEntity<MinionData> minionData;

        [ReadOnly]
        public ComponentDataFromEntity<UnitTransformData> transforms;

        [ReadOnly]
        public ComponentDataFromEntity<MinionTarget> minionTargets;
        
        public void Execute(Entity entity, int index, DynamicBuffer<EntityRef> unitsInFormation, ref FormationIntegrityData integrityData, [ReadOnly]ref FormationData formationData)
        {
            integrityData = new FormationIntegrityData();
            
            for (var i = 0; i < formationData.UnitCount; ++i)
            {
                var unitEntity = unitsInFormation[i].entity;

                if (unitEntity == new Entity()) break; // if it's a null entity we reached
                
                var unitTransform = transforms[unitEntity];
                var unitData = minionData[unitEntity];
                var target = minionTargets[unitEntity].Target;

                if (unitData.attackCycle >= 0)
                    ++integrityData.unitsAttacking;
                
                var distance = math.length(target - unitTransform.Position);
                
                if (distance < FormationPathFindSystem.FarDistance)
                {
                    ++integrityData.unitCount;
                    if (distance >= FormationPathFindSystem.CloseDistance)
                        ++integrityData.unitsClose;
                }
                else
                {
                    ++integrityData.unitsFar;
                }
            }
        }
    }
}