using NUnit.Framework;
using MCP.Entity;
using MCP.Core;
using UnityEngine;
using System.Collections.Generic;

namespace MCP.Tests.Entity
{
    /// <summary>
    /// Tests for MCP Entity system (EntityRegistry, SemanticResolver, etc.)
    /// </summary>
    public class EntityTests
    {
        private EntityRegistry registry;

        [SetUp]
        public void SetUp()
        {
            // Create a new GameObject with EntityRegistry for each test
            var go = new GameObject("EntityRegistry");
            registry = go.AddComponent<EntityRegistry>();
        }

        [TearDown]
        public void TearDown()
        {
            if (registry != null)
            {
                Object.DestroyImmediate(registry.gameObject);
            }
            // Clean up any remaining test entities
            var remaining = Object.FindObjectsByType<EntityIdentity>(FindObjectsSortMode.None);
            foreach (var e in remaining)
            {
                Object.DestroyImmediate(e.gameObject);
            }
        }

        #region EntityRegistry Tests

        [Test]
        public void EntityRegistry_RegisterEntity_AddsToIndex()
        {
            // Arrange
            var entityGo = new GameObject("TV");
            var identity = entityGo.AddComponent<EntityIdentity>();
            identity.entityId = "tv_01";
            identity.displayName = "电视机";

            try
            {
                // Act - manually trigger registration
                registry.Register(identity);

                // Assert
                var found = registry.GetById("tv_01");
                Assert.IsNotNull(found, "Entity should be found in registry");
                Assert.AreEqual(identity, found, "Found entity should match registered entity");
            }
            finally
            {
                Object.DestroyImmediate(entityGo);
            }
        }

        [Test]
        public void EntityRegistry_UnregisterEntity_RemovesFromIndex()
        {
            // Arrange
            var entityGo = new GameObject("TV");
            var identity = entityGo.AddComponent<EntityIdentity>();
            identity.entityId = "tv_02";
            identity.displayName = "电视机";

            try
            {
                registry.Register(identity);
                Assert.IsNotNull(registry.GetById("tv_02"), "Entity should be registered");

                // Act
                registry.Unregister(identity);

                // Assert
                var found = registry.GetById("tv_02");
                Assert.IsNull(found, "Entity should be removed from registry");
            }
            finally
            {
                Object.DestroyImmediate(entityGo);
            }
        }

        [Test]
        public void EntityRegistry_GetById_NullOrEmpty_ReturnsNull()
        {
            // Act & Assert
            Assert.IsNull(registry.GetById(null), "Null ID should return null");
            Assert.IsNull(registry.GetById(""), "Empty ID should return null");
            Assert.IsNull(registry.GetById("   "), "Whitespace ID should return null");
        }

        [Test]
        public void EntityRegistry_Search_ExactMatchTier1()
        {
            // Arrange
            var tvGo = new GameObject("TV");
            var tv = tvGo.AddComponent<EntityIdentity>();
            tv.entityId = "tv_main";
            tv.displayName = "电视机";

            var doorGo = new GameObject("Door");
            var door = doorGo.AddComponent<EntityIdentity>();
            door.entityId = "door_main";
            door.displayName = "门";

            try
            {
                registry.Register(tv);
                registry.Register(door);

                // Act - search by entityId (Tier 1)
                var results = registry.Search("tv_main");

                // Assert
                Assert.AreEqual(1, results.Count, "Should find exactly one entity");
                Assert.AreEqual(tv, results[0], "Should find the TV");
            }
            finally
            {
                Object.DestroyImmediate(tvGo);
                Object.DestroyImmediate(doorGo);
            }
        }

        [Test]
        public void EntityRegistry_Search_ExactMatchDisplayNameTier1()
        {
            // Arrange
            var tvGo = new GameObject("TV");
            var tv = tvGo.AddComponent<EntityIdentity>();
            tv.entityId = "tv_01";
            tv.displayName = "电视机";

            try
            {
                registry.Register(tv);

                // Act - search by displayName (Tier 1)
                var results = registry.Search("电视机");

                // Assert
                Assert.AreEqual(1, results.Count);
                Assert.AreEqual(tv, results[0]);
            }
            finally
            {
                Object.DestroyImmediate(tvGo);
            }
        }

        [Test]
        public void EntityRegistry_Search_AliasMatchTier2()
        {
            // Arrange
            var tvGo = new GameObject("TV");
            var tv = tvGo.AddComponent<EntityIdentity>();
            tv.entityId = "tv_01";
            tv.displayName = "电视机";
            tv.aliases = new[] { "电视", "TV" };

            try
            {
                registry.Register(tv);

                // Act - search by alias (Tier 2)
                var results = registry.Search("TV");

                // Assert
                Assert.AreEqual(1, results.Count);
                Assert.AreEqual(tv, results[0]);
            }
            finally
            {
                Object.DestroyImmediate(tvGo);
            }
        }

