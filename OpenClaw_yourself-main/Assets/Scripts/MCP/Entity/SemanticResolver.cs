using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MCP.Core;

namespace MCP.Entity
{
    public static class SemanticResolver
    {
        public static ResolveResult ResolveTarget(string query, Vector3 playerPosition, string entityTypeFilter = null)
        {
            if (string.IsNullOrEmpty(query))
                return ResolveResult.Error("TARGET_NOT_FOUND", "Empty query. Use get_nearby_entities to discover available targets.");

            var registry = EntityRegistry.Instance;
            if (registry == null)
                return ResolveResult.Error("TARGET_NOT_FOUND", "EntityRegistry not available.");

            // Try exact ID lookup first
            var exact = registry.GetById(query);
            if (exact != null)
            {
                if (!string.IsNullOrEmpty(entityTypeFilter) &&
                    !string.Equals(exact.entityType, entityTypeFilter, System.StringComparison.OrdinalIgnoreCase))
                {
                    return ResolveResult.Error("TARGET_NOT_FOUND",
                        $"Entity '{query}' exists but is type '{exact.entityType}', not '{entityTypeFilter}'. Use get_nearby_entities to discover available targets.");
                }
                return ResolveResult.Ok(ResolvedTarget.FromEntity(exact.runtimeId ?? exact.entityId, exact.gameObject, exact.transform.position));
            }

            // Semantic search
            var candidates = registry.Search(query);

            // Filter by type if specified
            if (!string.IsNullOrEmpty(entityTypeFilter))
                candidates = candidates.Where(e => string.Equals(e.entityType, entityTypeFilter, System.StringComparison.OrdinalIgnoreCase)).ToList();

            if (candidates.Count == 0)
            {
                return ResolveResult.Error("TARGET_NOT_FOUND",
                    $"No entity found matching '{query}'. Use get_nearby_entities to discover available targets.");
            }

            if (candidates.Count == 1)
            {
                var c = candidates[0];
                return ResolveResult.Ok(ResolvedTarget.FromEntity(c.runtimeId ?? c.entityId, c.gameObject, c.transform.position));
            }

            // Ambiguous: multiple candidates — sort by distance and return info
            var infos = candidates
                .Select(e => new CandidateInfo
                {
                    EntityId = e.runtimeId ?? e.entityId,
                    DisplayName = e.displayName,
                    EntityType = e.entityType,
                    Distance = Vector3.Distance(playerPosition, e.transform.position)
                })
                .OrderBy(ci => ci.Distance)
                .ToList();

            string nameList = string.Join(", ", infos.Select(ci => $"'{ci.EntityId}'"));
            return ResolveResult.Error("AMBIGUOUS_TARGET",
                $"Multiple entities match '{query}': {nameList}. Please specify by entity_id.",
                infos);
        }
    }
}
