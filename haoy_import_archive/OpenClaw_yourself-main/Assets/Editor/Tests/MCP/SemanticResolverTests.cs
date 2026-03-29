using NUnit.Framework;
using MCP.Entity;
using MCP.Core;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection;

namespace MCP.Tests.Entity
{
    /// <summary>
    /// Tests for SemanticResolver - the entity resolution and disambiguation system
    /// </summary>
    public class SemanticResolverTests
    {
        private EntityRegistry registry;
        private GameObject player;

        [SetUp]
        public void SetUp()
        {
            // Create EntityRegistry
            var registryGo = new GameObject("EntityRegistry");
            registry = registryGo.AddComponent<EntityRegistry>();
            BindRegistryInstance(registry);

            // Create a mock player
            player = new GameObject("Player");
            player.tag = "Player";
            player.transform.position = Vector3.zero;
        }

        [TearDown]
        public void TearDown()
        {
            if (registry != null)
                Object.DestroyImmediate(registry.gameObject);
            if (player != null)
                Object.DestroyImmediate(player);

            // Clean up any remaining entities
            var remaining = Object.FindObjectsByType<EntityIdentity>(FindObjectsSortMode.None);
            foreach (var e in remaining)
                Object.DestroyImmediate(e.gameObject);
        }

        private static void BindRegistryInstance(EntityRegistry instance)
        {
            typeof(EntityRegistry)
                .GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetSetMethod(true)
                ?.Invoke(null, new object[] { instance });
        }

        #region SemanticResolver Basic Tests

        [Test]
        public void SemanticResolver_EmptyQuery_ReturnsError()
        {
            // Act
            var result = SemanticResolver.ResolveTarget("", Vector3.zero);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual("TARGET_NOT_FOUND", result.ErrorCode);
        }

        [Test]
        public void SemanticResolver_NullQuery_ReturnsError()
        {
            // Act
            var result = SemanticResolver.ResolveTarget(null, Vector3.zero);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual("TARGET_NOT_FOUND", result.ErrorCode);
        }

        [Test]
        public void SemanticResolver_NoRegistry_ReturnsError()
        {
            // Arrange - destroy registry
            Object.DestroyImmediate(registry.gameObject);

            // Act
            var result = SemanticResolver.ResolveTarget("tv", Vector3.zero);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual("TARGET_NOT_FOUND", result.ErrorCode);
        }

        #endregion

        #region Exact ID Match Tests

        [Test]
        public void SemanticResolver_ExactIdMatch_ReturnsTarget()
        {
            // Arrange
            var tvGo = new GameObject("TV");
            tvGo.transform.position = new Vector3(5f, 0, 0);
            var tv = tvGo.AddComponent<EntityIdentity>();
            tv.entityId = "tv_living_room";
            tv.displayName = "客厅电视";
            registry.Register(tv);

            try
            {
                // Act - use exact entityId
                var result = SemanticResolver.ResolveTarget("tv_living_room", Vector3.zero);

                // Assert
                Assert.IsTrue(result.Success);
                Assert.IsNotNull(result.Target);
                Assert.AreEqual("tv_living_room", result.Target.EntityId);
                Assert.AreEqual(TargetType.Entity, result.Target.Type);
            }
            finally
            {
                Object.DestroyImmediate(tvGo);
            }
        }

        [Test]
        public void SemanticResolver_ExactIdMatch_WithTypeFilter_MatchingType_ReturnsTarget()
        {
            // Arrange
            var tvGo = new GameObject("TV");
            tvGo.transform.position = new Vector3(5f, 0, 0);
            var tv = tvGo.AddComponent<EntityIdentity>();
            tv.entityId = "tv_01";
            tv.displayName = "电视";
            tv.entityType = "appliance";
            registry.Register(tv);

            try
            {
                // Act - use exact ID with matching type filter
                var result = SemanticResolver.ResolveTarget("tv_01", Vector3.zero, entityTypeFilter: "appliance");

                // Assert
                Assert.IsTrue(result.Success);
                Assert.AreEqual("tv_01", result.Target.EntityId);
            }
            finally
            {
                Object.DestroyImmediate(tvGo);
            }
        }

        [Test]
        public void SemanticResolver_ExactIdMatch_WithTypeFilter_NonMatchingType_ReturnsError()
        {
            // Arrange
            var tvGo = new GameObject("TV");
            tvGo.transform.position = new Vector3(5f, 0, 0);
            var tv = tvGo.AddComponent<EntityIdentity>();
            tv.entityId = "tv_01";
            tv.displayName = "电视";
            tv.entityType = "appliance";
            registry.Register(tv);

            try
            {
                // Act - use exact ID with non-matching type filter
                var result = SemanticResolver.ResolveTarget("tv_01", Vector3.zero, entityTypeFilter: "furniture");

                // Assert
                Assert.IsFalse(result.Success);
                Assert.AreEqual("TARGET_NOT_FOUND", result.ErrorCode);
            }
            finally
            {
                Object.DestroyImmediate(tvGo);
            }
        }

