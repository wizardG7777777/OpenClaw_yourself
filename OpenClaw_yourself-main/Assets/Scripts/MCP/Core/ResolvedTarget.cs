using UnityEngine;

namespace MCP.Core
{
    public enum TargetType { Entity, Point }

    public class ResolvedTarget
    {
        public TargetType Type { get; private set; }
        public string EntityId { get; private set; }
        public GameObject EntityObject { get; private set; }
        public Vector3 Position { get; private set; }

        public static ResolvedTarget FromEntity(string entityId, GameObject obj, Vector3 pos)
        {
            return new ResolvedTarget
            {
                Type = TargetType.Entity,
                EntityId = entityId,
                EntityObject = obj,
                Position = pos
            };
        }

        public static ResolvedTarget FromPoint(Vector3 pos)
        {
            return new ResolvedTarget
            {
                Type = TargetType.Point,
                Position = pos
            };
        }
    }
}
