using Unity.Entities;

namespace UnityEngine.Experimental.AI
{
    public struct PolygonIdBuffer : IBufferElementData
    {
        public static implicit operator PolygonId(PolygonIdBuffer e) { return e.Value; }
        public static implicit operator PolygonIdBuffer(PolygonId e) { return new PolygonIdBuffer { Value = e }; }

        public PolygonId Value;
    }
}