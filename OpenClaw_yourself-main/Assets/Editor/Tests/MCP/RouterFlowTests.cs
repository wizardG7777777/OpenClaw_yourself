using System.Collections.Generic;
using System.Reflection;
using MCP.Core;
using MCP.Entity;
using MCP.Router;
using NUnit.Framework;
using UnityEngine;

namespace MCP.Tests.Router
{
    public class RouterFlowTests
    {
        private GameObject routerGo;
        private MCPRouter router;
        private GameObject registryGo;
        private EntityRegistry registry;
        private GameObject player;

        [SetUp]
        public void SetUp()
        {
            registryGo = new GameObject("EntityRegistry");
            registry = registryGo.AddComponent<EntityRegistry>();
            BindRegistryInstance(registry);

            routerGo = new GameObject("MCPRouter");
            router = routerGo.AddComponent<MCPRouter>();
            BindRouterRegistry(router);

            player = new GameObject("Player");
            player.tag = "Player";
            player.transform.position = Vector3.zero;
        }

        [TearDown]
        public void TearDown()
        {
            if (player != null) Object.DestroyImmediate(player);
            if (routerGo != null) Object.DestroyImmediate(routerGo);
            if (registryGo != null) Object.DestroyImmediate(registryGo);

            var remaining = Object.FindObjectsByType<EntityIdentity>(FindObjectsSortMode.None);
            foreach (var e in remaining)
                Object.DestroyImmediate(e.gameObject);
        }

        [Test]
        public void Route_QueryTool_ReturnsOkWithData()
        {
            var response = router.Route(new MCPRequest
            {
                Tool = "get_inventory",
                Args = new Dictionary<string, object>()
            });

            Assert.IsTrue(response.Ok);
            var data = response.Data as Dictionary<string, object>;
            Assert.IsNotNull(data);
            Assert.AreEqual("get_inventory", data["tool"]);
        }

        [Test]
        public void Route_ActionTool_WithResolvedTarget_ReturnsRunning()
        {
            var tvGo = CreateEntity("tv_01", "电视", Vector3.one);
            try
            {
                var response = router.Route(new MCPRequest
                {
                    Tool = "move_to",
                    Args = new Dictionary<string, object> { { "target_id", "tv_01" } }
                });

                Assert.IsTrue(response.Ok);
                Assert.AreEqual("running", response.Status);
                Assert.IsNotNull(response.ActionId);
                Assert.AreEqual("tv_01", router.GetCurrentAction().Target.EntityId);
            }
            finally
            {
                Object.DestroyImmediate(tvGo);
            }
        }

        [Test]
        public void Route_ExclusiveAction_LastWriteWins_ReturnsCancelledActionId()
        {
            var tv1 = CreateEntity("tv_01", "电视1", new Vector3(1f, 0f, 0f));
            var tv2 = CreateEntity("tv_02", "电视2", new Vector3(2f, 0f, 0f));
            try
            {
                var first = router.Route(new MCPRequest
                {
                    Tool = "move_to",
                    Args = new Dictionary<string, object> { { "target_id", "tv_01" } }
                });

                var second = router.Route(new MCPRequest
                {
                    Tool = "move_to",
                    Args = new Dictionary<string, object> { { "target_id", "tv_02" } }
                });

                Assert.IsTrue(first.Ok);
                Assert.IsTrue(second.Ok);
                Assert.AreEqual(first.ActionId, second.CancelledActionId);
            }
            finally
            {
                Object.DestroyImmediate(tv1);
                Object.DestroyImmediate(tv2);
            }
        }

        [Test]
        public void Route_TargetNotFound_ReturnsError()
        {
            var response = router.Route(new MCPRequest
            {
                Tool = "move_to",
                Args = new Dictionary<string, object> { { "target_id", "missing_entity" } }
            });

            Assert.IsFalse(response.Ok);
            Assert.IsNotNull(response.Error);
            Assert.AreEqual(ErrorCodes.TARGET_NOT_FOUND, response.Error.Code);
        }

        [Test]
        public void Route_AmbiguousTarget_ReturnsCandidates()
        {
            var door1 = CreateEntity("door_01", "前门", new Vector3(1f, 0f, 0f), new[] { "门" });
            var door2 = CreateEntity("door_02", "后门", new Vector3(5f, 0f, 0f), new[] { "门" });
            try
            {
                var response = router.Route(new MCPRequest
                {
                    Tool = "move_to",
                    Args = new Dictionary<string, object> { { "target_id", "门" } }
                });

                Assert.IsFalse(response.Ok);
                Assert.IsNotNull(response.Error);
                Assert.AreEqual(ErrorCodes.AMBIGUOUS_TARGET, response.Error.Code);
                Assert.IsNotNull(response.Error.Details);
                Assert.IsTrue(response.Error.Details.ContainsKey("candidates"));
            }
            finally
            {
                Object.DestroyImmediate(door1);
                Object.DestroyImmediate(door2);
            }
        }

        [Test]
        public void Route_Update_TimesOutRunningAction()
        {
            var tvGo = CreateEntity("tv_01", "电视", Vector3.one);
            try
            {
                router.Route(new MCPRequest
                {
                    Tool = "move_to",
                    Args = new Dictionary<string, object> { { "target_id", "tv_01" } }
                });

                var current = router.GetCurrentAction();
                Assert.IsNotNull(current);
                current.CreatedAt = Time.time - 100f;
                current.Timeout = 1f;

                InvokePrivateUpdate(router);

                Assert.AreEqual(ActionStatus.Failed, current.Status);
                Assert.AreEqual(ErrorCodes.ACTION_TIMEOUT, current.ErrorCode);
            }
            finally
            {
                Object.DestroyImmediate(tvGo);
            }
        }

        [Test]
        public void Route_UseToolOn_PreservesToolIdInActionArgs()
        {
            var tvGo = CreateEntity("tv_01", "电视", Vector3.one);
            try
            {
                var response = router.Route(new MCPRequest
                {
                    Tool = "use_tool_on",
                    Args = new Dictionary<string, object>
                    {
                        { "target_id", "tv_01" },
                        { "tool_id", "wrench" }
                    }
                });

                Assert.IsTrue(response.Ok);
                var args = router.GetCurrentAction().Result as Dictionary<string, object>;
                Assert.IsNotNull(args);
                Assert.AreEqual("wrench", args["tool_id"]);
            }
            finally
            {
                Object.DestroyImmediate(tvGo);
            }
        }

        private static GameObject CreateEntity(string id, string displayName, Vector3 position, string[] aliases = null)
        {
            var go = new GameObject(id);
            go.transform.position = position;
            var identity = go.AddComponent<EntityIdentity>();
            identity.entityId = id;
            identity.displayName = displayName;
            identity.aliases = aliases;
            EntityRegistry.Instance?.Register(identity);
            return go;
        }

        private static void BindRegistryInstance(EntityRegistry instance)
        {
            typeof(EntityRegistry)
                .GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetSetMethod(true)
                ?.Invoke(null, new object[] { instance });
        }

        private static void BindRouterRegistry(MCPRouter target)
        {
            var toolRegistry = new ToolRegistry();
            toolRegistry.RegisterMVPTools();

            typeof(MCPRouter)
                .GetProperty("Registry", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.SetValue(target, toolRegistry);
        }

        private static void InvokePrivateUpdate(MCPRouter target)
        {
            typeof(MCPRouter)
                .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(target, null);
        }
    }
}
