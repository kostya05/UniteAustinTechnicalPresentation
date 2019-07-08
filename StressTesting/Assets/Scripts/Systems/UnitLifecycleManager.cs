using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Assets.Instancing.Skinning.Scripts.ECS;
using UnityEngine.Profiling;
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

[UpdateAfter(typeof(SpellSystem))]
public class UnitLifecycleManager : JobComponentSystem
{
	private EntityQuery unitsQuery;
	private EntityQuery dyingUnitsQuery;
	private EntityQuery dyingArrowsQuery;

	public NativeQueue<Entity> queueForKillingEntities;
	public NativeQueue<Entity> deathQueue;
	public NativeQueue<Entity> entitiesForFlying;

	public int MaxDyingUnitsPerFrame = 250;

	public NativeQueue<ArrowData> createdArrows;
	private const int CreatedArrowsQueueSize = 100000;

	private const int DeathQueueSize = 80000;

	private SpellSystem spellSystem;

	private Queue<Entity> entitiesThatNeedToBeKilled = new Queue<Entity>(100000);

	protected override unsafe void OnDestroyManager()
	{
		if (queueForKillingEntities.IsCreated) queueForKillingEntities.Dispose();
		if (deathQueue.IsCreated) deathQueue.Dispose();
		if (createdArrows.IsCreated) createdArrows.Dispose();
		if (entitiesForFlying.IsCreated) entitiesForFlying.Dispose();

		base.OnDestroyManager();
	}

	protected override void OnCreate()
	{
		base.OnCreate();
		spellSystem = World.GetOrCreateSystem<SpellSystem>();

		dyingArrowsQuery = GetEntityQuery(
			ComponentType.ReadOnly<DyingUnitData>(),
			ComponentType.ReadOnly<ArrowData>());

		dyingUnitsQuery = GetEntityQuery(
			ComponentType.ReadWrite<UnitTransformData>(),
			ComponentType.ReadOnly<DyingUnitData>());

		unitsQuery = GetEntityQuery(
			ComponentType.ReadOnly<MinionData>(),
			ComponentType.ReadOnly<MinionPathData>(),
			ComponentType.ReadOnly<TextureAnimatorData>(),
			ComponentType.ReadOnly<RigidbodyData>(),
			ComponentType.ReadOnly<MinionTarget>(),
			ComponentType.ReadOnly<UnitTransformData>());
		
		if (!queueForKillingEntities.IsCreated) queueForKillingEntities = new NativeQueue<Entity>(Allocator.Persistent);
		if (!entitiesForFlying.IsCreated) entitiesForFlying = new NativeQueue<Entity>(Allocator.Persistent);
	}
	
	private struct KillingEntitiesJob : IJobForEachWithEntity<DyingUnitData>
	{
		public float Time;
		public NativeQueue<Entity>.Concurrent queueForKillingEntities;

		public void Execute(Entity entity, int index, [ReadOnly]ref DyingUnitData unitData)
		{
			if (Time > unitData.TimeAtWhichToExpire)
			{
				queueForKillingEntities.Enqueue(entity);
			}
		}
	}
	
