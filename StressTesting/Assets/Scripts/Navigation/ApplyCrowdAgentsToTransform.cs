using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

[UpdateAfter(typeof(CrowdSystem))]
public class CrowdAgentsToTransformSystem : JobComponentSystem
{
    struct CrowdGroup
    {
        [ReadOnly]
        public NativeArray<CrowdAgent> agents;
        
        public TransformAccessArray agentTransforms;

        public CrowdGroup(EntityQuery crowdQuery)
        {
            agents = crowdQuery.ToComponentDataArray<CrowdAgent>(Allocator.TempJob);
            agentTransforms = crowdQuery.GetTransformAccessArray();
        }
    }

    EntityQuery m_CrowdQuery;

    protected override void OnCreate()
    {
        m_CrowdQuery = GetEntityQuery(ComponentType.ReadOnly<CrowdAgent>(),
            ComponentType.ReadOnly<WriteToTransformMarker>(), typeof(Transform));
    }

    struct WriteCrowdAgentsToTransformsJob : IJobParallelForTransform
    {
        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<CrowdAgent> crowdAgents;

        public void Execute(int index, TransformAccess transform)
        {
            var agent = crowdAgents[index];
            transform.position = agent.worldPosition;
            if (math.length(agent.velocity) > 0.1f)
                transform.rotation = Quaternion.LookRotation(agent.velocity);
        }
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        var m_Crowd = new CrowdGroup(m_CrowdQuery);
        WriteCrowdAgentsToTransformsJob writeJob;
        writeJob.crowdAgents = m_Crowd.agents;
        return writeJob.Schedule(m_Crowd.agentTransforms, inputDeps);
    }
}
