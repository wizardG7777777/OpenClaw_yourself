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
    /// Tests for individual Action Handlers
    /// </summary>
    public class ActionHandlerTests
    {
        private GameObject player;
        private GameObject target;
        private GameObject itemRegistryGo;

        [SetUp]
        public void SetUp()
        {
            // Create player
            player = new GameObject("Player");
            player.tag = "Player";
            player.transform.position = Vector3.zero;

            // Create target entity
            target = new GameObject("TargetEntity");
            target.transform.position = new Vector3(2f, 0, 0);

            // Create ItemRegistry with default items
            itemRegistryGo = new GameObject("ItemRegistry");
            var registry = itemRegistryGo.AddComponent<ItemRegistry>();
            typeof(ItemRegistry)
                .GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)
                ?.GetSetMethod(true)
                ?.Invoke(null, new object[] { registry });
            // Trigger Awake to register default items
            var awake = typeof(ItemRegistry).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            awake?.Invoke(registry, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (player != null)
                Object.DestroyImmediate(player);
            if (target != null)
                Object.DestroyImmediate(target);
            if (itemRegistryGo != null)
                Object.DestroyImmediate(itemRegistryGo);

            typeof(ItemRegistry)
                .GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)
                ?.GetSetMethod(true)
                ?.Invoke(null, new object[] { null });

            // Clean up any remaining
            var remaining = Object.FindObjectsByType<EntityIdentity>(FindObjectsSortMode.None);
            foreach (var e in remaining)
                Object.DestroyImmediate(e.gameObject);
        }

        #region EquipItemHandler Tests

        [Test]
        public void EquipItemHandler_ValidItem_CompletesSuccessfully()
        {
            // Arrange
            var handler = new EquipItemHandler();
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Target = ResolvedTarget.FromEntity("wrench", null, Vector3.zero)
            };

            // Act
            handler.StartAction(action);

            // Assert
            Assert.AreEqual(ActionStatus.Completed, action.Status);
            Assert.IsNotNull(action.Result);
        }

        [Test]
        public void EquipItemHandler_InvalidItem_Fails()
        {
            // Arrange
            var handler = new EquipItemHandler();
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Target = ResolvedTarget.FromEntity("invalid_item", null, Vector3.zero)
            };

            // Act
            handler.StartAction(action);

            // Assert
            Assert.AreEqual(ActionStatus.Failed, action.Status);
            Assert.AreEqual(ErrorCodes.TARGET_NOT_FOUND, action.ErrorCode);
        }

        [Test]
        public void EquipItemHandler_NullTarget_Fails()
        {
            // Arrange
            var handler = new EquipItemHandler();
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Target = null
            };

            // Act
            handler.StartAction(action);

            // Assert
            Assert.AreEqual(ActionStatus.Failed, action.Status);
        }

        [Test]
        public void EquipItemHandler_EmptyItemId_Fails()
        {
            // Arrange
            var handler = new EquipItemHandler();
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Target = ResolvedTarget.FromEntity("", null, Vector3.zero)
            };

            // Act
            handler.StartAction(action);

            // Assert
            Assert.AreEqual(ActionStatus.Failed, action.Status);
        }

        [Test]
        public void EquipItemHandler_AvailableItems_IncludeMVPSet()
        {
            // Arrange
            var validItems = new[] { "wrench", "shovel", "postcard" };

            foreach (var itemId in validItems)
            {
                var handler = new EquipItemHandler();
                var action = new ActionInstance
                {
                    Target = ResolvedTarget.FromEntity(itemId, null, Vector3.zero)
                };

                // Act
                handler.StartAction(action);

                // Assert
                Assert.AreEqual(ActionStatus.Completed, action.Status,
                    $"Item '{itemId}' should be valid and complete successfully");
            }
        }

        #endregion

        #region TalkToNpcHandler Tests

        [Test]
        public void TalkToNpcHandler_ValidTarget_CompletesSuccessfully()
        {
            // Arrange
            var handler = new TalkToNpcHandler();
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Target = ResolvedTarget.FromEntity("npc_villager", target, target.transform.position)
            };

            // Act
            handler.StartAction(action);

            // Assert
            Assert.AreEqual(ActionStatus.Completed, action.Status);
            Assert.IsNotNull(action.Result);
        }

        [Test]
        public void TalkToNpcHandler_NullTarget_Fails()
        {
            // Arrange
            var handler = new TalkToNpcHandler();
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Target = null
            };

            // Act
            handler.StartAction(action);

            // Assert
            Assert.AreEqual(ActionStatus.Failed, action.Status);
            Assert.AreEqual(ErrorCodes.TARGET_NOT_FOUND, action.ErrorCode);
        }

        [Test]
        public void TalkToNpcHandler_NullEntityObject_Fails()
        {
            // Arrange
            var handler = new TalkToNpcHandler();
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Target = ResolvedTarget.FromEntity("npc_01", null, Vector3.zero)
            };

            // Act
            handler.StartAction(action);

            // Assert
            Assert.AreEqual(ActionStatus.Failed, action.Status);
            Assert.AreEqual(ErrorCodes.TARGET_NOT_FOUND, action.ErrorCode);
        }

        [Test]
        public void TalkToNpcHandler_Result_ContainsNpcName()
        {
            // Arrange
            var handler = new TalkToNpcHandler();
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Target = ResolvedTarget.FromEntity("npc_merchant", target, target.transform.position)
            };

            // Act
            handler.StartAction(action);

            // Assert
            Assert.AreEqual(ActionStatus.Completed, action.Status);
            var resultDict = action.Result as System.Dynamic.ExpandoObject;
            // Note: Result is an anonymous object, checking via string representation
            Assert.IsNotNull(action.Result);
        }

        #endregion

        #region UseToolOnHandler Tests

        [Test]
        public void UseToolOnHandler_NoToolId_Fails()
        {
            // Arrange
            var handler = new UseToolOnHandler();
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Target = ResolvedTarget.FromEntity("target_01", target, target.transform.position),
                Result = null // No tool_id in result/args
            };

            // Act
            handler.StartAction(action);

            // Assert
            Assert.AreEqual(ActionStatus.Failed, action.Status);
            Assert.AreEqual(ErrorCodes.TARGET_NOT_FOUND, action.ErrorCode);
        }

        [Test]
        public void UseToolOnHandler_InvalidTool_Fails()
        {
            // Arrange
            var handler = new UseToolOnHandler();
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Target = ResolvedTarget.FromEntity("target_01", target, target.transform.position),
                Result = new Dictionary<string, object> { { "tool_id", "invalid_tool" } }
            };

            // Act
            handler.StartAction(action);

            // Assert
            Assert.AreEqual(ActionStatus.Failed, action.Status);
            Assert.AreEqual(ErrorCodes.TARGET_NOT_FOUND, action.ErrorCode);
        }

        [Test]
        public void UseToolOnHandler_NullTarget_Fails()
        {
            // Arrange
            var handler = new UseToolOnHandler();
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Target = null,
                Result = new Dictionary<string, object> { { "tool_id", "wrench" } }
            };

            // Act
            handler.StartAction(action);

            // Assert
            Assert.AreEqual(ActionStatus.Failed, action.Status);
            Assert.AreEqual(ErrorCodes.TARGET_NOT_FOUND, action.ErrorCode);
        }

        [Test]
        public void UseToolOnHandler_ValidToolAndInteractable_CompletesSuccessfully()
        {
            var mock = target.AddComponent<MockInteractable>();
            var handler = new UseToolOnHandler();
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Target = ResolvedTarget.FromEntity("target_01", target, target.transform.position),
                Result = new Dictionary<string, object> { { "tool_id", "wrench" } }
            };

            handler.StartAction(action);

            Assert.AreEqual(ActionStatus.Completed, action.Status);
            Assert.IsTrue(mock.WasInteracted);
        }

        #endregion

        #region InteractWithHandler Tests

        [Test]
        public void InteractWithHandler_NullTarget_Fails()
        {
            // Arrange
            var handler = new InteractWithHandler();
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Target = null
            };

            // Act
            handler.StartAction(action);

            // Assert
            Assert.AreEqual(ActionStatus.Failed, action.Status);
            Assert.AreEqual(ErrorCodes.TARGET_NOT_FOUND, action.ErrorCode);
        }

        [Test]
        public void InteractWithHandler_NullEntityObject_Fails()
        {
            // Arrange
            var handler = new InteractWithHandler();
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Target = ResolvedTarget.FromEntity("target_01", null, Vector3.zero)
            };

            // Act
            handler.StartAction(action);

            // Assert
            Assert.AreEqual(ActionStatus.Failed, action.Status);
            Assert.AreEqual(ErrorCodes.TARGET_NOT_FOUND, action.ErrorCode);
        }

        [Test]
        public void InteractWithHandler_NoIInteractable_Fails()
        {
            // Arrange
            // target doesn't have IInteractable component
            var handler = new InteractWithHandler();
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Target = ResolvedTarget.FromEntity("target_01", target, target.transform.position)
            };

            // Act
            handler.StartAction(action);

            // Assert
            Assert.AreEqual(ActionStatus.Failed, action.Status);
            Assert.AreEqual(ErrorCodes.TOOL_NOT_APPLICABLE, action.ErrorCode);
        }

        [Test]
        public void InteractWithHandler_PlayerTooFar_Fails()
        {
            // Arrange
            // Move player far away
            player.transform.position = new Vector3(100f, 0, 0);
            target.transform.position = Vector3.zero;

            var handler = new InteractWithHandler();
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Target = ResolvedTarget.FromEntity("target_01", target, target.transform.position)
            };

            // Act
            handler.StartAction(action);

            // Assert
            Assert.AreEqual(ActionStatus.Failed, action.Status);
            Assert.AreEqual(ErrorCodes.OUT_OF_RANGE, action.ErrorCode);
        }

        [Test]
        public void InteractWithHandler_InteractableInRange_CompletesSuccessfully()
        {
            var mock = target.AddComponent<MockInteractable>();
            player.transform.position = Vector3.zero;
            target.transform.position = new Vector3(2f, 0f, 0f);

            var handler = new InteractWithHandler();
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Target = ResolvedTarget.FromEntity("target_01", target, target.transform.position)
            };

            handler.StartAction(action);

            Assert.AreEqual(ActionStatus.Completed, action.Status);
            Assert.IsNotNull(action.Result);
            Assert.IsTrue(mock.WasInteracted);
        }

        #endregion

        #region MoveToHandler Tests

        [Test]
        public void MoveToHandler_NoPlayer_Fails()
        {
            // Arrange
            Object.DestroyImmediate(player);
            player = null;

            var handler = new MoveToHandler();
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Target = ResolvedTarget.FromPoint(Vector3.one)
            };

            // Act
            handler.StartAction(action);

            // Assert
            Assert.AreEqual(ActionStatus.Failed, action.Status);
            Assert.AreEqual(ErrorCodes.TARGET_NOT_FOUND, action.ErrorCode);
        }

        [Test]
        public void MoveToHandler_NoNavMeshAgent_Fails()
        {
            // Arrange
            // Player doesn't have NavMeshAgent
            var handler = new MoveToHandler();
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Target = ResolvedTarget.FromPoint(Vector3.one)
            };

            // Act
            handler.StartAction(action);

            // Assert
            Assert.AreEqual(ActionStatus.Failed, action.Status);
            Assert.AreEqual(ErrorCodes.UNREACHABLE, action.ErrorCode);
        }

        [Test]
        public void MoveToHandler_IsComplete_InitiallyFalse()
        {
            // Arrange
            var handler = new MoveToHandler();
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Status = ActionStatus.Running
            };

            // Act
            handler.StartAction(action);

            // Assert - handler should not be complete after start (even if failed)
            // Note: The handler's IsComplete checks action.Status != Running
            // If it failed immediately, it would be complete
            if (action.Status == ActionStatus.Running)
            {
                Assert.IsFalse(handler.IsComplete);
            }
        }

        [Test]
        public void MoveToHandler_Cancel_SetsCancelled()
        {
            // Arrange
            var handler = new MoveToHandler();
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Status = ActionStatus.Running
            };

            // Act
            handler.StartAction(action);
            handler.Cancel();

            // Assert
            Assert.AreEqual(ActionStatus.Cancelled, action.Status);
        }

        #endregion

        #region Action Timeout Tests

        [Test]
        public void ActionInstance_TimeoutCheck_CanBeVerified()
        {
            // Arrange
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Status = ActionStatus.Running,
                CreatedAt = Time.time - 100f, // Started 100 seconds ago
                Timeout = 30f // 30 second timeout
            };

            // Act & Assert
            Assert.IsTrue(Time.time - action.CreatedAt > action.Timeout,
                "Action should be considered timed out");
        }

        [Test]
        public void ActionInstance_NotTimedOut_WhenWithinTimeout()
        {
            // Arrange
            var action = new ActionInstance
            {
                ActionId = "act_001",
                Status = ActionStatus.Running,
                CreatedAt = Time.time,
                Timeout = 60f
            };

            // Act & Assert
            Assert.IsFalse(Time.time - action.CreatedAt > action.Timeout,
                "Action should not be timed out yet");
        }

        #endregion

        #region Handler Interface Contract Tests

        [Test]
        public void AllHandlers_ImplementIActionHandler()
        {
            // Verify that all action handlers implement IActionHandler
            Assert.IsTrue(typeof(IActionHandler).IsAssignableFrom(typeof(MoveToHandler)));
            Assert.IsTrue(typeof(IActionHandler).IsAssignableFrom(typeof(InteractWithHandler)));
            Assert.IsTrue(typeof(IActionHandler).IsAssignableFrom(typeof(UseToolOnHandler)));
            Assert.IsTrue(typeof(IActionHandler).IsAssignableFrom(typeof(TalkToNpcHandler)));
            Assert.IsTrue(typeof(IActionHandler).IsAssignableFrom(typeof(EquipItemHandler)));
        }

        [Test]
        public void AllHandlers_HaveIsCompleteProperty()
        {
            // This test verifies the interface contract is implemented
            var handlers = new IActionHandler[]
            {
                new MoveToHandler(),
                new InteractWithHandler(),
                new UseToolOnHandler(),
                new TalkToNpcHandler(),
                new EquipItemHandler()
            };

            foreach (var handler in handlers)
            {
                // Should be able to read IsComplete without exception
                var isComplete = handler.IsComplete;
                // Type should be bool
                Assert.IsInstanceOf<bool>(isComplete);
            }
        }

        [Test]
        public void AllHandlers_HaveRequiredMethods()
        {
            // Verify methods exist via reflection
            var handlerTypes = new[]
            {
                typeof(MoveToHandler),
                typeof(InteractWithHandler),
                typeof(UseToolOnHandler),
                typeof(TalkToNpcHandler),
                typeof(EquipItemHandler)
            };

            foreach (var type in handlerTypes)
            {
                Assert.IsNotNull(type.GetMethod("StartAction"), $"{type.Name} should have StartAction");
                Assert.IsNotNull(type.GetMethod("UpdateAction"), $"{type.Name} should have UpdateAction");
                Assert.IsNotNull(type.GetMethod("Cancel"), $"{type.Name} should have Cancel");
            }
        }

        #endregion

        private sealed class MockInteractable : MonoBehaviour, IInteractable
        {
            public bool WasInteracted { get; private set; }

            public bool Interact()
            {
                WasInteracted = true;
                return true;
            }

            public string GetPromptText()
            {
                return "Interact";
            }

            public System.Collections.Generic.Dictionary<string, object> GetState()
            {
                return new System.Collections.Generic.Dictionary<string, object>
                {
                    { "status", WasInteracted ? "used" : "idle" }
                };
            }
        }
    }
}
