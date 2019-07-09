using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Unity.Entities;
using UnityEngine.Experimental.AI;

[UpdateAfter(typeof(MinionAttackSystem))]
public class MinionSystem : JobComponentSystem
{
	private EntityQuery minionsQuery;
	
	public NativeMultiHashMap<int, int> CollisionBuckets;

	private FormationSystem formationSystem;

	public const int fieldWidth = 4000;
	public const int fieldWidthHalf = fieldWidth / 2;
	public const int fieldHeight = 4000;
	public const int fieldHeightHalf = fieldHeight / 2;
	public const float step = 2f;
	
	NavMeshQuery moveLocationQuery;
	protected override void OnCreate()
	{
		formationSystem = World.GetOrCreateSystem<FormationSystem>();
		var navMeshWorld = NavMeshWorld.GetDefaultWorld();
		moveLocationQuery = new NavMeshQuery(navMeshWorld, Allocator.Persistent);

		minionsQuery = GetEntityQuery(
			ComponentType.ReadOnly<AliveMinionData>(),
			ComponentType.ReadOnly<MinionTarget>(),
			ComponentType.ReadWrite<MinionData>(),
			ComponentType.ReadWrite<UnitTransformData>(),
			ComponentType.ReadWrite<RigidbodyData>(),
			ComponentType.ReadWrite<TextureAnimatorData>(),
			ComponentType.ReadWrite<MinionBitmask>(),
			ComponentType.ReadWrite<NavMeshLocationComponent>(),
			ComponentType.ReadWrite<MinionAttackData>(),
			ComponentType.ReadWrite<IndexInFormationData>());
	}
	protected override void OnDestroyManager()
	{
		base.OnDestroyManager();

		if (CollisionBuckets.IsCreated) CollisionBuckets.Dispose();
		moveLocationQuery.Dispose();
	}

	public static int Hash(float3 position)
	{
		int2 quantized = new int2(math.floor(position.xz / step));
		return quantized.x + fieldWidthHalf + (quantized.y + fieldHeightHalf) * fieldWidth;
	}

	public void ForceInjection()
	{
		//UpdateInjectedComponentGroups();
	}

	protected override JobHandle OnUpdate(JobHandle inputDeps)
	{
		if (!Application.isPlaying)
			return inputDeps;

		var count = minionsQuery.CalculateLength();
		if (count == 0) 
			return inputDeps; // I still hate this initialization issues

		// TODO maybe fix native array
		var forwardsBuffer = new NativeArray<Vector3>(count, Allocator.TempJob);
		var positionsBuffer = new NativeArray<Vector3>(count, Allocator.TempJob);
		var locationsBuffer = new NativeArray<NavMeshLocation>(count, Allocator.TempJob);

		var rigidbodyDatas = minionsQuery.ToComponentDataArray<RigidbodyData>(Allocator.TempJob);
		var targetPositions = minionsQuery.ToComponentDataArray<MinionTarget>(Allocator.TempJob);
		var transforms = minionsQuery.ToComponentDataArray<UnitTransformData>(Allocator.TempJob);
		var minionAttackDatas = minionsQuery.ToComponentDataArray<MinionAttackData>(Allocator.TempJob);
		var minions = minionsQuery.ToComponentDataArray<MinionData>(Allocator.TempJob);
		var animationDatas = minionsQuery.ToComponentDataArray<TextureAnimatorData>(Allocator.TempJob);
		var navMeshLocations = minionsQuery.ToComponentDataArray<NavMeshLocationComponent>(Allocator.TempJob);
		

		// ============ JOB CREATION ===============
		var minionBehaviorJob = new MinionBehaviourJob
		{
			rigidbodyData = rigidbodyDatas,
			targetPositions = targetPositions,
			transforms = transforms,
			minionAttackData = minionAttackDatas,
			minionData = minions,
			animatorData = animationDatas,
			navMeshLocations = navMeshLocations,
			forwardsBuffer = forwardsBuffer,
			positionsBuffer = positionsBuffer,
			locationsBuffer = locationsBuffer,
			archerAttackTime = SimulationSettings.Instance.ArcherAttackTime,
			dt = Time.deltaTime,
			randomizer = Time.frameCount,
		};

		var minionBehaviorMoveJob = new MinionBehaviourMoveJob
		{
			positionsBuffer = positionsBuffer,
			locationsBuffer = locationsBuffer,
			query = moveLocationQuery
		};

		var minionBehaviorSyncbackJob = new MinionBehaviourSyncbackJob
		{
			transforms = transforms,
			navMeshLocations = navMeshLocations,
			forwardsBuffer = forwardsBuffer,
			positionsBuffer = positionsBuffer,
			locationsBuffer = locationsBuffer
		};

		
		var minionBehaviorJobFence = minionBehaviorJob.ScheduleBatch(minions.Length, SimulationState.BigBatchSize,
			inputDeps);
		minionBehaviorJobFence = minionBehaviorMoveJob.ScheduleBatch(minions.Length, SimulationState.BigBatchSize, minionBehaviorJobFence);
		var navMeshWorld = NavMeshWorld.GetDefaultWorld();
		navMeshWorld.AddDependency(minionBehaviorJobFence);
		minionBehaviorJobFence = minionBehaviorSyncbackJob.ScheduleBatch(minions.Length, SimulationState.BigBatchSize, minionBehaviorJobFence);

		return minionBehaviorJobFence;
	}
}
