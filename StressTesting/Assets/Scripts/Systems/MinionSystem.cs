using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Unity.Entities;
using UnityEngine.Experimental.AI;

[UpdateAfter(typeof(MinionAttackSystem))]
public class MinionSystem : JobComponentSystem
{
	public struct Minions
	{
		[ReadOnly]
		public NativeArray<AliveMinionData> aliveMinionsFilter;
		[ReadOnly]
		public NativeArray<MinionTarget> targets;
		public NativeArray<MinionData> minions;
		public NativeArray<UnitTransformData> transforms;
		public NativeArray<RigidbodyData> velocities;
		public NativeArray<TextureAnimatorData> animationData;
		public NativeArray<MinionBitmask> bitmask;
		public NativeArray<NavMeshLocationComponent> navMeshLocations;
		public NativeArray<MinionAttackData> attackData;
		public NativeArray<Entity> entities;
		public NativeArray<IndexInFormationData> indicesInFormation;

		public int Length;
	}
	
	[Inject]
	private Minions minions;
	
	//[Inject]
	//private ComponentDataFromEntity<FormationClosestData> formationClosestDataFromEntity;

	//[Inject]
	//private ComponentDataFromEntity<FormationData> formationsFromEntity;
	
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

		if (minions.Length == 0) return inputDeps; // I still hate this initialization issues

		// TODO maybe fix native array
		var forwardsBuffer = new NativeArray<Vector3>(minions.Length, Allocator.TempJob);
		var positionsBuffer = new NativeArray<Vector3>(minions.Length, Allocator.TempJob);
		var locationsBuffer = new NativeArray<NavMeshLocation>(minions.Length, Allocator.TempJob);
		

		// ============ JOB CREATION ===============
		var minionBehaviorJob = new MinionBehaviourJob
		{
			rigidbodyData = minions.velocities,
			targetPositions = minions.targets,
			transforms = minions.transforms,
			minionAttackData = minions.attackData,
			minionData = minions.minions,
			animatorData = minions.animationData,
			navMeshLocations = minions.navMeshLocations,
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
			transforms = minions.transforms,
			navMeshLocations = minions.navMeshLocations,
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
