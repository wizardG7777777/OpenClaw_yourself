using NUnit.Framework;
using MCP.Router;
using MCP.Core;
using System.Collections.Generic;
using System.Linq;

namespace MCP.Tests.Router
{
    /// <summary>
    /// Tests for MCP Router layer (ToolRegistry, ParameterNormalizer, ToolDefinition)
    /// </summary>
    public class RouterTests
    {
        #region ToolDefinition Tests

        [Test]
        public void ToolDefinition_Constructor_WithAllParameters_SetsCorrectly()
        {
            // Arrange & Act
            var definition = new ToolDefinition(
                toolName: "move_to",
                description: "Move to a target",
                isExclusive: true,
                defaultTimeout: 30f,
                requiredParams: new[] { "target_id" },
                optionalParams: new[] { "timeout", "speed" },
                handlerType: null
            );

            // Assert
            Assert.AreEqual("move_to", definition.ToolName);
            Assert.AreEqual("Move to a target", definition.Description);
            Assert.IsTrue(definition.IsExclusive);
            Assert.AreEqual(30f, definition.DefaultTimeout);
            Assert.AreEqual(1, definition.RequiredParams.Length);
            Assert.Contains("target_id", definition.RequiredParams);
            Assert.AreEqual(2, definition.OptionalParams.Length);
            Assert.Contains("timeout", definition.OptionalParams);
            Assert.Contains("speed", definition.OptionalParams);
        }

        [Test]
        public void ToolDefinition_Constructor_WithDefaults_UsesEmptyArrays()
        {
            // Arrange & Act
            var definition = new ToolDefinition(
                toolName: "get_inventory",
                description: "Get inventory",
                isExclusive: false
            );

            // Assert
            Assert.IsNotNull(definition.RequiredParams);
            Assert.IsNotNull(definition.OptionalParams);
            Assert.AreEqual(0, definition.RequiredParams.Length);
            Assert.AreEqual(0, definition.OptionalParams.Length);
            Assert.AreEqual(0f, definition.DefaultTimeout);
            Assert.IsNull(definition.HandlerType);
        }

        [Test]
        public void ToolDefinition_QueryTool_HasNonExclusiveFlag()
        {
            // Arrange & Act
            var definition = new ToolDefinition(
                toolName: "get_player_state",
                description: "Get player state",
                isExclusive: false
            );

            // Assert
            Assert.IsFalse(definition.IsExclusive);
        }

        [Test]
        public void ToolDefinition_ActionTool_HasExclusiveFlagAndTimeout()
        {
            // Arrange & Act
            var definition = new ToolDefinition(
                toolName: "interact_with",
                description: "Interact with entity",
                isExclusive: true,
                defaultTimeout: 10f
            );

            // Assert
            Assert.IsTrue(definition.IsExclusive);
            Assert.AreEqual(10f, definition.DefaultTimeout);
        }

        #endregion

        #region ToolRegistry Tests

        [Test]
        public void ToolRegistry_RegisterTool_AddsToRegistry()
        {
            // Arrange
            var registry = new ToolRegistry();
            var tool = new ToolDefinition("test_tool", "Test tool", isExclusive: false);

            // Act
            registry.RegisterTool(tool);

            // Assert
            Assert.IsTrue(registry.IsToolRegistered("test_tool"));
            Assert.AreEqual(tool, registry.GetTool("test_tool"));
        }

        [Test]
        public void ToolRegistry_RegisterTool_WithSameName_Overwrites()
        {
            // Arrange
            var registry = new ToolRegistry();
            var tool1 = new ToolDefinition("my_tool", "Original", isExclusive: false);
            var tool2 = new ToolDefinition("my_tool", "Updated", isExclusive: true);

            registry.RegisterTool(tool1);

            // Act
            registry.RegisterTool(tool2);

            // Assert
            var retrieved = registry.GetTool("my_tool");
            Assert.AreEqual("Updated", retrieved.Description);
            Assert.IsTrue(retrieved.IsExclusive);
        }

        [Test]
        public void ToolRegistry_RegisterNull_DoesNotCrash()
        {
            // Arrange
            var registry = new ToolRegistry();

            // Act & Assert
            Assert.DoesNotThrow(() => registry.RegisterTool(null));
        }

