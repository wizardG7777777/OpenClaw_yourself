using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MCP.Entity
{
    public class EntityRegistry : MonoBehaviour
    {
        public static EntityRegistry Instance { get; private set; }

        private Dictionary<string, EntityIdentity> entities = new Dictionary<string, EntityIdentity>();
        private Dictionary<string, int> suffixCounters = new Dictionary<string, int>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Fallback: pick up any EntityIdentity already active in the scene
            var existing = FindObjectsByType<EntityIdentity>(FindObjectsSortMode.None);
            foreach (var e in existing)
                Register(e);
        }

        private void OnEnable()
        {
            if (Instance == null || Instance == this)
                Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void Register(EntityIdentity entity)
        {
            if (entity == null || string.IsNullOrEmpty(entity.entityId))
                return;

            if (entity.autoSuffix)
            {
                string baseId = entity.entityId;
                if (!suffixCounters.ContainsKey(baseId))
                    suffixCounters[baseId] = 0;

                int suffix = suffixCounters[baseId];
                entity.runtimeId = $"{baseId}_{suffix}";
                suffixCounters[baseId] = suffix + 1;
            }
            else
            {
                entity.runtimeId = entity.entityId;
            }

            entities[entity.runtimeId] = entity;
        }

        public void Unregister(EntityIdentity entity)
        {
            if (entity == null)
                return;
            string key = entity.runtimeId ?? entity.entityId;
            if (string.IsNullOrEmpty(key))
                return;
            if (entities.TryGetValue(key, out var registered) && registered == entity)
                entities.Remove(key);
        }

        public EntityIdentity GetById(string entityId)
        {
            if (string.IsNullOrEmpty(entityId))
                return null;
            entities.TryGetValue(entityId, out var result);
            return result;
        }

        public List<EntityIdentity> Search(string query)
        {
            if (string.IsNullOrEmpty(query))
                return new List<EntityIdentity>();

            string q = query.ToLowerInvariant();
            var tier1 = new List<EntityIdentity>();
            var tier2 = new List<EntityIdentity>();
            var tier3 = new List<EntityIdentity>();

            foreach (var e in entities.Values)
            {
                // Tier 1: exact match on entityId or displayName
                string rid = e.runtimeId ?? e.entityId;
                if (rid.ToLowerInvariant() == q ||
                    (!string.IsNullOrEmpty(e.displayName) && e.displayName.ToLowerInvariant() == q))
                {
                    tier1.Add(e);
                    continue;
                }

                // Tier 2: exact match on any alias
                if (e.aliases != null && e.aliases.Any(a => a != null && a.ToLowerInvariant() == q))
                {
                    tier2.Add(e);
                    continue;
                }

                // Tier 3: contains match on displayName or any alias
                bool contains = (!string.IsNullOrEmpty(e.displayName) && e.displayName.ToLowerInvariant().Contains(q))
                    || (e.aliases != null && e.aliases.Any(a => a != null && a.ToLowerInvariant().Contains(q)));
                if (contains)
                    tier3.Add(e);
            }

            if (tier1.Count > 0) return tier1;
            if (tier2.Count > 0) return tier2;
            return tier3;
        }

        public List<EntityIdentity> GetNearby(Vector3 position, float radius, string[] entityTypes = null, bool interactableOnly = true)
        {
            float sqrRadius = radius * radius;
            var results = new List<(EntityIdentity entity, float dist)>();

            foreach (var e in entities.Values)
            {
                if (interactableOnly && !e.interactable)
                    continue;

                if (entityTypes != null && entityTypes.Length > 0)
                {
                    bool match = false;
                    for (int i = 0; i < entityTypes.Length; i++)
                    {
                        if (string.Equals(e.entityType, entityTypes[i], System.StringComparison.OrdinalIgnoreCase))
                        { match = true; break; }
                    }
                    if (!match) continue;
                }

                float sqrDist = (e.transform.position - position).sqrMagnitude;
                if (sqrDist <= sqrRadius)
                    results.Add((e, sqrDist));
            }

            results.Sort((a, b) => a.dist.CompareTo(b.dist));
            return results.Select(r => r.entity).ToList();
        }
    }
}
