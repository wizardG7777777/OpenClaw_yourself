using NUnit.Framework;
using MCP.Core;
using UnityEngine;

namespace MCP.Tests.Core
{
    /// <summary>
    /// Tests for MCP Core data types and structures
    /// </summary>
    public class CoreTests
    {
        #region ResolvedTarget Tests

        [Test]
        public void ResolvedTarget_FromEntity_CreatesCorrectly()
        {
            // Arrange - create a temporary GameObject
            var go = new GameObject("TestEntity");
            go.transform.position = new Vector3(1f, 2f, 3f);
            
            try
            {
                // Act
                var target = ResolvedTarget.FromEntity("entity_001", go, go.transform.position);
                
                // Assert
                Assert.AreEqual(TargetType.Entity, target.Type, "Type should be Entity");
                Assert.AreEqual("entity_001", target.EntityId, "EntityId should match");
                Assert.AreEqual(go, target.EntityObject, "EntityObject should match");
                Assert.AreEqual(new Vector3(1f, 2f, 3f), target.Position, "Position should match");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ResolvedTarget_FromPoint_CreatesCorrectly()
        {
            // Arrange
            var position = new Vector3(10f, 20f, 30f);
            
            // Act
            var target = ResolvedTarget.FromPoint(position);
            
            // Assert
            Assert.AreEqual(TargetType.Point, target.Type, "Type should be Point");
            Assert.AreEqual(position, target.Position, "Position should match");
            Assert.IsNull(target.EntityId, "EntityId should be null for point target");
            Assert.IsNull(target.EntityObject, "EntityObject should be null for point target");
        }

        [Test]
        public void ResolvedTarget_FromEntity_WithNullEntityObject_Works()
        {
            // Act
            var target = ResolvedTarget.FromEntity("entity_002", null, Vector3.zero);
            
            // Assert
            Assert.AreEqual(TargetType.Entity, target.Type);
            Assert.AreEqual("entity_002", target.EntityId);
            Assert.IsNull(target.EntityObject);
        }

        #endregion

        #region ActionInstance Tests

        [Test]
        public void ActionInstance_DefaultValues_AreSetCorrectly()
        {
            // Arrange & Act
            var action = new ActionInstance
            {
                ActionId = "act_001",
                ToolName = "move_to",
                Status = ActionStatus.Running,
                CreatedAt = 100f,
                Timeout = 30f,
                ErrorCode = null,
                Result = null
            };
            
            // Assert
            Assert.AreEqual("act_001", action.ActionId);
            Assert.AreEqual("move_to", action.ToolName);
            Assert.AreEqual(ActionStatus.Running, action.Status);
            Assert.AreEqual(100f, action.CreatedAt);
            Assert.AreEqual(30f, action.Timeout);
            Assert.IsNull(action.ErrorCode);
            Assert.IsNull(action.Result);
            Assert.IsNull(action.Target);
            Assert.IsNull(action.CancelledActionId);
        }

        [Test]
        public void ActionInstance_StatusTransitions_Work()
        {
            // Arrange
            var action = new ActionInstance { ActionId = "act_002" };
            
            // Act & Assert - Running -> Completed
            action.Status = ActionStatus.Running;
            Assert.AreEqual(ActionStatus.Running, action.Status);
            
            action.Status = ActionStatus.Completed;
            Assert.AreEqual(ActionStatus.Completed, action.Status);
            
            // Act & Assert - Running -> Failed
            action.Status = ActionStatus.Running;
            action.Status = ActionStatus.Failed;
            Assert.AreEqual(ActionStatus.Failed, action.Status);
            
            // Act & Assert - Running -> Cancelled
            action.Status = ActionStatus.Running;
            action.Status = ActionStatus.Cancelled;
            Assert.AreEqual(ActionStatus.Cancelled, action.Status);
        }

        [Test]
        public void ActionInstance_WithResolvedTarget_LinksCorrectly()
        {
            // Arrange
            var go = new GameObject("TestTarget");
            var target = ResolvedTarget.FromEntity("target_001", go, Vector3.one);
            
            try
            {
                var action = new ActionInstance
                {
                    ActionId = "act_003",
                    Target = target
                };
                
                // Assert
                Assert.AreEqual(target, action.Target);
                Assert.AreEqual("target_001", action.Target.EntityId);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        #endregion

        #region ErrorCodes Tests

        [Test]
        public void ErrorCodes_Constants_AreDefined()
        {
            // Assert - verify all expected error codes exist
            Assert.AreEqual("TARGET_NOT_FOUND", ErrorCodes.TARGET_NOT_FOUND);
            Assert.AreEqual("AMBIGUOUS_TARGET", ErrorCodes.AMBIGUOUS_TARGET);
            Assert.AreEqual("ACTION_TIMEOUT", ErrorCodes.ACTION_TIMEOUT);
            Assert.AreEqual("TARGET_LOST", ErrorCodes.TARGET_LOST);
            Assert.AreEqual("INVALID_TOOL", ErrorCodes.INVALID_TOOL);
            Assert.AreEqual("INVALID_PARAMS", ErrorCodes.INVALID_PARAMS);
            Assert.AreEqual("OUT_OF_RANGE", ErrorCodes.OUT_OF_RANGE);
            Assert.AreEqual("UNREACHABLE", ErrorCodes.UNREACHABLE);
            Assert.AreEqual("TOOL_NOT_APPLICABLE", ErrorCodes.TOOL_NOT_APPLICABLE);
        }

        [Test]
        public void ErrorCodes_AreUnique()
        {
            // Arrange
            var codes = new[]
            {
                ErrorCodes.TARGET_NOT_FOUND,
                ErrorCodes.AMBIGUOUS_TARGET,
                ErrorCodes.ACTION_TIMEOUT,
                ErrorCodes.TARGET_LOST,
                ErrorCodes.INVALID_TOOL,
                ErrorCodes.INVALID_PARAMS,
                ErrorCodes.OUT_OF_RANGE,
                ErrorCodes.UNREACHABLE,
                ErrorCodes.TOOL_NOT_APPLICABLE
            };
            
            // Act & Assert - verify uniqueness using a HashSet
            var uniqueCodes = new System.Collections.Generic.HashSet<string>(codes);
            Assert.AreEqual(codes.Length, uniqueCodes.Count, "All error codes should be unique");
        }

        #endregion

        #region MCPRequest Tests

        [Test]
        public void MCPRequest_Serialization_HandlesBasicFields()
        {
            // Arrange
            var request = new MCPRequest
            {
                RequestId = "req_001",
                Tool = "move_to",
                PlayerId = "player_1",
                Args = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "target_id", "tv_01" },
                    { "timeout", 30f }
                }
            };
            
            // Assert
            Assert.AreEqual("req_001", request.RequestId);
            Assert.AreEqual("move_to", request.Tool);
            Assert.AreEqual("player_1", request.PlayerId);
            Assert.AreEqual(2, request.Args.Count);
            Assert.IsTrue(request.Args.ContainsKey("target_id"));
            Assert.IsTrue(request.Args.ContainsKey("timeout"));
        }

        [Test]
        public void MCPRequest_WithEmptyArgs_Works()
        {
            // Arrange
            var request = new MCPRequest
            {
                RequestId = "req_002",
                Tool = "get_inventory"
            };
            
            // Assert
            Assert.IsNull(request.Args);
            Assert.AreEqual("get_inventory", request.Tool);
        }

        #endregion

        #region MCPResponse Tests

        [Test]
        public void MCPResponse_SuccessResponse_IsValid()
        {
            // Arrange
            var response = new MCPResponse
            {
                Ok = true,
                ActionId = "act_001",
                Status = "running",
                Data = new { message = "Action started" }
            };
            
            // Assert
            Assert.IsTrue(response.Ok);
            Assert.AreEqual("act_001", response.ActionId);
            Assert.AreEqual("running", response.Status);
            Assert.IsNotNull(response.Data);
            Assert.IsNull(response.Error);
            Assert.IsNull(response.CancelledActionId);
        }

        [Test]
        public void MCPResponse_ErrorResponse_IsValid()
        {
            // Arrange
            var response = new MCPResponse
            {
                Ok = false,
                Error = new MCPError
                {
                    Code = ErrorCodes.TARGET_NOT_FOUND,
                    Message = "Target not found",
                    Retryable = true
                }
            };
            
            // Assert
            Assert.IsFalse(response.Ok);
            Assert.IsNotNull(response.Error);
            Assert.AreEqual(ErrorCodes.TARGET_NOT_FOUND, response.Error.Code);
            Assert.AreEqual("Target not found", response.Error.Message);
            Assert.IsTrue(response.Error.Retryable);
        }

        [Test]
        public void MCPResponse_WithCancellation_ReportsCancelledActionId()
        {
            // Arrange
            var response = new MCPResponse
            {
                Ok = true,
                ActionId = "act_new",
                Status = "running",
                CancelledActionId = "act_old"
            };
            
            // Assert
            Assert.IsTrue(response.Ok);
            Assert.AreEqual("act_new", response.ActionId);
            Assert.AreEqual("act_old", response.CancelledActionId);
        }

        #endregion

        #region MCPError Tests

        [Test]
        public void MCPError_WithDetails_IncludesInResponse()
        {
            // Arrange
            var error = new MCPError
            {
                Code = ErrorCodes.AMBIGUOUS_TARGET,
                Message = "Multiple targets found",
                Retryable = true,
                Details = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "query", "door" },
                    { "count", 3 }
                }
            };
            
            // Assert
            Assert.IsNotNull(error.Details);
            Assert.AreEqual(2, error.Details.Count);
            Assert.AreEqual("door", error.Details["query"]);
        }

