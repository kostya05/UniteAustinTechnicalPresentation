using Unity.Entities;
using Unity.Mathematics;

namespace UnityEngine.Experimental.AI
{
    [InternalBufferCapacity(SimulationState.MaxPathSize)]
    public struct PathElement : IBufferElementData
    {
        public static implicit operator float3(PathElement e) { return e.Value; }
        public static implicit operator PathElement(float3 e) { return new PathElement { Value = e }; }
        
        public static implicit operator Vector3(PathElement e) { return e.Value; }
        public static implicit operator PathElement(Vector3 e) { return new PathElement { Value = e }; }

        // Actual value each buffer element will store.
        public float3 Value;
    }
}