        [Test]
        public void EntityRegistry_Search_ContainsMatchTier3()
        {
            // Arrange
            var tvGo = new GameObject("TV");
            var tv = tvGo.AddComponent<EntityIdentity>();
            tv.entityId = "tv_01";
            tv.displayName = "客厅电视机";
            tv.aliases = new string[] { };

            try
            {
                registry.Register(tv);

                // Act - partial match (Tier 3)
                var results = registry.Search("电视");

                // Assert
                Assert.AreEqual(1, results.Count);
                Assert.AreEqual(tv, results[0]);
            }
            finally
            {
                Object.DestroyImmediate(tvGo);
            }
        }

        [Test]
        public void EntityRegistry_Search_Priority_Tier1OverTier2()
        {
            // Arrange - create entities where Tier 1 and Tier 2 would both match
            var tv1Go = new GameObject("TV1");
            var tv1 = tv1Go.AddComponent<EntityIdentity>();
            tv1.entityId = "tv_01";
            tv1.displayName = "电视"; // Tier 1 match

            var tv2Go = new GameObject("TV2");
            var tv2 = tv2Go.AddComponent<EntityIdentity>();
            tv2.entityId = "tv_02";
            tv2.displayName = "卧室电视";
            tv2.aliases = new[] { "电视" }; // Tier 2 match

            try
            {
                registry.Register(tv1);
                registry.Register(tv2);

                // Act - search for "电视"
                var results = registry.Search("电视");

                // Assert - only Tier 1 matches should be returned
                Assert.AreEqual(1, results.Count, "Only Tier 1 match should be returned");
                Assert.AreEqual(tv1, results[0], "Should return Tier 1 match");
            }
            finally
            {
                Object.DestroyImmediate(tv1Go);
                Object.DestroyImmediate(tv2Go);
            }
        }

        [Test]
        public void EntityRegistry_Search_EmptyQuery_ReturnsEmptyList()
        {
            // Act
            var results = registry.Search("");
            var nullResults = registry.Search(null);

            // Assert
            Assert.AreEqual(0, results.Count, "Empty query should return empty list");
            Assert.AreEqual(0, nullResults.Count, "Null query should return empty list");
        }

        [Test]
        public void EntityRegistry_GetNearby_ReturnsSortedByDistance()
        {
            // Arrange
            var playerGo = new GameObject("Player");
            playerGo.transform.position = Vector3.zero;

            var nearGo = new GameObject("NearEntity");
            nearGo.transform.position = new Vector3(1f, 0, 0);
            var near = nearGo.AddComponent<EntityIdentity>();
            near.entityId = "near";
            near.displayName = "近处的物体";

            var farGo = new GameObject("FarEntity");
            farGo.transform.position = new Vector3(10f, 0, 0);
            var far = farGo.AddComponent<EntityIdentity>();
            far.entityId = "far";
            far.displayName = "远处的物体";

            try
            {
                registry.Register(near);
                registry.Register(far);

                // Act
                var results = registry.GetNearby(Vector3.zero, 20f);

                // Assert
                Assert.AreEqual(2, results.Count, "Should find both entities");
                Assert.AreEqual(near, results[0], "Closer entity should be first");
                Assert.AreEqual(far, results[1], "Farther entity should be second");
            }
            finally
            {
                Object.DestroyImmediate(playerGo);
                Object.DestroyImmediate(nearGo);
                Object.DestroyImmediate(farGo);
            }
        }

        [Test]
        public void EntityRegistry_GetNearby_RespectsRadius()
        {
            // Arrange
            var nearGo = new GameObject("NearEntity");
            nearGo.transform.position = new Vector3(1f, 0, 0);
            var near = nearGo.AddComponent<EntityIdentity>();
            near.entityId = "near";

            var farGo = new GameObject("FarEntity");
            farGo.transform.position = new Vector3(100f, 0, 0);
            var far = farGo.AddComponent<EntityIdentity>();
            far.entityId = "far";

            try
            {
                registry.Register(near);
                registry.Register(far);

                // Act
                var results = registry.GetNearby(Vector3.zero, 5f);

                // Assert
                Assert.AreEqual(1, results.Count, "Should only find near entity");
                Assert.AreEqual(near, results[0]);
            }
            finally
            {
                Object.DestroyImmediate(nearGo);
                Object.DestroyImmediate(farGo);
            }
        }

