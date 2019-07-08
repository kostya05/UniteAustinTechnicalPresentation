using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Entities;

public struct MinionFlightJob : IJobParallelFor
{
	[DeallocateOnJobCompletion]
	public NativeArray<UnitTransformData> flyingUnits;
	[DeallocateOnJobCompletion]
	public NativeArray<TextureAnimatorData> textureAnimators;
	[DeallocateOnJobCompletion]
	public NativeArray<RigidbodyData> rigidbodies;
	[DeallocateOnJobCompletion]
	public NativeArray<MinionData> minionData;

	[ReadOnly]
	public NativeArray<RaycastHit> raycastHits;

	[ReadOnly]
	public float dt;

	public void Execute(int index)
	{
		var rigidbody = rigidbodies[index];
		var transform = flyingUnits[index];
		var textureAnimator = textureAnimators[index];

		textureAnimator.NewAnimationId = AnimationName.Falling;
		transform.Position = transform.Position + rigidbody.Velocity * dt;
		rigidbody.Velocity.y = rigidbody.Velocity.y + SimulationState.Gravity * dt;
		// add damping

		if (transform.Position.y < raycastHits[index].point.y)
		{
			var minion = minionData[index];
			minion.Health = 0;
			minionData[index] = minion;
		}

		rigidbodies[index] = rigidbody;
		flyingUnits[index] = transform;
		textureAnimators[index] = textureAnimator;
	}
}