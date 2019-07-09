using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Unity.Entities;

[UpdateAfter(typeof(MinionSystem))]
public class ArrowSystem : JobComponentSystem
{
	public struct Arrows
	{
		public NativeArray<ArrowData> data;
		public NativeArray<Entity> entities;

		public int Length;

		public Arrows(EntityQuery entityQuery)
		{
			Length = entityQuery.CalculateLength();

			data = entityQuery.ToComponentDataArray<ArrowData>(Allocator.TempJob);
			entities = entityQuery.ToEntityArray(Allocator.TempJob);
		}

		public void Free()
		{
			data.Dispose();
			entities.Dispose();
		}
	}

	public struct Minions
	{
		[ReadOnly]
		public NativeArray<MinionBitmask> constData;
		[ReadOnly]
		public NativeArray<UnitTransformData> transforms;
		public NativeArray<Entity> entities;

		public int Length;

		public Minions(EntityQuery entityQuery)
		{
			Length = entityQuery.CalculateLength();

			constData = entityQuery.ToComponentDataArray<MinionBitmask>(Allocator.TempJob);
			transforms = entityQuery.ToComponentDataArray<UnitTransformData>(Allocator.TempJob);
			entities = entityQuery.ToEntityArray(Allocator.TempJob);
		}

		public void Free()
		{
			constData.Dispose();
			transforms.Dispose();
			entities.Dispose();
		}
	}

	private EntityQuery arrowsQuery;
	private EntityQuery minionsQuery;
	
	private MinionSystem minionSystem;

	private NativeArray<RaycastHit> raycastHits;
	private NativeArray<RaycastCommand> raycastCommands;

	private UnitLifecycleManager lifecycleManager;

	protected override void OnDestroyManager ()
	{
		base.OnDestroyManager ();
		if (raycastHits.IsCreated) raycastHits.Dispose();
		if (raycastCommands.IsCreated) raycastCommands.Dispose();
	}

	protected override void OnCreate()
	{
		lifecycleManager = World.GetOrCreateSystem<UnitLifecycleManager>();
		minionSystem = World.GetOrCreateSystem<MinionSystem>();

		arrowsQuery = GetEntityQuery(
			ComponentType.ReadOnly<AliveMinionData>(), 
			ComponentType.ReadOnly<MinionBitmask>(),
			ComponentType.ReadOnly<UnitTransformData>(),
			ComponentType.ReadWrite<ArrowData>());
		minionsQuery = GetEntityQuery(
			ComponentType.ReadOnly<AliveMinionData>(),
			ComponentType.ReadOnly<MinionBitmask>(), 
			ComponentType.ReadOnly<UnitTransformData>());
	}

	protected override JobHandle OnUpdate(JobHandle inputDeps)
	{
		if (minionSystem == null) 
			return inputDeps;

		var arrows = new Arrows(arrowsQuery);
		if (arrows.Length == 0)
		{
			arrows.Free();
			return inputDeps;
		}

		var minions = new Minions(minionsQuery);
		if (minions.Length == 0)
		{
			arrows.Free();
			minions.Free();
			return inputDeps;
		}

		// Update seems to be called after Play mode has been exited

		// ============ REALLOC ===============
		// todo fix nativearray
		NativeArrayExtensions.ResizeNativeArray(ref raycastHits, math.max(raycastHits.Length, arrows.Length));
		NativeArrayExtensions.ResizeNativeArray(ref raycastCommands, math.max(raycastCommands.Length, arrows.Length));

		// ============ JOB CREATION ===============

		var arrowJob = new ProgressArrowJob
		{
			raycastCommands = raycastCommands,
			arrows = arrows.data,
			arrowEntities = arrows.entities,
			dt = Time.deltaTime,
			allMinionTransforms = minions.transforms,
			buckets = minionSystem.CollisionBuckets,
			minionConstData = minions.constData,
			AttackCommands = CommandSystem.AttackCommandsConcurrent,
			minionEntities = minions.entities,
			queueForKillingEntities = lifecycleManager.queueForKillingEntities.ToConcurrent()
		};
			
		var stopArrowJob = new StopArrowsJob
		{
			raycastHits = raycastHits,
			arrows = arrows.data,
			arrowEntities = arrows.entities,
			stoppedArrowsQueue = lifecycleManager.deathQueue.ToConcurrent()
		};

		var arrowJobFence = arrowJob.Schedule(arrows.Length, SimulationState.SmallBatchSize, JobHandle.CombineDependencies(inputDeps, CommandSystem.AttackCommandsFence));
		arrowJobFence.Complete();
		var raycastJobFence = RaycastCommand.ScheduleBatch(raycastCommands, raycastHits, SimulationState.SmallBatchSize, arrowJobFence);
		var stopArrowJobFence = stopArrowJob.Schedule(arrows.Length, SimulationState.SmallBatchSize, raycastJobFence);

		CommandSystem.AttackCommandsConcurrentFence = JobHandle.CombineDependencies(stopArrowJobFence, CommandSystem.AttackCommandsConcurrentFence);
		// Complete arrow movement
		return stopArrowJobFence;
	}
	
	[BurstCompile]
	public struct StopArrowsJob : IJobParallelFor
	{
		[DeallocateOnJobCompletion]
		public NativeArray<ArrowData> arrows;
		[ReadOnly, DeallocateOnJobCompletion]
		public NativeArray<Entity> arrowEntities;

		[ReadOnly]
		public NativeArray<RaycastHit> raycastHits;

		public NativeQueue<Entity>.Concurrent stoppedArrowsQueue;

		public void Execute(int index)
		{
			if (arrows[index].active) 
			{
				var arrow = arrows[index];

				if (arrow.position.y <= raycastHits[index].point.y)
				{
					arrow.active = false;	
					arrows[index] = arrow;
					stoppedArrowsQueue.Enqueue (arrowEntities [index]);
				}
			}
		}
	}
}