        [Test]
        public void ToolRegistry_RegisterEmptyName_DoesNotAdd()
        {
            // Arrange
            var registry = new ToolRegistry();
            var tool = new ToolDefinition("", "Empty name tool", isExclusive: false);

            // Act
            registry.RegisterTool(tool);

            // Assert
            Assert.IsFalse(registry.IsToolRegistered(""));
        }

        [Test]
        public void ToolRegistry_GetTool_NonExisting_ReturnsNull()
        {
            // Arrange
            var registry = new ToolRegistry();

            // Act
            var result = registry.GetTool("non_existing");

            // Assert
            Assert.IsNull(result);
        }

        [Test]
        public void ToolRegistry_GetTool_NullName_ReturnsNull()
        {
            // Arrange
            var registry = new ToolRegistry();

            // Act
            var result = registry.GetTool(null);

            // Assert
            Assert.IsNull(result);
        }

        [Test]
        public void ToolRegistry_IsToolRegistered_NullName_ReturnsFalse()
        {
            // Arrange
            var registry = new ToolRegistry();

            // Act
            var result = registry.IsToolRegistered(null);

            // Assert
            Assert.IsFalse(result);
        }

        [Test]
        public void ToolRegistry_IsToolRegistered_EmptyName_ReturnsFalse()
        {
            // Arrange
            var registry = new ToolRegistry();

            // Act
            var result = registry.IsToolRegistered("");

            // Assert
            Assert.IsFalse(result);
        }

        [Test]
        public void ToolRegistry_GetAllowedTools_ReturnsAllRegistered()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.RegisterTool(new ToolDefinition("tool_a", "Tool A", isExclusive: false));
            registry.RegisterTool(new ToolDefinition("tool_b", "Tool B", isExclusive: false));
            registry.RegisterTool(new ToolDefinition("tool_c", "Tool C", isExclusive: false));

            // Act
            var allowed = registry.GetAllowedTools();

            // Assert
            Assert.AreEqual(3, allowed.Count);
            Assert.IsTrue(allowed.Contains("tool_a"));
            Assert.IsTrue(allowed.Contains("tool_b"));
            Assert.IsTrue(allowed.Contains("tool_c"));
        }

        [Test]
        public void ToolRegistry_GetAllowedTools_EmptyRegistry_ReturnsEmpty()
        {
            // Arrange
            var registry = new ToolRegistry();

            // Act
            var allowed = registry.GetAllowedTools();

            // Assert
            Assert.AreEqual(0, allowed.Count);
        }

        [Test]
        public void ToolRegistry_RegisterMVPTools_RegistersAllNineTools()
        {
            // Arrange
            var registry = new ToolRegistry();

            // Act
            registry.RegisterMVPTools();

            // Assert - Check all 9 MVP tools are registered
            // Queries (non-exclusive)
            Assert.IsTrue(registry.IsToolRegistered("get_player_state"));
            Assert.IsTrue(registry.IsToolRegistered("get_world_summary"));
            Assert.IsTrue(registry.IsToolRegistered("get_nearby_entities"));
            Assert.IsTrue(registry.IsToolRegistered("get_inventory"));

            // Actions (exclusive)
            Assert.IsTrue(registry.IsToolRegistered("move_to"));
            Assert.IsTrue(registry.IsToolRegistered("interact_with"));
            Assert.IsTrue(registry.IsToolRegistered("use_tool_on"));
            Assert.IsTrue(registry.IsToolRegistered("talk_to_npc"));
            Assert.IsTrue(registry.IsToolRegistered("equip_item"));

            // Verify count
            Assert.AreEqual(9, registry.GetAllowedTools().Count);
        }

        [Test]
        public void ToolRegistry_MVPTools_QueryTools_AreNonExclusive()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.RegisterMVPTools();

            // Act & Assert
            var getPlayerState = registry.GetTool("get_player_state");
            Assert.IsFalse(getPlayerState.IsExclusive);

            var getWorldSummary = registry.GetTool("get_world_summary");
            Assert.IsFalse(getWorldSummary.IsExclusive);

            var getNearbyEntities = registry.GetTool("get_nearby_entities");
            Assert.IsFalse(getNearbyEntities.IsExclusive);

