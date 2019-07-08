using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Unity.Entities;
using UnityEngine.Profiling;

// This system should depend on the crowd system, but as systems can depend only on one thing, then all the formation
// systems will execute before the prepare buckets system, which is bad. That is why this system depends on the buckets
// system, to ensure the buckets system begins execution.
[UpdateAfter(typeof(PrepareBucketsSystem))]
public class FormationSystem : JobComponentSystem
{
	public struct Formations
	{
		public NativeArray<FormationData> data;
		//public FixedArrayArray<EntityRef> unitData;
		public NativeArray<FormationNavigationData> navigationData;
		public NativeArray<FormationClosestData> closestFormations;
		public NativeArray<CrowdAgentNavigator> navigators;
		public NativeArray<CrowdAgent> agents;
		public NativeArray<FormationHighLevelPath> highLevelPaths;
		public NativeArray<Entity> entities;

		public int Length;

		public Formations(EntityQuery entityQuery) : this()
		{
			Length = entityQuery.CalculateLength();
			if(Length == 0)
				return;
			
			entities = entityQuery.ToEntityArray(Allocator.TempJob);
			navigationData = entityQuery.ToComponentDataArray<FormationNavigationData>(Allocator.TempJob);
			closestFormations = entityQuery.ToComponentDataArray<FormationClosestData>(Allocator.TempJob);
			data = entityQuery.ToComponentDataArray<FormationData>(Allocator.TempJob);
			agents = entityQuery.ToComponentDataArray<CrowdAgent>(Allocator.TempJob);
			navigators = entityQuery.ToComponentDataArray<CrowdAgentNavigator>(Allocator.TempJob);
			highLevelPaths = entityQuery.ToComponentDataArray<FormationHighLevelPath>(Allocator.TempJob);
		}
	}
	
	private struct DeallocateFormations : IJob
	{
		[DeallocateOnJobCompletion]
		public NativeArray<FormationData> data;
		[DeallocateOnJobCompletion]
		public NativeArray<FormationNavigationData> navigationData;
		[DeallocateOnJobCompletion]
		public NativeArray<FormationClosestData> closestFormations;
		[DeallocateOnJobCompletion]
		public NativeArray<CrowdAgentNavigator> navigators;
		[DeallocateOnJobCompletion]
		public NativeArray<CrowdAgent> agents;
		[DeallocateOnJobCompletion]
		public NativeArray<FormationHighLevelPath> highLevelPaths;
		[DeallocateOnJobCompletion]
		public NativeArray<Entity> entities;
		
		public void Execute()
		{
			
		}
	}

	private EntityQuery formationsQuery;

	//[Inject]
	//private ComponentDataFromEntity<IndexInFormationData> indicesInFormation;


	//public static readonly int GroundLayermask = 1 << LayerMask.NameToLayer("Ground");
	public const int GroundLayermask = 1 << 8;

	protected override void OnCreate()
	{
		formationsQuery = GetEntityQuery(
			ComponentType.ReadWrite<FormationData>(),
			ComponentType.ReadWrite<FormationNavigationData>(),
			ComponentType.ReadWrite<FormationClosestData>(),
			ComponentType.ReadWrite<CrowdAgentNavigator>(),
			ComponentType.ReadWrite<CrowdAgent>(),
			ComponentType.ReadWrite<FormationHighLevelPath>(),
			ComponentType.ChunkComponent<EntityRef>());
	}

	protected override JobHandle OnUpdate(JobHandle inputDeps)
	{
		// Realloc();
		var formations = new Formations(formationsQuery);
		if (formations.Length == 0) 
			return inputDeps;
		
		//NativeArrayExtensions.ResizeNativeArray(ref raycastHits, math.max(raycastHits.Length,minions.Length));
		//NativeArrayExtensions.ResizeNativeArray(ref raycastCommands, math.max(raycastCommands.Length, minions.Length));
		
		var copyNavigationJob = new CopyNavigationPositionToFormation
		{
			formations = formations.data,
			agents = formations.agents,
			navigators = formations.navigators,
			navigationData = formations.navigationData,
			dt = Time.deltaTime
		};
		var copyNavigationJobHandle = copyNavigationJob.Schedule(formations.Length, SimulationState.SmallBatchSize, inputDeps);

		var copyFormations = formations.data;
		var copyFormationEntities = formations.entities;
		
		var closestSearchJob = new SearchClosestFormations
		{
			formations = copyFormations,
			closestFormations = formations.closestFormations,
			formationEntities = copyFormationEntities
		};
		var closestSearchJobHandle = closestSearchJob.Schedule(formations.Length, SimulationState.HugeBatchSize, copyNavigationJobHandle);
		
		var updateFormationsJob = new UpdateFormations
		{
			closestFormations = formations.closestFormations,
			formationNavigators = formations.navigators,
			formationHighLevelPath = formations.highLevelPaths,
			formations = formations.data,
		};
		var updateFormationsJobHandle = updateFormationsJob.Schedule(formations.Length, SimulationState.SmallBatchSize, closestSearchJobHandle);
		
		// Pass two, rearrangeing the minion indices
		// TODO Split this system into systems that make sense 
		
		// calculate formation movement
		// advance formations
		// calculate minion position and populate the array
		
		return new DeallocateFormations
		{
			agents = formations.agents,
			data = formations.data,
			entities = formations.entities,
			navigators = formations.navigators,
			closestFormations = formations.closestFormations,
			navigationData = formations.navigationData,
			highLevelPaths = formations.highLevelPaths
		}.Schedule(updateFormationsJobHandle);
	}
	
