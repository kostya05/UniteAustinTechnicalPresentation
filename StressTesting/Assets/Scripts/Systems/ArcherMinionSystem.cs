using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Entities;

[UpdateAfter(typeof(MinionCollisionSystem))]
public class ArcherMinionSystem : JobComponentSystem
{
	public struct RangedMinions
	{
		public NativeArray<MinionData> minions;
		public NativeArray<UnitTransformData> transforms;
		public NativeArray<MinionBitmask> bitmask;

		public int Length;

		public RangedMinions(EntityQuery entityQuery) : this()
		{
			Length = entityQuery.CalculateLength();
			if(Length == 0)
				return;

			minions = entityQuery.ToComponentDataArray<MinionData>(Allocator.TempJob);
			transforms = entityQuery.ToComponentDataArray<UnitTransformData>(Allocator.TempJob);
			bitmask = entityQuery.ToComponentDataArray<MinionBitmask>(Allocator.TempJob);
		}
	}

	private EntityQuery rangedMinionsQuery;

	private ComponentDataFromEntity<FormationClosestData> formationClosestDataFromEntity;
	private ComponentDataFromEntity<FormationData> formationsFromEntity;

	private UnitLifecycleManager lifeCycleManager;

	private JobHandle archerJobFence;

	public float archerAttackCycle = 0;

	protected override void OnCreate()
	{
		lifeCycleManager = World.GetOrCreateSystem<UnitLifecycleManager>();
		rangedMinionsQuery = GetEntityQuery(
			ComponentType.ReadOnly<RangedUnitData>(),
			ComponentType.ReadOnly<AliveMinionData>(),
			ComponentType.ReadWrite<MinionData>(),
			ComponentType.ReadWrite<UnitTransformData>(),
			ComponentType.ReadWrite<MinionBitmask>());
	}

	protected override JobHandle OnUpdate(JobHandle inputDeps)
	{
		var rangedMinions = new RangedMinions(rangedMinionsQuery);
		if (rangedMinions.Length == 0 || !lifeCycleManager.createdArrows.IsCreated)
			return inputDeps;

		float prevArcherAttackCycle = archerAttackCycle;
		archerAttackCycle += Time.deltaTime;
		if (archerAttackCycle > SimulationSettings.Instance.ArcherAttackTime)
		{
			archerAttackCycle -= SimulationSettings.Instance.ArcherAttackTime;
		}

		formationsFromEntity = GetComponentDataFromEntity<FormationData>();
		formationClosestDataFromEntity = GetComponentDataFromEntity<FormationClosestData>();

		var archerJob = new ArcherJob
		{
			createdArrowsQueue = lifeCycleManager.createdArrows.ToConcurrent(),
			archers = rangedMinions.minions,
			transforms = rangedMinions.transforms,
			formations = formationsFromEntity,
			closestFormationsFromEntity = formationClosestDataFromEntity,
			minionConstData = rangedMinions.bitmask,
			randomizer = Time.frameCount,
			archerHitTime = SimulationSettings.Instance.ArcherHitTime,
			archerAttackCycle = archerAttackCycle,
			prevArcherAttackCycle = prevArcherAttackCycle
		};

		archerJobFence = archerJob.Schedule(rangedMinions.Length, SimulationState.SmallBatchSize, inputDeps);

		return archerJobFence;
	}
}
