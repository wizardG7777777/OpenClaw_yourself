using NUnit.Framework;
using MCP.Core;
using MCP.Entity;
using MCP.Gateway;
using MCP.Router;
using MCP.Executor;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace MCP.Tests.Integration
{
    /// <summary>
    /// Integration tests for the complete MCP system flow
    /// </summary>
    public class IntegrationTests
    {
        private GameObject registryGo;
        private EntityRegistry registry;
        private GameObject routerGo;
        private GameObject gatewayGo;
        private GameObject executionManagerGo;
        private GameObject player;

        [SetUp]
        public void SetUp()
        {
            // Create core components
            registryGo = new GameObject("EntityRegistry");
            registry = registryGo.AddComponent<EntityRegistry>();
            BindRegistryInstance(registry);

            routerGo = new GameObject("MCPRouter");
            routerGo.AddComponent<MCPRouter>();

            gatewayGo = new GameObject("MCPGateway");
            gatewayGo.AddComponent<MCPGateway>();

            executionManagerGo = new GameObject("ExecutionManager");
            executionManagerGo.AddComponent<ExecutionManager>();

            // Create player
            player = new GameObject("Player");
            player.tag = "Player";
            player.transform.position = Vector3.zero;
        }

        [TearDown]
        public void TearDown()
        {
            if (registryGo != null) Object.DestroyImmediate(registryGo);
            if (routerGo != null) Object.DestroyImmediate(routerGo);
            if (gatewayGo != null) Object.DestroyImmediate(gatewayGo);
            if (executionManagerGo != null) Object.DestroyImmediate(executionManagerGo);
            if (player != null) Object.DestroyImmediate(player);

            // Clean up any test entities
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

        #region End-to-End Flow Tests

        [Test]
        public void EndToEnd_QueryTool_ReturnsSuccess()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.RegisterMVPTools();

            // Simulate a get_inventory query
            var request = new MCPRequest
            {
                RequestId = "req_001",
                Tool = "get_inventory",
                Args = new Dictionary<string, object>(),
                PlayerId = "player_1"
            };

            // Act - process through handler
            var response = GetInventoryHandler.Handle(request);

            // Assert
            Assert.IsTrue(response.Ok);
            Assert.IsNotNull(response.Data);
        }

        [Test]
        public void EndToEnd_InvalidTool_ReturnsError()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.RegisterMVPTools();

            // Act
            var tool = registry.GetTool("invalid_tool");

            // Assert
            Assert.IsNull(tool);
        }

        [Test]
        public void EndToEnd_Validation_ValidRequest_Passes()
        {
            // Arrange
            var json = JObject.Parse(@"{ ""tool"": ""get_inventory"", ""args"": {} }");
            var registry = new ToolRegistry();
            registry.RegisterMVPTools();

            // Act
            var structureResult = RequestValidator.ValidateStructure(json);
            var toolResult = RequestValidator.ValidateToolExists(
                json["tool"].Value<string>(),
                registry.GetAllowedTools()
            );

            // Assert
            Assert.IsTrue(structureResult.IsValid);
            Assert.IsTrue(toolResult.IsValid);
        }

        [Test]
        public void EndToEnd_Validation_InvalidTool_Fails()
        {
            // Arrange
            var json = JObject.Parse(@"{ ""tool"": ""invalid_tool"", ""args"": {} }");
            var registry = new ToolRegistry();
            registry.RegisterMVPTools();

            // Act
            var structureResult = RequestValidator.ValidateStructure(json);
            var toolResult = RequestValidator.ValidateToolExists(
                json["tool"].Value<string>(),
                registry.GetAllowedTools()
            );

            // Assert
            Assert.IsTrue(structureResult.IsValid); // Structure is fine
            Assert.IsFalse(toolResult.IsValid); // But tool doesn't exist
        }

        #endregion

        #region Tool Registry + Parameter Normalization Integration

        [Test]
        public void Integration_ToolRegistry_WithParameterNormalization()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.RegisterMVPTools();

            var moveToTool = registry.GetTool("move_to");
            var args = new Dictionary<string, object>
            {
                { "target_id", "tv_01" }
            };

            // Act
            var normalized = ParameterNormalizer.Normalize(args, moveToTool, out string errorParam);

            // Assert
            Assert.IsNotNull(normalized);
            Assert.IsNull(errorParam);
            Assert.AreEqual("tv_01", normalized["target_id"]);
        }

        [Test]
        public void Integration_ToolRegistry_MissingRequiredParam_Detected()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.RegisterMVPTools();

            var moveToTool = registry.GetTool("move_to");
            var args = new Dictionary<string, object>(); // missing target_id

            // Act
            var normalized = ParameterNormalizer.Normalize(args, moveToTool, out string errorParam);

            // Assert
            Assert.IsNull(normalized);
            Assert.AreEqual("target_id", errorParam);
        }

        [Test]
        public void Integration_ToolRegistry_TalkToNpc_UsesNpcIdRequiredParam()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.RegisterMVPTools();

            var talkToNpcTool = registry.GetTool("talk_to_npc");
            var args = new Dictionary<string, object>
            {
                { "npc_id", "npc_01" }
            };

            // Act
            var normalized = ParameterNormalizer.Normalize(args, talkToNpcTool, out string errorParam);

            // Assert
            Assert.IsNotNull(normalized);
            Assert.IsNull(errorParam);
            Assert.AreEqual("npc_01", normalized["npc_id"]);
        }

        [Test]
        public void Integration_ToolRegistry_TalkToNpc_MissingNpcId_Detected()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.RegisterMVPTools();

            var talkToNpcTool = registry.GetTool("talk_to_npc");
            var args = new Dictionary<string, object>();

            // Act
            var normalized = ParameterNormalizer.Normalize(args, talkToNpcTool, out string errorParam);

            // Assert
            Assert.IsNull(normalized);
            Assert.AreEqual("npc_id", errorParam);
        }

        [Test]
        public void Integration_AllMVPQueryTools_AreNonExclusive()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.RegisterMVPTools();

            var queryTools = new[] { "get_player_state", "get_world_summary", "get_nearby_entities", "get_inventory" };

            // Act & Assert
            foreach (var toolName in queryTools)
            {
                var tool = registry.GetTool(toolName);
                Assert.IsNotNull(tool, $"{toolName} should exist");
                Assert.IsFalse(tool.IsExclusive, $"{toolName} should be non-exclusive (query)");
            }
        }

        [Test]
        public void Integration_AllMVPActionTools_AreExclusive()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.RegisterMVPTools();

            var actionTools = new[] { "move_to", "interact_with", "use_tool_on", "talk_to_npc", "equip_item" };

            // Act & Assert
            foreach (var toolName in actionTools)
            {
                var tool = registry.GetTool(toolName);
                Assert.IsNotNull(tool, $"{toolName} should exist");
                Assert.IsTrue(tool.IsExclusive, $"{toolName} should be exclusive (action)");
            }
        }

        #endregion

        #region Entity Resolution Integration

        [Test]
        public void Integration_EntityRegistry_WithSemanticResolver()
        {
            // Arrange
            var tvGo = new GameObject("TV");
            var tv = tvGo.AddComponent<EntityIdentity>();
            tv.entityId = "tv_living_room";
            tv.displayName = "客厅电视";
            tv.transform.position = new Vector3(5f, 0, 0);

            // Register entity through the test-owned registry instance
            registry.Register(tv);

            try
            {
                // Act - use semantic resolver
                var result = SemanticResolver.ResolveTarget("客厅电视", Vector3.zero);

                // Assert
                Assert.IsTrue(result.Success);
                Assert.AreEqual("tv_living_room", result.Target.EntityId);
            }
            finally
            {
                Object.DestroyImmediate(tvGo);
            }
        }

        [Test]
        public void Integration_ResolvedTarget_UsedInActionInstance()
        {
            // Arrange
            var targetGo = new GameObject("Target");
            targetGo.transform.position = Vector3.one;

            var resolvedTarget = ResolvedTarget.FromEntity("target_01", targetGo, Vector3.one);

            var action = new ActionInstance
            {
                ActionId = "act_001",
                ToolName = "move_to",
                Status = ActionStatus.Running,
                Target = resolvedTarget,
                CreatedAt = Time.time,
                Timeout = 30f
            };

            try
            {
                // Assert
                Assert.AreEqual("target_01", action.Target.EntityId);
                Assert.AreEqual(Vector3.one, action.Target.Position);
                Assert.AreEqual(TargetType.Entity, action.Target.Type);
            }
            finally
            {
                Object.DestroyImmediate(targetGo);
            }
        }

        #endregion

        #region Error Response Integration

        [Test]
        public void Integration_TargetNotFound_GeneratesCorrectError()
        {
            // Arrange - empty registry, no entities

            // Act
            var result = SemanticResolver.ResolveTarget("nonexistent_entity", Vector3.zero);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(ErrorCodes.TARGET_NOT_FOUND, result.ErrorCode);
            Assert.IsNotNull(result.Message);
            Assert.IsTrue(result.Message.Contains("nonexistent_entity"));
        }

        [Test]
        public void Integration_AmbiguousTarget_GeneratesCandidates()
        {
            // Arrange
            var door1Go = new GameObject("Door1");
            door1Go.transform.position = new Vector3(2f, 0, 0);
            var door1 = door1Go.AddComponent<EntityIdentity>();
            door1.entityId = "door_front";
            door1.displayName = "前门";
            door1.aliases = new[] { "门" };

            var door2Go = new GameObject("Door2");
            door2Go.transform.position = new Vector3(5f, 0, 0);
            var door2 = door2Go.AddComponent<EntityIdentity>();
            door2.entityId = "door_back";
            door2.displayName = "后门";
            door2.aliases = new[] { "门" };

            registry.Register(door1);
            registry.Register(door2);

            try
            {
                // Act
                var result = SemanticResolver.ResolveTarget("门", Vector3.zero);

                // Assert
                Assert.IsFalse(result.Success);
                Assert.AreEqual(ErrorCodes.AMBIGUOUS_TARGET, result.ErrorCode);
                Assert.IsNotNull(result.Candidates);
                Assert.AreEqual(2, result.Candidates.Count);
            }
            finally
            {
                Object.DestroyImmediate(door1Go);
                Object.DestroyImmediate(door2Go);
            }
        }

        [Test]
        public void Integration_MCPResponse_ErrorFormat()
        {
            // Arrange
            var response = new MCPResponse
            {
                Ok = false,
                Error = new MCPError
                {
                    Code = ErrorCodes.INVALID_PARAMS,
                    Message = "Missing required parameter 'target_id'",
                    Retryable = false,
                    Details = new Dictionary<string, object>
                    {
                        { "field", "target_id" },
                        { "tool", "move_to" }
                    }
                }
            };

            // Assert
            Assert.IsFalse(response.Ok);
            Assert.IsNull(response.ActionId);
            Assert.IsNull(response.Data);
            Assert.IsNotNull(response.Error);
            Assert.AreEqual(ErrorCodes.INVALID_PARAMS, response.Error.Code);
            Assert.IsFalse(response.Error.Retryable);
        }

        #endregion

        #region Action State Machine Integration

        [Test]
        public void Integration_ActionStateMachine_Transitions()
        {
            // Arrange
            var action = new ActionInstance
            {
                ActionId = "act_001",
                ToolName = "equip_item",
                Status = ActionStatus.Running
            };

            // Act & Assert - test state transitions
            Assert.AreEqual(ActionStatus.Running, action.Status);

            action.Status = ActionStatus.Completed;
            Assert.AreEqual(ActionStatus.Completed, action.Status);

            // Create a new action for failed test
            var action2 = new ActionInstance
            {
                ActionId = "act_002",
                Status = ActionStatus.Running
            };
            action2.Status = ActionStatus.Failed;
            Assert.AreEqual(ActionStatus.Failed, action2.Status);

            // Create a new action for cancelled test
            var action3 = new ActionInstance
            {
                ActionId = "act_003",
                Status = ActionStatus.Running
            };
            action3.Status = ActionStatus.Cancelled;
            Assert.AreEqual(ActionStatus.Cancelled, action3.Status);
        }

        [Test]
        public void Integration_ActionInstance_WithResult()
        {
            // Arrange
            var action = new ActionInstance
            {
                ActionId = "act_001",
                ToolName = "equip_item",
                Status = ActionStatus.Completed,
                Result = new Dictionary<string, object>
                {
                    { "item_id", "wrench" },
                    { "success", true }
                }
            };

            // Act & Assert
            Assert.IsNotNull(action.Result);
            var result = action.Result as Dictionary<string, object>;
            Assert.IsNotNull(result);
            Assert.AreEqual("wrench", result["item_id"]);
        }

        [Test]
        public void Integration_ActionInstance_WithErrorCode()
        {
            // Arrange
            var action = new ActionInstance
            {
                ActionId = "act_001",
                ToolName = "equip_item",
                Status = ActionStatus.Failed,
                ErrorCode = ErrorCodes.TARGET_NOT_FOUND,
                Result = new { message = "Item not found" }
            };

            // Act & Assert
            Assert.AreEqual(ActionStatus.Failed, action.Status);
            Assert.AreEqual(ErrorCodes.TARGET_NOT_FOUND, action.ErrorCode);
        }

        #endregion

        #region Complete Request Flow Tests

        [Test]
        public void Integration_CompleteFlow_ValidQuery()
        {
            // This test simulates a complete request flow

            // Step 1: Parse and validate request JSON
            var json = JObject.Parse(@"{
                ""request_id"": ""req_001"",
                ""tool"": ""get_inventory"",
                ""args"": {},
                ""player_id"": ""player_1""
            }");

            var structureResult = RequestValidator.ValidateStructure(json);
            Assert.IsTrue(structureResult.IsValid, "Structure should be valid");

            // Step 2: Build MCPRequest
            var request = new MCPRequest
            {
                RequestId = json["request_id"].Value<string>(),
                Tool = json["tool"].Value<string>(),
                Args = json["args"].ToObject<Dictionary<string, object>>(),
                PlayerId = json["player_id"].Value<string>()
            };

            Assert.AreEqual("get_inventory", request.Tool);

            // Step 3: Route and handle (query)
            var response = GetInventoryHandler.Handle(request);

            // Step 4: Verify response
            Assert.IsTrue(response.Ok);
            Assert.IsNotNull(response.Data);
        }

        [Test]
        public void Integration_CompleteFlow_ValidationError()
        {
            // Step 1: Try to parse invalid JSON (missing tool)
            var json = JObject.Parse(@"{ ""args"": {} }");

            // Step 2: Validate structure
            var structureResult = RequestValidator.ValidateStructure(json);

            // Step 3: Verify validation failed
            Assert.IsFalse(structureResult.IsValid);
            Assert.AreEqual("tool", structureResult.ErrorField);

            // Step 4: Build error response
            var response = new MCPResponse
            {
                Ok = false,
                Error = new MCPError
                {
                    Code = ErrorCodes.INVALID_PARAMS,
                    Message = structureResult.ErrorReason,
                    Retryable = false,
                    Details = new Dictionary<string, object>
                    {
                        { "field", structureResult.ErrorField },
                        { "suggestion", structureResult.Suggestion }
                    }
                }
            };

            Assert.IsFalse(response.Ok);
            Assert.AreEqual(ErrorCodes.INVALID_PARAMS, response.Error.Code);
        }

        #endregion
    }
}
