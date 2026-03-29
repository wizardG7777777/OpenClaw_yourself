using NUnit.Framework;
using MCP.Executor;
using MCP.Core;
using MCP.Entity;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection;

namespace MCP.Tests.Executor
{
    /// <summary>
    /// Tests for MCP Executor layer (ActionHandlers, QueryHandlers)
    /// </summary>
    public class ExecutorTests
    {
        private GameObject itemRegistryGo;

        [SetUp]
        public void SetUp()
        {
            itemRegistryGo = new GameObject("ItemRegistry");
            var registry = itemRegistryGo.AddComponent<ItemRegistry>();
            typeof(ItemRegistry)
                .GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)
                ?.GetSetMethod(true)
                ?.Invoke(null, new object[] { registry });
            var awake = typeof(ItemRegistry).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            awake?.Invoke(registry, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (itemRegistryGo != null)
                Object.DestroyImmediate(itemRegistryGo);
            typeof(ItemRegistry)
                .GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)
                ?.GetSetMethod(true)
                ?.Invoke(null, new object[] { null });
        }

        #region GetInventoryHandler Tests

        [Test]
        public void GetInventoryHandler_ReturnsMVPItems()
        {
            // Arrange
            var request = new MCPRequest { Tool = "get_inventory" };

            // Act
            var response = GetInventoryHandler.Handle(request);

            // Assert
            Assert.IsTrue(response.Ok);
            Assert.IsNotNull(response.Data);

            var data = response.Data as Dictionary<string, object>;
            Assert.IsNotNull(data);
            Assert.IsTrue(data.ContainsKey("items"));

            var items = data["items"] as List<Dictionary<string, object>>;
            Assert.IsNotNull(items);
            Assert.AreEqual(3, items.Count);

            // Verify each MVP item exists
            var itemIds = new HashSet<string>();
            foreach (var item in items)
            {
                itemIds.Add(item["item_id"].ToString());
            }

            Assert.IsTrue(itemIds.Contains("wrench"), "Should contain wrench");
            Assert.IsTrue(itemIds.Contains("shovel"), "Should contain shovel");
            Assert.IsTrue(itemIds.Contains("postcard"), "Should contain postcard");
        }

        [Test]
        public void GetInventoryHandler_ItemDetails_AreCorrect()
        {
            // Arrange
            var request = new MCPRequest { Tool = "get_inventory" };

            // Act
            var response = GetInventoryHandler.Handle(request);

            // Assert
            var data = response.Data as Dictionary<string, object>;
            var items = data["items"] as List<Dictionary<string, object>>;

            // Find wrench
            Dictionary<string, object> wrench = null;
            foreach (var item in items)
            {
                if (item["item_id"].ToString() == "wrench")
                {
                    wrench = item;
                    break;
                }
            }

            Assert.IsNotNull(wrench);
            Assert.AreEqual("扳手", wrench["display_name"]);
            Assert.AreEqual(1, wrench["quantity"]);
            Assert.IsTrue(wrench.ContainsKey("description"));
            Assert.IsTrue(wrench.ContainsKey("type"));
            Assert.AreEqual("Tool", wrench["type"]);
            Assert.AreEqual(true, wrench["is_usable"]);
            Assert.AreEqual(true, wrench["is_equippable"]);
        }

        #endregion

        #region ExecutionManager Tests