        #endregion

        #region Semantic Search Tests

        [Test]
        public void SemanticResolver_SemanticSearch_SingleMatch_ReturnsTarget()
        {
            // Arrange
            var tvGo = new GameObject("TV");
            tvGo.transform.position = new Vector3(5f, 0, 0);
            var tv = tvGo.AddComponent<EntityIdentity>();
            tv.entityId = "tv_01";
            tv.displayName = "电视机";
            registry.Register(tv);

            try
            {
                // Act - search by displayName (Tier 1 semantic match)
                var result = SemanticResolver.ResolveTarget("电视机", Vector3.zero);

                // Assert
                Assert.IsTrue(result.Success);
                Assert.AreEqual("tv_01", result.Target.EntityId);
            }
            finally
            {
                Object.DestroyImmediate(tvGo);
            }
        }

        [Test]
        public void SemanticResolver_SemanticSearch_NoMatch_ReturnsNotFound()
        {
            // Arrange
            var tvGo = new GameObject("TV");
            var tv = tvGo.AddComponent<EntityIdentity>();
            tv.entityId = "tv_01";
            tv.displayName = "电视机";
            registry.Register(tv);

            try
            {
                // Act - search for something that doesn't exist
                var result = SemanticResolver.ResolveTarget("冰箱", Vector3.zero);

                // Assert
                Assert.IsFalse(result.Success);
                Assert.AreEqual("TARGET_NOT_FOUND", result.ErrorCode);
            }
            finally
            {
                Object.DestroyImmediate(tvGo);
            }
        }

        [Test]
        public void SemanticResolver_SemanticSearch_Ambiguous_ReturnsErrorWithCandidates()
        {
            // Arrange - create multiple doors
            var door1Go = new GameObject("Door1");
            door1Go.transform.position = new Vector3(2f, 0, 0);
            var door1 = door1Go.AddComponent<EntityIdentity>();
            door1.entityId = "door_bedroom";
            door1.displayName = "卧室门";
            door1.aliases = new[] { "门" };

            var door2Go = new GameObject("Door2");
            door2Go.transform.position = new Vector3(10f, 0, 0);
            var door2 = door2Go.AddComponent<EntityIdentity>();
            door2.entityId = "door_kitchen";
            door2.displayName = "厨房门";
            door2.aliases = new[] { "门" };

            registry.Register(door1);
            registry.Register(door2);

            try
            {
                // Act - search for ambiguous term
                var result = SemanticResolver.ResolveTarget("门", Vector3.zero);

                // Assert
                Assert.IsFalse(result.Success);
                Assert.AreEqual("AMBIGUOUS_TARGET", result.ErrorCode);
                Assert.IsNotNull(result.Candidates);
                Assert.AreEqual(2, result.Candidates.Count);

                // Verify candidates are sorted by distance
                Assert.AreEqual("door_bedroom", result.Candidates[0].EntityId);
                Assert.AreEqual("door_kitchen", result.Candidates[1].EntityId);
                Assert.Less(result.Candidates[0].Distance, result.Candidates[1].Distance);
            }
            finally
            {
                Object.DestroyImmediate(door1Go);
                Object.DestroyImmediate(door2Go);
            }
        }

        [Test]
        public void SemanticResolver_SemanticSearch_TierPriority_ExactIdBeatsDisplayName()
        {
            // Arrange - create entities where both would match at different tiers
            var tv1Go = new GameObject("TV1");
            tv1Go.transform.position = new Vector3(1f, 0, 0);
            var tv1 = tv1Go.AddComponent<EntityIdentity>();
            tv1.entityId = "tv_main"; // Would match "tv_main" exactly (Tier 1)
            tv1.displayName = "电视";

            var tv2Go = new GameObject("TV2");
            tv2Go.transform.position = new Vector3(2f, 0, 0);
            var tv2 = tv2Go.AddComponent<EntityIdentity>();
            tv2.entityId = "tv_secondary";
            tv2.displayName = "tv_main"; // Would also match "tv_main" as displayName (Tier 1)

            registry.Register(tv1);
            registry.Register(tv2);

            try
            {
                // Act
                var result = SemanticResolver.ResolveTarget("tv_main", Vector3.zero);

                // Assert - entityId match should win (it's checked first)
                Assert.IsTrue(result.Success);
                Assert.AreEqual("tv_main", result.Target.EntityId);
            }
            finally
            {
                Object.DestroyImmediate(tv1Go);
                Object.DestroyImmediate(tv2Go);
            }
        }

        [Test]
        public void SemanticResolver_SemanticSearch_AliasMatch_SingleResult_ReturnsTarget()
        {
            // Arrange
            var tvGo = new GameObject("TV");
            tvGo.transform.position = new Vector3(5f, 0, 0);
            var tv = tvGo.AddComponent<EntityIdentity>();
            tv.entityId = "tv_01";
            tv.displayName = "客厅电视机";
            tv.aliases = new[] { "TV", "电视" };
            registry.Register(tv);

            try
            {
                // Act - search by alias
                var result = SemanticResolver.ResolveTarget("TV", Vector3.zero);

                // Assert
                Assert.IsTrue(result.Success);
                Assert.AreEqual("tv_01", result.Target.EntityId);
            }
            finally
            {
                Object.DestroyImmediate(tvGo);
            }
        }

