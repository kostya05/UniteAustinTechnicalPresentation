using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Entities;

public struct PrepareRaycasts : IJobParallelFor
{
	[ReadOnly]
	public NativeArray<UnitTransformData> transforms;

	public NativeArray<RaycastCommand> raycastCommands;

	public void Execute(int index)
	{
		var transform = transforms[index];
		raycastCommands [index] = RaycastHelper.CreateRaycastCommand (transform.Position);
	}
}