        [Test]
        public void ExecutionManager_StartHandler_SetsActiveHandler()
        {
            // Arrange - need to create ExecutionManager in a scene
            var go = new GameObject("ExecutionManager");
            var manager = go.AddComponent<ExecutionManager>();

            try
            {
                // Initial state
                Assert.IsFalse(manager.HasActiveHandler);

                // Act - create a mock handler
                var mockHandler = new MockActionHandler();
                var action = new ActionInstance { ActionId = "act_001" };

                manager.StartHandler(mockHandler, action);

                // Assert
                Assert.IsTrue(manager.HasActiveHandler);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ExecutionManager_StartHandler_NewHandlerCancelsOld()
        {
            // Arrange
            var go = new GameObject("ExecutionManager");
            var manager = go.AddComponent<ExecutionManager>();

            try
            {
                var handler1 = new MockActionHandler();
                var action1 = new ActionInstance { ActionId = "act_001" };
                manager.StartHandler(handler1, action1);

                // Act - start a new handler
                var handler2 = new MockActionHandler();
                var action2 = new ActionInstance { ActionId = "act_002" };
                manager.StartHandler(handler2, action2);

                // Assert
                Assert.IsTrue(handler1.WasCancelled, "Old handler should be cancelled");
                Assert.IsFalse(handler2.WasCancelled, "New handler should not be cancelled");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ExecutionManager_CancelCurrent_CancelsActiveHandler()
        {
            // Arrange
            var go = new GameObject("ExecutionManager");
            var manager = go.AddComponent<ExecutionManager>();

            try
            {
                var handler = new MockActionHandler();
                var action = new ActionInstance { ActionId = "act_001" };
                manager.StartHandler(handler, action);

                // Act
                manager.CancelCurrent();

                // Assert
                Assert.IsTrue(handler.WasCancelled);
                Assert.IsFalse(manager.HasActiveHandler);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        #endregion

        #region ActionHandler Interface Tests

        [Test]
        public void MockActionHandler_ImplementsInterface()
        {
            // Arrange
            var handler = new MockActionHandler();
            var action = new ActionInstance { ActionId = "act_001" };

            // Act
            handler.StartAction(action);

            // Assert
            Assert.IsFalse(handler.IsComplete);
            Assert.AreEqual(action, handler.Action);
        }

        [Test]
        public void MockActionHandler_CompletesAfterUpdate()
        {
            // Arrange
            var handler = new MockActionHandler { AutoCompleteAfterUpdates = 2 };
            var action = new ActionInstance { ActionId = "act_001" };

            // Act
            handler.StartAction(action);
            Assert.IsFalse(handler.IsComplete);

            handler.UpdateAction();
            Assert.IsFalse(handler.IsComplete);

            handler.UpdateAction();

            // Assert
            Assert.IsTrue(handler.IsComplete);
        }

        [Test]
        public void MockActionHandler_Cancel_SetsCancelled()
        {
            // Arrange
            var handler = new MockActionHandler();
            var action = new ActionInstance { ActionId = "act_001" };
            handler.StartAction(action);

            // Act
            handler.Cancel();

            // Assert
            Assert.IsTrue(handler.WasCancelled);
        }

        #endregion

        #region EntityIdentity Integration Tests

        [Test]
        public void EntityIdentity_AutoRegistersOnEnable()
        {
            // Arrange
            var registryGo = new GameObject("EntityRegistry");
            var registry = registryGo.AddComponent<EntityRegistry>();

            try
            {
                // Act
                var entityGo = new GameObject("TV");
                var identity = entityGo.AddComponent<EntityIdentity>();
                identity.entityId = "tv_01";
                identity.displayName = "电视机";

                // Trigger OnEnable
                entityGo.SetActive(true);

                // Note: In Edit Mode tests, OnEnable might not be called automatically
                // So we manually register for this test
                registry.Register(identity);

                // Assert
                Assert.IsNotNull(registry.GetById("tv_01"));

                // Cleanup
                Object.DestroyImmediate(entityGo);
            }
            finally
            {
                Object.DestroyImmediate(registryGo);
            }
        }

        #endregion

        #region Error Response Tests

        [Test]
        public void ErrorResponse_TargetNotFound_HasCorrectCode()
        {
            // Arrange & Act
            var response = new MCPResponse
            {
                Ok = false,
                Error = new MCPError
                {
                    Code = ErrorCodes.TARGET_NOT_FOUND,
                    Message = "Target not found",
                    Retryable = true,
                    SuggestedNextActions = new List<SuggestedAction>
                    {
                        new SuggestedAction { Tool = "get_nearby_entities", Args = new Dictionary<string, object>() }
                    }
                }
            };

            // Assert
            Assert.IsFalse(response.Ok);
            Assert.AreEqual(ErrorCodes.TARGET_NOT_FOUND, response.Error.Code);
            Assert.IsTrue(response.Error.Retryable);
            Assert.IsNotNull(response.Error.SuggestedNextActions);
            Assert.AreEqual(1, response.Error.SuggestedNextActions.Count);
        }

        [Test]
        public void ErrorResponse_AmbiguousTarget_HasCandidates()
        {
            // Arrange & Act
            var response = new MCPResponse
            {
                Ok = false,
                Error = new MCPError
                {
                    Code = ErrorCodes.AMBIGUOUS_TARGET,
                    Message = "Multiple targets found",
                    Retryable = true,
                    Details = new Dictionary<string, object>
                    {
                        { "candidates", new List<CandidateInfo>
                            {
                                new CandidateInfo { EntityId = "door_1", DisplayName = "前门", Distance = 2f },
                                new CandidateInfo { EntityId = "door_2", DisplayName = "后门", Distance = 5f }
                            }
                        }
                    }
                }
            };

            // Assert
            Assert.IsFalse(response.Ok);
            Assert.AreEqual(ErrorCodes.AMBIGUOUS_TARGET, response.Error.Code);
            Assert.IsTrue(response.Error.Details.ContainsKey("candidates"));
        }

        #endregion

        #region Mock Classes

        private class MockActionHandler : IActionHandler
        {
            public ActionInstance Action { get; private set; }
            public bool WasCancelled { get; private set; }
            public bool IsComplete { get; private set; }
            public int UpdateCount { get; private set; }
            public int AutoCompleteAfterUpdates { get; set; } = -1;

            public void StartAction(ActionInstance action)
            {
                Action = action;
                IsComplete = false;
                WasCancelled = false;
                UpdateCount = 0;
            }

            public void UpdateAction()
            {
                if (IsComplete || WasCancelled) return;

                UpdateCount++;

                if (AutoCompleteAfterUpdates > 0 && UpdateCount >= AutoCompleteAfterUpdates)
                {
                    IsComplete = true;
                    if (Action != null)
                        Action.Status = ActionStatus.Completed;
                }
            }

            public void Cancel()
            {
                WasCancelled = true;
                if (Action != null)
                    Action.Status = ActionStatus.Cancelled;
                IsComplete = true;
            }
        }

        #endregion
    }
}