        [Test]
        public void SemanticResolver_SemanticSearch_ContainsMatch_SingleResult_ReturnsTarget()
        {
            // Arrange
            var tvGo = new GameObject("TV");
            tvGo.transform.position = new Vector3(5f, 0, 0);
            var tv = tvGo.AddComponent<EntityIdentity>();
            tv.entityId = "tv_01";
            tv.displayName = "客厅电视机";
            tv.aliases = new string[] { };
            registry.Register(tv);

            try
            {
                // Act - partial match in displayName
                var result = SemanticResolver.ResolveTarget("电视", Vector3.zero);

                // Assert
                Assert.IsTrue(result.Success);
                Assert.AreEqual("tv_01", result.Target.EntityId);
            }
            finally
            {
                Object.DestroyImmediate(tvGo);
            }
        }

        [Test]
        public void SemanticResolver_SemanticSearch_WithTypeFilter_ReducesMatches()
        {
            // Arrange
            var npcGo = new GameObject("NPC");
            npcGo.transform.position = new Vector3(2f, 0, 0);
            var npc = npcGo.AddComponent<EntityIdentity>();
            npc.entityId = "npc_01";
            npc.displayName = "小明";
            npc.entityType = "npc";

            var itemGo = new GameObject("Item");
            itemGo.transform.position = new Vector3(3f, 0, 0);
            var item = itemGo.AddComponent<EntityIdentity>();
            item.entityId = "item_01";
            item.displayName = "小明"; // Same display name
            item.entityType = "item";

            registry.Register(npc);
            registry.Register(item);

            try
            {
                // Act - with type filter
                var result = SemanticResolver.ResolveTarget("小明", Vector3.zero, entityTypeFilter: "npc");

                // Assert
                Assert.IsTrue(result.Success);
                Assert.AreEqual("npc_01", result.Target.EntityId);
            }
            finally
            {
                Object.DestroyImmediate(npcGo);
                Object.DestroyImmediate(itemGo);
            }
        }

        [Test]
        public void SemanticResolver_CandidateInfo_ContainsCorrectDistance()
        {
            // Arrange - create ambiguous entities at known distances
            var door1Go = new GameObject("Door1");
            door1Go.transform.position = new Vector3(3f, 0, 0); // Distance = 3
            var door1 = door1Go.AddComponent<EntityIdentity>();
            door1.entityId = "door_near";
            door1.displayName = "近门";
            door1.aliases = new[] { "门" };

            var door2Go = new GameObject("Door2");
            door2Go.transform.position = new Vector3(8f, 0, 0); // Distance = 8
            var door2 = door2Go.AddComponent<EntityIdentity>();
            door2.entityId = "door_far";
            door2.displayName = "远门";
            door2.aliases = new[] { "门" };

            registry.Register(door1);
            registry.Register(door2);

            try
            {
                // Act
                var result = SemanticResolver.ResolveTarget("门", Vector3.zero);

                // Assert
                Assert.IsFalse(result.Success);
                Assert.AreEqual(2, result.Candidates.Count);

                // Check distances are calculated correctly
                Assert.AreEqual("door_near", result.Candidates[0].EntityId);
                Assert.AreEqual(3f, result.Candidates[0].Distance, 0.01f);

                Assert.AreEqual("door_far", result.Candidates[1].EntityId);
                Assert.AreEqual(8f, result.Candidates[1].Distance, 0.01f);
            }
            finally
            {
                Object.DestroyImmediate(door1Go);
                Object.DestroyImmediate(door2Go);
            }
        }

        #endregion

        #region Case Insensitivity Tests

        [Test]
        public void SemanticResolver_SemanticSearch_IsCaseInsensitive()
        {
            // Arrange
            var tvGo = new GameObject("TV");
            tvGo.transform.position = new Vector3(5f, 0, 0);
            var tv = tvGo.AddComponent<EntityIdentity>();
            tv.entityId = "TV_LivingRoom";
            tv.displayName = "Living Room TV";
            registry.Register(tv);

            try
            {
                // Act - search with different casing
                var result1 = SemanticResolver.ResolveTarget("tv_livingroom", Vector3.zero);
                var result2 = SemanticResolver.ResolveTarget("TV_LIVINGROOM", Vector3.zero);
                var result3 = SemanticResolver.ResolveTarget("living room tv", Vector3.zero);

                // Assert - all should match
                Assert.IsTrue(result1.Success, "Lowercase search should work");
                Assert.IsTrue(result2.Success, "Uppercase search should work");
                Assert.IsTrue(result3.Success, "Mixed case search should work");
            }
            finally
            {
                Object.DestroyImmediate(tvGo);
            }
        }

        #endregion
    }
}