	protected override JobHandle OnUpdate(JobHandle inputDeps)
	{
		var length = unitsQuery.CalculateLength();
		if (length == 0) 
			return inputDeps;
		
		Profiler.BeginSample("Explosion wait");
		spellSystem.CombinedExplosionHandle.Complete(); // TODO try to remove this 
		Profiler.EndSample();
		inputDeps.Complete();
		Profiler.BeginSample("Spawn ");

		if (!deathQueue.IsCreated) deathQueue = new NativeQueue<Entity>(Allocator.Persistent);
		if (!createdArrows.IsCreated) createdArrows = new NativeQueue<ArrowData>(Allocator.Persistent);

		while (createdArrows.Count > 0)
		{
			var data = createdArrows.Dequeue();
			Spawner.Instance.SpawnArrow(data);
		}

		//UpdateInjectedComponentGroups();

		var cleanupJob = new CleanupJob
		{
			deathQueue = deathQueue.ToConcurrent()
		};

		var moveUnitsJob = new MoveUnitsBelowGround()
		{
			time = Time.time
		};

		var cleanupJobFence = cleanupJob.Schedule(unitsQuery, inputDeps);
		var moveUnitsBelowGroundFence = moveUnitsJob.Schedule(dyingUnitsQuery, spellSystem.CombinedExplosionHandle);

		Profiler.EndSample();

		cleanupJobFence.Complete();
		moveUnitsBelowGroundFence.Complete();

		Profiler.BeginSample("LifeCycleManager - Main Thread");

		float time = Time.time;

		var queue = queueForKillingEntities.ToConcurrent();
		
		var h1 = new KillingEntitiesJob
		{
			Time = time,
			queueForKillingEntities = queue
		}.Schedule(dyingUnitsQuery);
		
		var h2 = new KillingEntitiesJob
		{
			Time = time,
			queueForKillingEntities = queue
		}.Schedule(dyingArrowsQuery, h1);
		
		JobHandle.CompleteAll(ref h1, ref h2);

		Profiler.EndSample();
		Profiler.BeginSample("Queue processing");

		float timeForUnitExpiring = Time.time + 5f;
		float timeForArrowExpiring = Time.time + 1f;

		Profiler.BeginSample("Death queue");
		int processed = 0;
		while (deathQueue.Count > 0)
		{
			var entityToKill = deathQueue.Dequeue();
			if (EntityManager.HasComponent<MinionData>(entityToKill))
			{
				EntityManager.RemoveComponent<MinionData>(entityToKill);
				entitiesThatNeedToBeKilled.Enqueue(entityToKill);
			}

			if (EntityManager.HasComponent<ArrowData>(entityToKill))
			{
				EntityManager.AddComponentData(entityToKill, new DyingUnitData(timeForArrowExpiring, 0));
			}
		}
		Profiler.EndSample();

		Profiler.BeginSample("Explosion wait");
		spellSystem.CombinedExplosionHandle.Complete();
		Profiler.EndSample();

		Profiler.BeginSample("Killing minionEntities");
		// TODO try batched replacing 
		while (entitiesThatNeedToBeKilled.Count > 0 && processed < MaxDyingUnitsPerFrame)
		{
			processed++;
			var entityToKill = entitiesThatNeedToBeKilled.Dequeue();
			if (EntityManager.HasComponent<MinionTarget>(entityToKill))
			{
				EntityManager.RemoveComponent<MinionTarget>(entityToKill);
				if (EntityManager.HasComponent<AliveMinionData>(entityToKill)) EntityManager.RemoveComponent<AliveMinionData>(entityToKill);

				var textureAnimatorData = EntityManager.GetComponentData<TextureAnimatorData>(entityToKill);
				textureAnimatorData.NewAnimationId = AnimationName.Death;
				var transform = EntityManager.GetComponentData<UnitTransformData>(entityToKill);
				EntityManager.AddComponentData(entityToKill, new DyingUnitData(timeForUnitExpiring, transform.Position.y));

				EntityManager.SetComponentData(entityToKill, textureAnimatorData);

				var formations = GetComponentDataFromEntity<FormationData>();
				var formation = formations[transform.FormationEntity];
				formation.UnitCount--;
				formation.Width = (int)math.ceil((math.sqrt(formation.UnitCount / 2f) * 2f));
				if (formation.UnitCount == 0)
					formation.FormationState = FormationData.State.AllDead;
				formations[transform.FormationEntity] = formation;
			}
		}
		Profiler.EndSample();

		processed = 0;
		Profiler.BeginSample("Flying queue");
		while (entitiesForFlying.Count > 0 && processed < MaxDyingUnitsPerFrame)
		{
			processed++;
			var entity = entitiesForFlying.Dequeue();
			if (EntityManager.Exists(entity) && !EntityManager.HasComponent<FlyingData>(entity))
			{
				if (EntityManager.HasComponent(entity, typeof(AliveMinionData))) EntityManager.RemoveComponent<AliveMinionData>(entity);
				EntityManager.AddComponentData(entity, new FlyingData());
			}
		}
		Profiler.EndSample();

		Profiler.BeginSample("Destroying entities");
		while (queueForKillingEntities.Count > 0)
		{
			EntityManager.DestroyEntity(queueForKillingEntities.Dequeue());
		}
		Profiler.EndSample();

		Profiler.EndSample();
		return new JobHandle();
	}

	[BurstCompile]
	private struct CleanupJob : IJobForEachWithEntity<MinionData>
	{
		public NativeQueue<Entity>.Concurrent deathQueue;

		public void Execute(Entity entity, int index, [ReadOnly]ref MinionData minionData)
		{
			if(minionData.Health <= 0)
				deathQueue.Enqueue(entity);
		}
	}

	[BurstCompile]
	private struct MoveUnitsBelowGround : IJobForEach<DyingUnitData, UnitTransformData>
	{
		public float time;

		public void Execute([ReadOnly]ref DyingUnitData dyingData, ref UnitTransformData transform)
		{
			float t = (dyingData.TimeAtWhichToExpire - time) / 5f;
			t = math.clamp(t, 0, 1);
			transform.Position.y = math.lerp(dyingData.StartingYCoord - 0.8f, dyingData.StartingYCoord, t);
		}
	}
}
