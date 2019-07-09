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
	}

	protected override JobHandle OnUpdate(JobHandle inputDeps)
	{
		var count = formationsQuery.CalculateLength();
		if (count == 0) 
			return inputDeps;

		var formationUnitData = GetBufferFromEntity<EntityRef>(false);
		var cleanUnitsJob = new ClearUnitDataJob
		{
			formationUnitData = formationUnitData
		};
		
		var fillUnitJob = new FillUnitDataJob
		{ 
			formationUnitData = formationUnitData
		};
		
		var rearrangeJob = new RearrangeUnitIndexesJob
		{
			indicesInFormation = GetComponentDataFromEntity<IndexInFormationData>(),
			formationUnitData = formationUnitData
		};

		var cleanFence = cleanUnitsJob.Schedule(formationsQuery, inputDeps);
		var fillFence = fillUnitJob.Schedule(minionsQuery, cleanFence);
		var rearrangeFence = rearrangeJob.Schedule(formationsQuery, fillFence);

		return rearrangeFence;
	}

	[BurstCompile]
	private struct ClearUnitDataJob : IJobForEachWithEntity<FormationData>
	{
		[NativeDisableParallelForRestriction]
		public BufferFromEntity<EntityRef> formationUnitData;

		public void Execute([ReadOnly]Entity entity, int index, [ReadOnly]ref FormationData formationData)
		{
			var len = math.max(formationData.SpawnedCount, formationData.UnitCount);
			formationUnitData[entity].ResizeUninitialized(len);

			/*var unitData = formationUnitData[entity];
			unitData.Clear();
			
			for (var i = 0; i < len; i++)
				unitData.Add(new EntityRef());*/

			//unitData[i] = );
		}
	}

	[BurstCompile]
	private struct FillUnitDataJob : IJobForEachWithEntity_ECC<UnitTransformData, IndexInFormationData>
	{
		[NativeDisableParallelForRestriction]
		public BufferFromEntity<EntityRef> formationUnitData;

		public void Execute(Entity entity, int index, [ReadOnly]ref UnitTransformData transform, [ReadOnly]ref IndexInFormationData indexData)
		{
			var unitData = formationUnitData[transform.FormationEntity];
			unitData[indexData.IndexInFormation] = new EntityRef(entity);
		}
	}

	[BurstCompile]
	private struct RearrangeUnitIndexesJob : IJobForEachWithEntity<FormationData>
	{
		[NativeDisableParallelForRestriction]
		public ComponentDataFromEntity<IndexInFormationData> indicesInFormation;
		
		[NativeDisableParallelForRestriction]
		public BufferFromEntity<EntityRef> formationUnitData;
		
		public void Execute(Entity entity, int index, [ReadOnly]ref FormationData formation)
		{
			var unitData = formationUnitData[entity];
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