        [Test]
        public void MCPError_WithSuggestedActions_IncludesThem()
        {
            // Arrange
            var error = new MCPError
            {
                Code = ErrorCodes.TARGET_NOT_FOUND,
                Message = "Target not found",
                Retryable = true,
                SuggestedNextActions = new System.Collections.Generic.List<SuggestedAction>
                {
                    new SuggestedAction { Tool = "get_nearby_entities", Args = new System.Collections.Generic.Dictionary<string, object>() }
                }
            };
            
            // Assert
            Assert.IsNotNull(error.SuggestedNextActions);
            Assert.AreEqual(1, error.SuggestedNextActions.Count);
            Assert.AreEqual("get_nearby_entities", error.SuggestedNextActions[0].Tool);
        }

        #endregion

        #region SuggestedAction Tests

        [Test]
        public void SuggestedAction_CanStoreToolAndArgs()
        {
            // Arrange
            var args = new System.Collections.Generic.Dictionary<string, object>
            {
                { "radius", 50f }
            };
            
            var action = new SuggestedAction
            {
                Tool = "get_nearby_entities",
                Args = args
            };
            
            // Assert
            Assert.AreEqual("get_nearby_entities", action.Tool);
            Assert.AreEqual(args, action.Args);
            Assert.AreEqual(50f, action.Args["radius"]);
        }

        #endregion
    }
}
