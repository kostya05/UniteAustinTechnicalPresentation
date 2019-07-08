using Unity.Entities;
using Unity.Mathematics;

namespace UnityEngine.Experimental.AI
{
    [InternalBufferCapacity(SimulationState.MaxPathSize)]
    public struct PathPoint : IBufferElementData
    {
        public static implicit operator float3(PathPoint e) { return e.Value; }
        public static implicit operator PathPoint(int e) { return new PathPoint { Value = e }; }

        // Actual value each buffer element will store.
        public float3 Value;
    }
}