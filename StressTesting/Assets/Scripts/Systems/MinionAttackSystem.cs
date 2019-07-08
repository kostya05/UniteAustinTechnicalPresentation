using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Entities;

[UpdateAfter(typeof(ArcherMinionSystem))]
public class MinionAttackSystem : JobComponentSystem
{
	protected override void OnCreate()
	{
		query = GetEntityQuery(ComponentType.ReadOnly<AliveMinionData>(),
			ComponentType.ReadWrite<MinionData>(),
			ComponentType.ReadOnly<UnitTransformData>(),
			ComponentType.ReadWrite<MinionAttackData>());
	}

	private EntityQuery query;

	protected override JobHandle OnUpdate(JobHandle inputDeps)
	{
		var length = query.CalculateLength();
		if (length== 0) return inputDeps;

		
		var attackTargetJob = new AttackTargetJob
		{
			dt = Time.deltaTime,
			AttackCommands = CommandSystem.AttackCommandsConcurrent,
		};

		var attackJobFence = attackTargetJob.Schedule(query, JobHandle.CombineDependencies(inputDeps, CommandSystem.AttackCommandsFence));
		CommandSystem.AttackCommandsConcurrentFence = JobHandle.CombineDependencies(attackJobFence, CommandSystem.AttackCommandsConcurrentFence);

		return attackJobFence;
	}
}
