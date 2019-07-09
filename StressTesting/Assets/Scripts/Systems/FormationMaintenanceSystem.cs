using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Entities;


[UpdateAfter(typeof(FormationSystem))]
public class FormationMaintenanceSystem : JobComponentSystem
{
	private EntityQuery formationsQuery;
	private EntityQuery minionsQuery;

	protected override void OnCreate()
	{
		minionsQuery = GetEntityQuery(
			ComponentType.ReadOnly<AliveMinionData>(),
			ComponentType.ReadOnly<UnitTransformData>(),
			ComponentType.ReadOnly<IndexInFormationData>());

		var components = FormationSystem.GetFormationQueryTypes();
		formationsQuery = GetEntityQuery(components);
		components.Dispose();
	}

	protected override JobHandle OnUpdate(JobHandle inputDeps)
	{
		var count = formationsQuery.CalculateLength();
		if (count == 0) 
			return inputDeps;

		var cleanUnitsJob = new ClearUnitDataJob();
		
		var fillUnitJob = new FillUnitDataJob
		{ 
			formationUnitData = GetBufferFromEntity<EntityRef>()
		};
		
		var rearrangeJob = new RearrangeUnitIndexesJob
		{
			indicesInFormation = GetComponentDataFromEntity<IndexInFormationData>()
		};

		var cleanFence = cleanUnitsJob.Schedule(formationsQuery, inputDeps);
		var fillFence = fillUnitJob.Schedule(minionsQuery, cleanFence);
		var rearrangeFence = rearrangeJob.Schedule(formationsQuery, fillFence);

		return rearrangeFence;
	}

	[BurstCompile]
	private struct ClearUnitDataJob : IJobForEach_BC<EntityRef, FormationData>
	{
		public void Execute(DynamicBuffer<EntityRef> unitData, [ReadOnly]ref FormationData formationData)
		{
			var len = math.max(formationData.SpawnedCount, formationData.UnitCount);

			for (var i = 0; i < len; i++)
				unitData[i] = new EntityRef();
		}
	}

	[BurstCompile]
	private struct FillUnitDataJob : IJobForEachWithEntity_ECC<UnitTransformData, IndexInFormationData>
	{
		public BufferFromEntity<EntityRef> formationUnitData;

		public void Execute(Entity entity, int index, [ReadOnly]ref UnitTransformData transform, [ReadOnly]ref IndexInFormationData indexData)
		{
			var unitData = formationUnitData[transform.FormationEntity];
			unitData[indexData.IndexInFormation] = new EntityRef(entity);
		}
	}

	[BurstCompile]
	private struct RearrangeUnitIndexesJob : IJobForEach_BC<EntityRef, FormationData>
	{
		[NativeDisableParallelForRestriction]
		public ComponentDataFromEntity<IndexInFormationData> indicesInFormation;
		public void Execute(DynamicBuffer<EntityRef> unitData, [ReadOnly]ref FormationData formation)
		{
			var len = math.max(formation.SpawnedCount, formation.UnitCount);
			for (var i = 0; i < len; i++)
			{
				if (unitData[i].entity != new Entity()) 
					continue;
				
				// Find a suitable index
				int j;
				for (j = i + 1; j < len; j++)
				{
					if (unitData[j].entity == new Entity()) 
						continue;
					
					// We got an index. Replace
					unitData[i] = unitData[j];
					var t = indicesInFormation[unitData[j].entity];
					t.IndexInFormation = i;
					indicesInFormation[unitData[j].entity] = t;

					break;
				}

				// No available indexes till the end
				if (j == len) 
					break;
			}
		}
	}
}
