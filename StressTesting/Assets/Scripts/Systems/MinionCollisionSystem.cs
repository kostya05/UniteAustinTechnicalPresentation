using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Entities;

[UpdateAfter(typeof(FormationIntegritySystem))]
public class MinionCollisionSystem : JobComponentSystem
{
	private MinionSystem minionSystem;

	NativeList<UnitTransformData> m_Transforms;
	NativeList<Entity> m_Entities;
	NativeList<MinionBitmask> m_Bitmasks;

	private EntityQuery minionsQuery;

	protected override void OnCreate()
	{
		minionSystem = World.GetOrCreateSystem<MinionSystem>();
		
		m_Transforms = new NativeList<UnitTransformData>(Allocator.Persistent);
		m_Entities = new NativeList<Entity>(Allocator.Persistent);
		m_Bitmasks = new NativeList<MinionBitmask>(Allocator.Persistent);

		minionsQuery = GetEntityQuery(
			ComponentType.ReadOnly<AliveMinionData>(),
			ComponentType.ReadOnly<UnitTransformData>(),
			ComponentType.ReadOnly<MinionBitmask>(),
			ComponentType.ReadWrite<RigidbodyData>(),
			ComponentType.ReadWrite<MinionAttackData>());
	}

	protected override void OnDestroyManager()
	{
		m_Transforms.Dispose();
		m_Entities.Dispose();
		m_Bitmasks.Dispose();
	}

	protected override JobHandle OnUpdate(JobHandle inputDeps)
	{
		var count = minionsQuery.CalculateLength();
		if (count == 0) 
			return inputDeps;

		m_Transforms.ResizeUninitialized(count);
		m_Entities.ResizeUninitialized(count);
		m_Bitmasks.ResizeUninitialized(count);
		
		var prepareCollision = new PrepareMinionCollisionJob
		{
			entitiesArray = m_Entities,
			transformsArray = m_Transforms,
			minionBitmaskArray = m_Bitmasks
		};
		inputDeps = prepareCollision.Schedule(minionsQuery, inputDeps);
		
		var collisionForceJob = new MinionCollisionJob
		{
			transforms = m_Transforms,
			buckets = minionSystem.CollisionBuckets,
			dt = Time.deltaTime,
			minionBitmask = m_Bitmasks,
			entities = m_Entities
		};

		var collisionJobFence = collisionForceJob.Schedule(minionsQuery, inputDeps);

		return collisionJobFence;
	}
}