            var getInventory = registry.GetTool("get_inventory");
            Assert.IsFalse(getInventory.IsExclusive);
        }

        [Test]
        public void ToolRegistry_MVPTools_ActionTools_AreExclusive()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.RegisterMVPTools();

            // Act & Assert
            var moveTo = registry.GetTool("move_to");
            Assert.IsTrue(moveTo.IsExclusive);

            var interactWith = registry.GetTool("interact_with");
            Assert.IsTrue(interactWith.IsExclusive);

            var useToolOn = registry.GetTool("use_tool_on");
            Assert.IsTrue(useToolOn.IsExclusive);

            var talkToNpc = registry.GetTool("talk_to_npc");
            Assert.IsTrue(talkToNpc.IsExclusive);

            var equipItem = registry.GetTool("equip_item");
            Assert.IsTrue(equipItem.IsExclusive);
        }

        [Test]
        public void ToolRegistry_MVPTools_ActionTools_HaveCorrectTimeouts()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.RegisterMVPTools();

            // Act & Assert - verify timeouts match design document
            Assert.AreEqual(30f, registry.GetTool("move_to").DefaultTimeout);
            Assert.AreEqual(10f, registry.GetTool("interact_with").DefaultTimeout);
            Assert.AreEqual(15f, registry.GetTool("use_tool_on").DefaultTimeout);
            Assert.AreEqual(20f, registry.GetTool("talk_to_npc").DefaultTimeout);
            Assert.AreEqual(5f, registry.GetTool("equip_item").DefaultTimeout);
        }

        [Test]
        public void ToolRegistry_MVPTools_HaveCorrectRequiredParams()
        {
            // Arrange
            var registry = new ToolRegistry();
            registry.RegisterMVPTools();

            // Act & Assert
            var moveTo = registry.GetTool("move_to");
            Assert.Contains("target_id", moveTo.RequiredParams);

            var interactWith = registry.GetTool("interact_with");
            Assert.Contains("target_id", interactWith.RequiredParams);

            var useToolOn = registry.GetTool("use_tool_on");
            Assert.Contains("tool_id", useToolOn.RequiredParams);
            Assert.Contains("target_id", useToolOn.RequiredParams);

            var talkToNpc = registry.GetTool("talk_to_npc");
            Assert.Contains("npc_id", talkToNpc.RequiredParams);

            var equipItem = registry.GetTool("equip_item");
            Assert.Contains("item_id", equipItem.RequiredParams);
        }

        #endregion

        #region ParameterNormalizer Tests

        [Test]
        public void ParameterNormalizer_Normalize_WithAllRequiredParams_ReturnsNormalized()
        {
            // Arrange
            var tool = new ToolDefinition(
                "test_tool",
                "Test",
                isExclusive: false,
                requiredParams: new[] { "target_id" },
                optionalParams: new[] { "timeout" }
            );

            var args = new Dictionary<string, object>
            {
                { "target_id", "tv_01" },
                { "timeout", 30f }
            };

            // Act
            var result = ParameterNormalizer.Normalize(args, tool, out string errorParam);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsNull(errorParam);
            Assert.AreEqual("tv_01", result["target_id"]);
            Assert.AreEqual(30f, result["timeout"]);
        }

        [Test]
        public void ParameterNormalizer_Normalize_MissingRequiredParam_ReturnsNull()
        {
            // Arrange
            var tool = new ToolDefinition(
                "test_tool",
                "Test",
                isExclusive: false,
                requiredParams: new[] { "target_id", "action" }
            );

            var args = new Dictionary<string, object>
            {
                { "target_id", "tv_01" }
                // missing "action"
            };

            // Act
            var result = ParameterNormalizer.Normalize(args, tool, out string errorParam);

            // Assert
            Assert.IsNull(result);
            Assert.AreEqual("action", errorParam);
        }

        [Test]
        public void ParameterNormalizer_Normalize_NullArgs_WithNoRequiredParams_ReturnsEmptyDict()
        {
            // Arrange
            var tool = new ToolDefinition(
                "get_inventory",
                "Get inventory",
                isExclusive: false
            );

            // Act
            var result = ParameterNormalizer.Normalize(null, tool, out string errorParam);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsNull(errorParam);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void ParameterNormalizer_Normalize_AddsOptionalParamsWithNull()
        {
            // Arrange
            var tool = new ToolDefinition(
                "test_tool",
                "Test",
                isExclusive: false,
                optionalParams: new[] { "timeout", "speed" }
            );

            var args = new Dictionary<string, object>();

            // Act
            var result = ParameterNormalizer.Normalize(args, tool, out string errorParam);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.ContainsKey("timeout"));
            Assert.IsTrue(result.ContainsKey("speed"));
            Assert.IsNull(result["timeout"]);
            Assert.IsNull(result["speed"]);
        }

        [Test]
        public void ParameterNormalizer_Normalize_PreservesExistingOptionalParams()
        {
            // Arrange
            var tool = new ToolDefinition(
                "test_tool",
                "Test",
                isExclusive: false,
                optionalParams: new[] { "timeout", "speed" }
            );

            var args = new Dictionary<string, object>
            {
                { "timeout", 60f }
            };

            // Act
            var result = ParameterNormalizer.Normalize(args, tool, out string errorParam);

            // Assert
            Assert.AreEqual(60f, result["timeout"]);
            Assert.IsNull(result["speed"]);
        }

        [Test]
        public void ParameterNormalizer_Normalize_ExclusiveTool_WithValidTimeout_ParsesFloat()
        {
            // Arrange
            var tool = new ToolDefinition(
                "move_to",
                "Move",
                isExclusive: true,
                defaultTimeout: 30f,
                requiredParams: new[] { "target_id" },
                optionalParams: new[] { "timeout" }
            );

            var args = new Dictionary<string, object>
            {
                { "target_id", "tv_01" },
                { "timeout", "45" } // string that can be parsed as float
            };

            // Act
            var result = ParameterNormalizer.Normalize(args, tool, out string errorParam);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(45f, result["timeout"]);
        }

        [Test]
        public void ParameterNormalizer_Normalize_ExclusiveTool_WithInvalidTimeout_UsesDefault()
        {
            // Arrange
            var tool = new ToolDefinition(
                "move_to",
                "Move",
                isExclusive: true,
                defaultTimeout: 30f,
                requiredParams: new[] { "target_id" },
                optionalParams: new[] { "timeout" }
            );

            var args = new Dictionary<string, object>
            {
                { "target_id", "tv_01" },
                { "timeout", "invalid" }
            };

            // Act
            var result = ParameterNormalizer.Normalize(args, tool, out string errorParam);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(30f, result["timeout"]);
        }

        [Test]
        public void ParameterNormalizer_Normalize_ExclusiveTool_WithNegativeTimeout_UsesDefault()
        {
            // Arrange
            var tool = new ToolDefinition(
                "move_to",
                "Move",
                isExclusive: true,
                defaultTimeout: 30f,
                requiredParams: new[] { "target_id" },
                optionalParams: new[] { "timeout" }
            );

            var args = new Dictionary<string, object>
            {
                { "target_id", "tv_01" },
                { "timeout", -10f }
            };

            // Act
            var result = ParameterNormalizer.Normalize(args, tool, out string errorParam);

            // Assert - negative values should be rejected and default used
            Assert.IsNotNull(result);
            Assert.AreEqual(30f, result["timeout"]);
        }

        [Test]
        public void ParameterNormalizer_Normalize_RequiredParamWithNullValue_TreatedAsMissing()
        {
            // Arrange
            var tool = new ToolDefinition(
                "test_tool",
                "Test",
                isExclusive: false,
                requiredParams: new[] { "target_id" }
            );

            var args = new Dictionary<string, object>
            {
                { "target_id", null }
            };

            // Act
            var result = ParameterNormalizer.Normalize(args, tool, out string errorParam);

            // Assert
            Assert.IsNull(result);
            Assert.AreEqual("target_id", errorParam);
        }

        [Test]
        public void ParameterNormalizer_Normalize_NonExclusiveTool_IgnoresTimeoutValidation()
        {
            // Arrange
            var tool = new ToolDefinition(
                "get_player_state",
                "Get player state",
                isExclusive: false,
                optionalParams: new[] { "include_details" }
            );

            var args = new Dictionary<string, object>
            {
                { "include_details", true }
            };

            // Act
            var result = ParameterNormalizer.Normalize(args, tool, out string errorParam);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(true, result["include_details"]);
        }

        #endregion
    }
}
