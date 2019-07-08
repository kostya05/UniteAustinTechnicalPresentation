using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Unity.Entities;
using UnityEngine.Profiling;

[UpdateAfter(typeof(CrowdSystem))]
public class PrepareBucketsSystem : JobComponentSystem
{
	private MinionSystem minionSystem;
	private EntityQuery query;

	public struct Minions
	{
		[ReadOnly]
		public NativeArray<UnitTransformData> transforms;
		[ReadOnly]
		public NativeArray<MinionBitmask> bitmask;

		public int Length;

		public Minions(EntityQuery entityQuery, ref JobHandle inputDeps) : this()
		{
			Length = entityQuery.CalculateLength();

			if (Length == 0)
				return;
			
			transforms = entityQuery.ToComponentDataArray<UnitTransformData>(Allocator.TempJob, out var h1);
			bitmask = entityQuery.ToComponentDataArray<MinionBitmask>(Allocator.TempJob, out var h2);
			
			inputDeps = JobHandle.CombineDependencies(inputDeps, h1, h2);
		}
	}

	protected override void OnCreate()
	{
		base.OnCreate();
		minionSystem = World.GetOrCreateSystem<MinionSystem>();
		query = GetEntityQuery(ComponentType.ReadOnly<AliveMinionData>(), ComponentType.ReadOnly<UnitTransformData>(),
			ComponentType.ReadOnly<MinionBitmask>());
	}

	protected override JobHandle OnUpdate(JobHandle inputDeps)
	{
		var minions = new Minions(query, ref inputDeps);
		if (minions.Length == 0)
			return inputDeps;

		Profiler.BeginSample("Clearing buckets");

		if (!minionSystem.CollisionBuckets.IsCreated) 
			minionSystem.CollisionBuckets = new NativeMultiHashMap<int, int>(minions.Length, Allocator.Persistent);
		else 
			minionSystem.CollisionBuckets.Clear();

		// realloc if needed
		minionSystem.CollisionBuckets.Capacity = math.max(minionSystem.CollisionBuckets.Capacity, minions.Length);

		Profiler.EndSample();

		var prepareBucketsJob = new PrepareBucketsJob
		{
			transforms = minions.transforms,
			buckets = minionSystem.CollisionBuckets,
			minionBitmask = minions.bitmask
		};

		var PrepareBucketsFence = prepareBucketsJob.Schedule(inputDeps);
		return PrepareBucketsFence;
	}
}