	[BurstCompile]
	private struct UpdateFormations : IJobParallelFor
	{
		public NativeArray<FormationData> formations;
		[ReadOnly] public NativeArray<FormationClosestData> closestFormations;
		[ReadOnly] public NativeArray<FormationHighLevelPath> formationHighLevelPath;

		public NativeArray<CrowdAgentNavigator> formationNavigators;

		public void Execute(int index)
		{
			var navigator = formationNavigators[index];

			var formation = formations[index];

#if DEBUG_CROWDSYSTEM && !ENABLE_HLVM_COMPILER
			Debug.Assert(navigator.active || formation.FormationState == FormationData.State.AllDead);
#endif

			if (formation.FormationState == FormationData.State.AllDead)
			{
				if (navigator.active)
				{
					navigator.active = false;
					formationNavigators[index] = navigator;
				}
				return;
			}

			float3 targetPosition = formationNavigators[index].requestedDestination;
			bool foundTargetPosition = false;
			if (closestFormations[index].closestFormation != new Entity())
			{
				var closestPosition = closestFormations[index].closestFormationPosition;

				// Aggro distance of 75
				if (formation.EnableAgro && math.distance(closestPosition, formation.Position) < 75)
				{
					targetPosition = closestPosition;
					foundTargetPosition = true;
				}
			}

			if (!foundTargetPosition && formation.EnableHighLevelPath)
			{
				int nextPathIndex = formation.HighLevelPathIndex;

				do
				{
					targetPosition = formationHighLevelPath[index].GetTarget(nextPathIndex);
					nextPathIndex++;
				}
				while (math.distance(targetPosition.xz, formation.Position.xz) < 0.1f && nextPathIndex <= 3);
				formation.HighLevelPathIndex = nextPathIndex - 1;
			}

			if (math.distance(formationNavigators[index].requestedDestination.xz, targetPosition.xz) > 0.1f)
			{
				navigator.MoveTo(targetPosition);
			}

			formationNavigators[index] = navigator;
			formations[index] = formation;
		}
	}

	[BurstCompile]
	private struct SearchClosestFormations : IJobParallelFor
	{
		[DeallocateOnJobCompletion] [ReadOnly] public NativeArray<FormationData> formations;
		[DeallocateOnJobCompletion] [ReadOnly] public NativeArray<Entity> formationEntities;
		public NativeArray<FormationClosestData> closestFormations;

		public void Execute(int index)
		{
			if (formations[index].FormationState == FormationData.State.AllDead) return;
			var data = closestFormations[index];
			float d = float.PositiveInfinity;
			int closestIndex = -1;

			for (int i = 0; i < formations.Length; i++)
			{
				if (!(i == index || formations[i].FormationState == FormationData.State.AllDead || formations[i].IsFriendly == formations[index].IsFriendly))
				{
					float3 relative = formations[index].Position - formations[i].Position;
					float newD = math.dot(relative, relative);

					if (newD < d)
					{
						d = newD;
						closestIndex = i;
					}
				}
			}

			if(closestIndex != -1) data.closestFormation = formationEntities[closestIndex]; else data.closestFormation = new Entity();
			if (closestIndex != -1) data.closestFormationPosition = formations[closestIndex].Position;

			closestFormations[index] = data;
		}
	}

	[BurstCompile]
	private struct CopyNavigationPositionToFormation : IJobParallelFor
	{
		public NativeArray<FormationData> formations;
		[ReadOnly]
		public NativeArray<CrowdAgent> agents;
		[ReadOnly]
		public NativeArray<CrowdAgentNavigator> navigators;
		public NativeArray<FormationNavigationData> navigationData;
		[ReadOnly]
		public float dt;

		public void Execute(int index)
		{
			var formation = formations[index];
			var prevPosition = formation.Position;

			formation.Position = agents[index].worldPosition;

			var forward = formation.Position - prevPosition;
			forward.y = 0;

			// If we are moving we should assign a new forward vector.
			if (!MathUtil.Approximately(math.dot(forward, forward), 0))
			{
				forward = math.normalize(forward);
				formation.Forward = Vector3.RotateTowards(formation.Forward, forward, 0.314f * dt, 1);
			}

			var navData = navigationData[index];

			var targetRelative = navData.TargetPosition - navigators[index].steeringTarget;
			if (!MathUtil.Approximately(math.dot(targetRelative, targetRelative), 0))
			{
				// We got a next corner
				navData.initialCornerDistance = math.length(formation.Position - navigators[index].steeringTarget);
				navData.prevFormationSide = formation.formationSide;
				navData.TargetPosition = navigators[index].steeringTarget;

				navigationData[index] = navData;
			}

			if (navData.initialCornerDistance != 0) formation.formationSide = math.lerp(navData.prevFormationSide, navigators[index].nextCornerSide,
																math.clamp(1 - math.length(formation.Position - navData.TargetPosition) / navData.initialCornerDistance, 0, 1));


			formations[index] = formation;
		}
	}
}