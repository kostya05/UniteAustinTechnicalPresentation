using Unity.Entities;
using Unity.Mathematics;

namespace UnityEngine.Experimental.AI
{
    [InternalBufferCapacity(SimulationState.MaxPathSize)]
    public struct PathPoint : IBufferElementData
    {
        public static implicit operator float3(PathPoint e) { return e.Value; }
        public static implicit operator PathPoint(float3 e) { return new PathPoint { Value = e }; }
        
        public static implicit operator Vector3(PathPoint e) { return e.Value; }
        public static implicit operator PathPoint(Vector3 e) { return new PathPoint { Value = e }; }

        // Actual value each buffer element will store.
        public float3 Value;
    }
}