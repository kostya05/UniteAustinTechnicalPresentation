using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

[BurstCompile]
public struct AttackTargetJob : IJobForEachWithEntity<MinionData, MinionAttackData, UnitTransformData>
{
	[ReadOnly]
	public float dt;

	public NativeQueue<AttackCommand>.Concurrent AttackCommands;

	public void Execute(Entity entity, int index, ref MinionData minion, ref MinionAttackData minionAttack, [ReadOnly]ref UnitTransformData transformData)
	{

		if (transformData.UnitType == 2)
		{
			return;
		}
		if (minion.attackCycle < 0)
		{
			if (minionAttack.targetEntity == new Entity()) return;
			minion.attackCycle = 0;
		}

		var prevAttackCycle = minion.attackCycle;

		if (minion.attackCycle + dt >= MinionData.HitTime && prevAttackCycle < MinionData.HitTime)
		{
			AttackCommands.Enqueue(new AttackCommand(entity, minionAttack.targetEntity, 25));
		}
		minion.attackCycle += dt;
		if (minion.attackCycle > MinionData.AttackTime)
		{
			if (minionAttack.targetEntity == new Entity()) 
				minion.attackCycle = -1;
			else 
				minion.attackCycle -= MinionData.AttackTime;
		}
	}
}
