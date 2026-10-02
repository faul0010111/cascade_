using Cascade.Simulation;
using UnityEngine;

namespace Cascade.Runtime
{
    /// <summary>
    /// Physics-based line of sight. Walls and closed door panels block. Agents live on the Ignore Raycast layer, so
    /// people never block each other's view (a simplification). A hit very close to the target counts as visible,
    /// so an agent can see a closed door itself.
    /// </summary>
    public sealed class UnityLineOfSight : ILineOfSight
    {
        public int Mask = Physics.DefaultRaycastLayers;

        public bool Check(Vector3 from, Vector3 to)
        {
            RaycastHit hit;
            if (!Physics.Linecast(from, to, out hit, Mask, QueryTriggerInteraction.Collide)) return true;
            return Vector3.Distance(hit.point, to) < 0.6f;
        }
    }
}