        [Test]
        public void EntityRegistry_GetNearby_RespectsInteractableOnly()
        {
            // Arrange
            var interactableGo = new GameObject("Interactable");
            interactableGo.transform.position = Vector3.zero;
            var interactable = interactableGo.AddComponent<EntityIdentity>();
            interactable.entityId = "interactive";
            interactable.interactable = true;

            var nonInteractableGo = new GameObject("NonInteractable");
            nonInteractableGo.transform.position = Vector3.zero;
            var nonInteractable = nonInteractableGo.AddComponent<EntityIdentity>();
            nonInteractable.entityId = "non_interactive";
            nonInteractable.interactable = false;

            try
            {
                registry.Register(interactable);
                registry.Register(nonInteractable);

                // Act - interactable only (default)
                var results = registry.GetNearby(Vector3.zero, 10f, interactableOnly: true);

                // Assert
                Assert.AreEqual(1, results.Count);
                Assert.AreEqual(interactable, results[0]);

                // Act - include non-interactable
                var allResults = registry.GetNearby(Vector3.zero, 10f, interactableOnly: false);
                Assert.AreEqual(2, allResults.Count);
            }
            finally
            {
                Object.DestroyImmediate(interactableGo);
                Object.DestroyImmediate(nonInteractableGo);
            }
        }

        [Test]
        public void EntityRegistry_GetNearby_FiltersByEntityType()
        {
            // Arrange
            var npcGo = new GameObject("NPC");
            npcGo.transform.position = Vector3.zero;
            var npc = npcGo.AddComponent<EntityIdentity>();
            npc.entityId = "npc_01";
            npc.entityType = "npc";

            var itemGo = new GameObject("Item");
            itemGo.transform.position = Vector3.zero;
            var item = itemGo.AddComponent<EntityIdentity>();
            item.entityId = "item_01";
            item.entityType = "item";

            try
            {
                registry.Register(npc);
                registry.Register(item);

                // Act
                var npcResults = registry.GetNearby(Vector3.zero, 10f, entityTypes: new[] { "npc" });

                // Assert
                Assert.AreEqual(1, npcResults.Count);
                Assert.AreEqual(npc, npcResults[0]);
            }
            finally
            {
                Object.DestroyImmediate(npcGo);
                Object.DestroyImmediate(itemGo);
            }
        }

        [Test]
        public void EntityRegistry_RegisterNull_DoesNotCrash()
        {
            // Act & Assert - should not throw
            Assert.DoesNotThrow(() => registry.Register(null));
            Assert.DoesNotThrow(() => registry.Unregister(null));
        }

        [Test]
        public void EntityRegistry_RegisterEmptyEntityId_DoesNotAdd()
        {
            // Arrange
            var go = new GameObject("Invalid");
            var identity = go.AddComponent<EntityIdentity>();
            identity.entityId = "";

            try
            {
                // Act
                registry.Register(identity);

                // Assert - search should find nothing
                var results = registry.Search("Invalid");
                Assert.AreEqual(0, results.Count);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        #endregion

        #region ResolveResult Tests

        [Test]
        public void ResolveResult_Ok_CreatesSuccessResult()
        {
            // Arrange
            var go = new GameObject("Target");
            var target = ResolvedTarget.FromEntity("entity_001", go, Vector3.one);

            try
            {
                // Act
                var result = ResolveResult.Ok(target);

                // Assert
                Assert.IsTrue(result.Success);
                Assert.AreEqual(target, result.Target);
                Assert.IsNull(result.ErrorCode);
                Assert.IsNull(result.Message);
                Assert.IsNull(result.Candidates);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ResolveResult_Error_CreatesErrorResult()
        {
            // Act
            var result = ResolveResult.Error("TARGET_NOT_FOUND", "No entity found");

            // Assert
            Assert.IsFalse(result.Success);
            Assert.IsNull(result.Target);
            Assert.AreEqual("TARGET_NOT_FOUND", result.ErrorCode);
            Assert.AreEqual("No entity found", result.Message);
            Assert.IsNull(result.Candidates);
        }

        [Test]
        public void ResolveResult_Error_WithCandidates_IncludesThem()
        {
            // Arrange
            var candidates = new List<CandidateInfo>
            {
                new CandidateInfo { EntityId = "door_1", DisplayName = "前门", Distance = 2f },
                new CandidateInfo { EntityId = "door_2", DisplayName = "后门", Distance = 5f }
            };

            // Act
            var result = ResolveResult.Error("AMBIGUOUS_TARGET", "Multiple doors found", candidates);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.IsNotNull(result.Candidates);
            Assert.AreEqual(2, result.Candidates.Count);
        }

        #endregion

        #region CandidateInfo Tests

        [Test]
        public void CandidateInfo_CanStoreAllFields()
        {
            // Arrange & Act
            var candidate = new CandidateInfo
            {
                EntityId = "door_001",
                DisplayName = "卧室门",
                EntityType = "furniture",
                Distance = 3.5f
            };

            // Assert
            Assert.AreEqual("door_001", candidate.EntityId);
            Assert.AreEqual("卧室门", candidate.DisplayName);
            Assert.AreEqual("furniture", candidate.EntityType);
            Assert.AreEqual(3.5f, candidate.Distance);
        }

        #endregion
    }
}
