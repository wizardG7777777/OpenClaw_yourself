using NUnit.Framework;
using MCP.Gateway;
using MCP.Router;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MCP.Tests.Gateway
{
    /// <summary>
    /// Tests for MCP Gateway layer (RequestValidator, ValidationResult, etc.)
    /// </summary>
    public class GatewayTests
    {
        private GameObject routerObject;
        private GameObject gatewayObject;

        [TearDown]
        public void TearDown()
        {
            if (gatewayObject != null)
                Object.DestroyImmediate(gatewayObject);

            if (routerObject != null)
                Object.DestroyImmediate(routerObject);

            gatewayObject = null;
            routerObject = null;
        }

        private MCPGateway CreateGateway()
        {
            routerObject = new GameObject("MCPRouter");
            var router = routerObject.AddComponent<MCPRouter>();

            var registry = new ToolRegistry();
            registry.RegisterMVPTools();

            typeof(MCPRouter)
                .GetProperty("Registry", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.SetValue(router, registry);

            gatewayObject = new GameObject("MCPGateway");
            var gateway = gatewayObject.AddComponent<MCPGateway>();

            typeof(MCPGateway)
                .GetField("router", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(gateway, router);

            return gateway;
        }

        #region RequestValidator Structure Tests

        [Test]
        public void RequestValidator_ValidateStructure_ValidRequest_ReturnsSuccess()
        {
            // Arrange
            var json = JObject.Parse(@"{ ""tool"": ""move_to"", ""args"": { ""target_id"": ""tv_01"" } }");

            // Act
            var result = RequestValidator.ValidateStructure(json);

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.IsNull(result.ErrorField);
            Assert.IsNull(result.ErrorReason);
        }

        [Test]
        public void RequestValidator_ValidateStructure_MinimalValidRequest_ReturnsSuccess()
        {
            // Arrange - only required field
            var json = JObject.Parse(@"{ ""tool"": ""get_inventory"" }");

            // Act
            var result = RequestValidator.ValidateStructure(json);

            // Assert
            Assert.IsTrue(result.IsValid);
            // Args should be auto-injected as empty object
            Assert.IsNotNull(json["args"]);
            Assert.AreEqual(JTokenType.Object, json["args"].Type);
        }

        [Test]
        public void RequestValidator_ValidateStructure_MissingTool_ReturnsFailure()
        {
            // Arrange
            var json = JObject.Parse(@"{ ""args"": {} }");

            // Act
            var result = RequestValidator.ValidateStructure(json);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("tool", result.ErrorField);
            Assert.IsTrue(result.ErrorReason.Contains("tool"));
        }

        [Test]
        public void RequestValidator_ValidateStructure_NullTool_ReturnsFailure()
        {
            // Arrange
            var json = JObject.Parse(@"{ ""tool"": null, ""args"": {} }");

            // Act
            var result = RequestValidator.ValidateStructure(json);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("tool", result.ErrorField);
        }

        [Test]
        public void RequestValidator_ValidateStructure_EmptyTool_ReturnsFailure()
        {
            // Arrange
            var json = JObject.Parse(@"{ ""tool"": """", ""args"": {} }");

            // Act
            var result = RequestValidator.ValidateStructure(json);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("tool", result.ErrorField);
            Assert.IsTrue(result.ErrorReason.Contains("empty"));
        }

        [Test]
        public void RequestValidator_ValidateStructure_WhitespaceTool_ReturnsFailure()
        {
            // Arrange
            var json = JObject.Parse(@"{ ""tool"": ""   "", ""args"": {} }");

            // Act
            var result = RequestValidator.ValidateStructure(json);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("tool", result.ErrorField);
        }

        [Test]
        public void RequestValidator_ValidateStructure_NonStringTool_ReturnsFailure()
        {
            // Arrange
            var json = JObject.Parse(@"{ ""tool"": 123, ""args"": {} }");

            // Act
            var result = RequestValidator.ValidateStructure(json);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("tool", result.ErrorField);
        }

        [Test]
        public void RequestValidator_ValidateStructure_ArgsAsArray_ReturnsFailure()
        {
            // Arrange
            var json = JObject.Parse(@"{ ""tool"": ""move_to"", ""args"": [] }");

            // Act
            var result = RequestValidator.ValidateStructure(json);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("args", result.ErrorField);
            Assert.IsTrue(result.ErrorReason.Contains("object"));
        }

        [Test]
        public void RequestValidator_ValidateStructure_ArgsAsString_ReturnsFailure()
        {
            // Arrange
            var json = JObject.Parse(@"{ ""tool"": ""move_to"", ""args"": ""invalid"" }");

            // Act
            var result = RequestValidator.ValidateStructure(json);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("args", result.ErrorField);
        }

        [Test]
        public void RequestValidator_ValidateStructure_ArgsAsNumber_ReturnsFailure()
        {
            // Arrange
            var json = JObject.Parse(@"{ ""tool"": ""move_to"", ""args"": 42 }");

            // Act
            var result = RequestValidator.ValidateStructure(json);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("args", result.ErrorField);
        }

        [Test]
        public void MCPGateway_ProcessRequest_GetInventory_ReturnsSuccess()
        {
            // Arrange
            var gateway = CreateGateway();

            // Act
            var response = gateway.ProcessRequest(@"{ ""tool"": ""get_inventory"", ""args"": {} }");

            // Assert
            Assert.IsTrue(response.Ok);
            Assert.IsNull(response.Error);

            var data = response.Data as Dictionary<string, object>;
            Assert.IsNotNull(data);
            Assert.AreEqual("get_inventory", data["tool"]);

            var args = data["args"] as Dictionary<string, object>;
            Assert.IsNotNull(args);
            Assert.AreEqual(0, args.Count);
        }

        [Test]
        public void MCPGateway_ProcessRequest_MalformedJson_ReturnsInvalidParams()
        {
            // Arrange
            var gateway = CreateGateway();

            // Act
            var response = gateway.ProcessRequest(@"{ ""tool"": ""get_inventory"", ""args"": ");

            // Assert
            Assert.IsFalse(response.Ok);
            Assert.IsNotNull(response.Error);
            Assert.AreEqual("INVALID_PARAMS", response.Error.Code);
            Assert.IsTrue(response.Error.Message.Contains("Malformed JSON"));
        }

        [Test]
        public void MCPGateway_ProcessRequest_ExceedsQpsLimit_ReturnsRateLimited()
        {
            // Arrange
            var gateway = CreateGateway();
            typeof(MCPGateway)
                .GetField("qpsLimit", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(gateway, 1f);

            // Act
            var first = gateway.ProcessRequest(@"{ ""tool"": ""get_inventory"", ""args"": {} }");
            var second = gateway.ProcessRequest(@"{ ""tool"": ""get_inventory"", ""args"": {} }");

            // Assert
            Assert.IsTrue(first.Ok);
            Assert.IsFalse(second.Ok);
            Assert.IsNotNull(second.Error);
            Assert.AreEqual("RATE_LIMITED", second.Error.Code);
        }

        #endregion

        #region RequestValidator Tool Exists Tests

        [Test]
        public void RequestValidator_ValidateToolExists_ExistingTool_ReturnsSuccess()
        {
            // Arrange
            var allowedTools = new HashSet<string> { "move_to", "interact_with", "get_inventory" };

            // Act
            var result = RequestValidator.ValidateToolExists("move_to", allowedTools);

            // Assert
            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void RequestValidator_ValidateToolExists_NonExistingTool_ReturnsFailure()
        {
            // Arrange
            var allowedTools = new HashSet<string> { "move_to", "interact_with" };

            // Act
            var result = RequestValidator.ValidateToolExists("invalid_tool", allowedTools);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("tool", result.ErrorField);
            Assert.IsTrue(result.ErrorReason.Contains("Unknown tool"));
        }

        [Test]
        public void RequestValidator_ValidateToolExists_NullAllowedTools_ReturnsFailure()
        {
            // Act
            var result = RequestValidator.ValidateToolExists("move_to", null);

            // Assert
            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void RequestValidator_ValidateToolExists_EmptyAllowedTools_ReturnsFailure()
        {
            // Arrange
            var allowedTools = new HashSet<string>();

            // Act
            var result = RequestValidator.ValidateToolExists("move_to", allowedTools);

            // Assert
            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void RequestValidator_ValidateToolExists_CaseSensitive()
        {
            // Arrange
            var allowedTools = new HashSet<string> { "move_to" };

            // Act - different casing
            var result = RequestValidator.ValidateToolExists("MOVE_TO", allowedTools);

            // Assert - should fail because tool names are case sensitive
            Assert.IsFalse(result.IsValid);
        }

        #endregion

        #region ValidationResult Tests

        [Test]
        public void ValidationResult_Success_CreatesValidResult()
        {
            // Act
            var result = ValidationResult.Success();

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.IsNull(result.ErrorField);
            Assert.IsNull(result.ErrorValue);
            Assert.IsNull(result.ErrorReason);
            Assert.IsNull(result.Suggestion);
        }

        [Test]
        public void ValidationResult_Failure_CreatesInvalidResult()
        {
            // Act
            var result = ValidationResult.Failure(
                field: "tool",
                value: "invalid",
                reason: "Unknown tool",
                suggestion: "Use a valid tool name"
            );

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("tool", result.ErrorField);
            Assert.AreEqual("invalid", result.ErrorValue);
            Assert.AreEqual("Unknown tool", result.ErrorReason);
            Assert.AreEqual("Use a valid tool name", result.Suggestion);
        }

        [Test]
        public void ValidationResult_Failure_WithoutSuggestion()
        {
            // Act
            var result = ValidationResult.Failure(
                field: "args",
                value: "invalid",
                reason: "Args must be an object"
            );

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("args", result.ErrorField);
            Assert.IsNull(result.Suggestion);
        }

        [Test]
        public void ValidationResult_Failure_WithNullValue()
        {
            // Act
            var result = ValidationResult.Failure(
                field: "tool",
                value: null,
                reason: "Tool is required"
            );

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("tool", result.ErrorField);
            Assert.IsNull(result.ErrorValue);
        }

        #endregion

        #region Integration Scenarios

        [Test]
        public void RequestValidator_FullValidation_ValidRequest_Passes()
        {
            // Arrange
            var json = JObject.Parse(@"{
                ""tool"": ""move_to"",
                ""args"": {
                    ""target_id"": ""tv_01"",
                    ""timeout"": 30
                }
            }");
            var allowedTools = new HashSet<string> { "move_to", "interact_with" };

            // Act
            var structureResult = RequestValidator.ValidateStructure(json);
            var toolResult = RequestValidator.ValidateToolExists(json["tool"].Value<string>(), allowedTools);

            // Assert
            Assert.IsTrue(structureResult.IsValid, "Structure should be valid");
            Assert.IsTrue(toolResult.IsValid, "Tool should be valid");
        }

        [Test]
        public void RequestValidator_FullValidation_InvalidTool_FailsAtToolCheck()
        {
            // Arrange
            var json = JObject.Parse(@"{ ""tool"": ""invalid_tool"", ""args"": {} }");
            var allowedTools = new HashSet<string> { "move_to", "interact_with" };

            // Act
            var structureResult = RequestValidator.ValidateStructure(json);
            var toolResult = RequestValidator.ValidateToolExists(json["tool"].Value<string>(), allowedTools);

            // Assert
            Assert.IsTrue(structureResult.IsValid, "Structure is valid");
            Assert.IsFalse(toolResult.IsValid, "Tool should be invalid");
        }

        [Test]
        public void RequestValidator_FullValidation_InvalidStructure_FailsAtStructureCheck()
        {
            // Arrange
            var json = JObject.Parse(@"{ ""tool"": 123 }");
            var allowedTools = new HashSet<string> { "move_to" };

            // Act
            var structureResult = RequestValidator.ValidateStructure(json);

            // Assert
            Assert.IsFalse(structureResult.IsValid, "Structure should be invalid");
            // Should not even reach tool validation due to structure failure
        }

        [Test]
        public void RequestValidator_ComplexArgsStructure_ValidatesCorrectly()
        {
            // Arrange - nested args object
            var json = JObject.Parse(@"{
                ""tool"": ""move_to"",
                ""args"": {
                    ""target_id"": ""tv_01"",
                    ""options"": {
                        ""speed"": ""walk"",
                        ""avoid_obstacles"": true
                    },
                    ""coordinates"": [1, 2, 3]
                }
            }");

            // Act
            var result = RequestValidator.ValidateStructure(json);

            // Assert
            Assert.IsTrue(result.IsValid, "Complex nested args should be valid");
        }

        #endregion

        #region Edge Cases

        [Test]
        public void RequestValidator_ValidateStructure_EmptyJsonObject_ReturnsFailure()
        {
            // Arrange
            var json = new JObject();

            // Act
            var result = RequestValidator.ValidateStructure(json);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("tool", result.ErrorField);
        }

        [Test]
        public void RequestValidator_ValidateStructure_ArgsWithNullValues_IsValid()
        {
            // Arrange
            var json = JObject.Parse(@"{ ""tool"": ""move_to"", ""args"": { ""target_id"": null } }");

            // Act
            var result = RequestValidator.ValidateStructure(json);

            // Assert - structure-wise this is valid (null values are allowed in args)
            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void RequestValidator_ValidateToolExists_NullToolName_ReturnsFailure()
        {
            // Arrange
            var allowedTools = new HashSet<string> { "move_to" };

            // Act
            var result = RequestValidator.ValidateToolExists(null, allowedTools);

            // Assert
            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void RequestValidator_ValidateToolExists_EmptyToolName_ReturnsFailure()
        {
            // Arrange
            var allowedTools = new HashSet<string> { "move_to" };

            // Act
            var result = RequestValidator.ValidateToolExists("", allowedTools);

            // Assert
            Assert.IsFalse(result.IsValid);
        }

        #endregion
    }
}
