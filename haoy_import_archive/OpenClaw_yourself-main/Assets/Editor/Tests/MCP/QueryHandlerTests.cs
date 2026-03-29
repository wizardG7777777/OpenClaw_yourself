using System.Collections.Generic;
using System.Reflection;
using MCP.Core;
using MCP.Entity;
using MCP.Executor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MCP.Tests.Executor
{
    public class QueryHandlerTests
    {
        private GameObject registryGo;
        private EntityRegistry registry;
        private GameObject player;
        private GameObject executionManagerGo;

        [SetUp]
        public void SetUp()
        {
            registryGo = new GameObject("EntityRegistry");
            registry = registryGo.AddComponent<EntityRegistry>();
            BindRegistryInstance(registry);
        }

        [TearDown]
        public void TearDown()
        {
            if (executionManagerGo != null) Object.DestroyImmediate(executionManagerGo);
            if (player != null) Object.DestroyImmediate(player);
            if (registryGo != null) Object.DestroyImmediate(registryGo);

            var remaining = Object.FindObjectsByType<EntityIdentity>(FindObjectsSortMode.None);
            foreach (var e in remaining)
                Object.DestroyImmediate(e.gameObject);
        }

        [Test]
        public void GetPlayerStateHandler_NoPlayer_ReturnsTargetNotFound()
        {
            var response = GetPlayerStateHandler.Handle(new MCPRequest { Tool = "get_player_state" });

            Assert.IsFalse(response.Ok);
            Assert.IsNotNull(response.Error);
            Assert.AreEqual(ErrorCodes.TARGET_NOT_FOUND, response.Error.Code);
        }

        [Test]
        public void GetPlayerStateHandler_WithPlayer_ReturnsExpectedShape()
        {
            player = new GameObject("Player");
            player.tag = "Player";
            player.transform.position = new Vector3(1f, 2f, 3f);
            player.transform.eulerAngles = new Vector3(0f, 90f, 0f);

            executionManagerGo = new GameObject("ExecutionManager");
            executionManagerGo.AddComponent<ExecutionManager>();

            var response = GetPlayerStateHandler.Handle(new MCPRequest { Tool = "get_player_state" });

            Assert.IsTrue(response.Ok);
            var data = response.Data as Dictionary<string, object>;
            Assert.IsNotNull(data);
            Assert.IsTrue(data.ContainsKey("position"));
            Assert.IsTrue(data.ContainsKey("rotation"));
            Assert.IsTrue(data.ContainsKey("scene"));
            Assert.IsTrue(data.ContainsKey("has_active_action"));
        }

        [Test]
        public void GetNearbyEntitiesHandler_AppliesRadiusTypeAndInteractableFilter()
        {
            player = new GameObject("Player");
            player.tag = "Player";
            player.transform.position = Vector3.zero;

            CreateEntity("near_npc", "近处 NPC", "npc", true, new Vector3(2f, 0f, 0f));
            CreateEntity("near_non_interactable", "不可交互物体", "npc", false, new Vector3(1f, 0f, 0f));
            CreateEntity("far_npc", "远处 NPC", "npc", true, new Vector3(100f, 0f, 0f));
            CreateEntity("near_item", "近处物品", "item", true, new Vector3(2f, 0f, 1f));

            var request = new MCPRequest
            {
                Tool = "get_nearby_entities",
                Args = new Dictionary<string, object>
                {
                    { "radius", 10f },
                    { "interactable_only", true },
                    { "entity_types", JArray.Parse("[\"npc\"]") }
                }
            };

            var response = GetNearbyEntitiesHandler.Handle(request);
            Assert.IsTrue(response.Ok);

            var data = response.Data as Dictionary<string, object>;
            Assert.IsNotNull(data);
            var entities = data["entities"] as List<Dictionary<string, object>>;
            Assert.IsNotNull(entities);
            Assert.AreEqual(1, entities.Count);
            Assert.AreEqual("near_npc", entities[0]["entity_id"]);
        }

        [Test]
        public void GetWorldSummaryHandler_ReturnsAggregatedFields()
        {
            player = new GameObject("Player");
            player.tag = "Player";
            player.transform.position = new Vector3(5f, 0f, 5f);
            CreateEntity("npc_01", "小明", "npc", true, new Vector3(6f, 0f, 5f));

            executionManagerGo = new GameObject("ExecutionManager");
            executionManagerGo.AddComponent<ExecutionManager>();

            var response = GetWorldSummaryHandler.Handle(new MCPRequest { Tool = "get_world_summary" });

            Assert.IsTrue(response.Ok);
            var data = response.Data as Dictionary<string, object>;
            Assert.IsNotNull(data);
            Assert.IsTrue(data.ContainsKey("scene"));
            Assert.IsTrue(data.ContainsKey("player_position"));
            Assert.IsTrue(data.ContainsKey("nearby_entity_count"));
            Assert.IsTrue(data.ContainsKey("has_active_action"));
            Assert.AreEqual(1, data["nearby_entity_count"]);
        }

        private void CreateEntity(string id, string displayName, string entityType, bool interactable, Vector3 position)
        {
            var go = new GameObject(id);
            go.transform.position = position;
            var identity = go.AddComponent<EntityIdentity>();
            identity.entityId = id;
            identity.displayName = displayName;
            identity.entityType = entityType;
            identity.interactable = interactable;
            registry.Register(identity);
        }

        private static void BindRegistryInstance(EntityRegistry instance)
        {
            typeof(EntityRegistry)
                .GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetSetMethod(true)
                ?.Invoke(null, new object[] { instance });
        }
    }
}
