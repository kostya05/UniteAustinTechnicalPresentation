using Unity.Entities;

namespace UnityEngine.Experimental.AI
{
    public struct PolygonIdBufferElement : IBufferElementData
    {
        public static implicit operator PolygonId(PolygonIdBufferElement e) { return e.Value; }
        public static implicit operator PolygonIdBufferElement(PolygonId e) { return new PolygonIdBufferElement { Value = e }; }

        public PolygonId Value;
    }
}