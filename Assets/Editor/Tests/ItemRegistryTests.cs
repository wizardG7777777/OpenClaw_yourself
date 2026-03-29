using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Tests
{
    public class ItemRegistryTests
    {
        private GameObject registryGo;
        private ItemRegistry registry;

        [SetUp]
        public void SetUp()
        {
            registryGo = new GameObject("ItemRegistry");
            registry = registryGo.AddComponent<ItemRegistry>();

            // Force singleton binding since Awake may not fire reliably in edit-mode tests
            typeof(ItemRegistry)
                .GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)
                ?.GetSetMethod(true)
                ?.Invoke(null, new object[] { registry });
        }

        [TearDown]
        public void TearDown()
        {
            if (registryGo != null)
                Object.DestroyImmediate(registryGo);

            typeof(ItemRegistry)
                .GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)
                ?.GetSetMethod(true)
                ?.Invoke(null, new object[] { null });
        }

        // ── Registration ───────────────────────────────────

        [Test]
        public void Register_AddsItem_CanRetrieveById()
        {
            var item = new Item("key_01", "钥匙", "打开大门的钥匙", ItemType.KeyItem);
            registry.Register(item);

            var result = registry.GetById("key_01");
            Assert.IsNotNull(result);
            Assert.AreEqual("钥匙", result.displayName);
            Assert.AreEqual(ItemType.KeyItem, result.itemType);
        }

        [Test]
        public void Register_DuplicateId_OverwritesPrevious()
        {
            var item1 = new Item("tool_01", "旧锤子", "旧的", ItemType.Tool);
            var item2 = new Item("tool_01", "新锤子", "新的", ItemType.Tool);
            registry.Register(item1);
            registry.Register(item2);

            var result = registry.GetById("tool_01");
            Assert.AreEqual("新锤子", result.displayName);
        }

        // ── Query ──────────────────────────────────────────

        [Test]
        public void GetById_NonExistent_ReturnsNull()
        {
            var result = registry.GetById("does_not_exist");
            Assert.IsNull(result);
        }

        [Test]
        public void Contains_RegisteredItem_ReturnsTrue()
        {
            registry.Register(new Item("wrench", "扳手", "工具", ItemType.Tool));
            Assert.IsTrue(registry.Contains("wrench"));
        }

        [Test]
        public void Contains_UnregisteredItem_ReturnsFalse()
        {
            Assert.IsFalse(registry.Contains("phantom_item"));
        }

        [Test]
        public void GetAll_ReturnsAllRegisteredItems()
        {
            registry.Register(new Item("a", "A", "desc", ItemType.Tool));
            registry.Register(new Item("b", "B", "desc", ItemType.Consumable));
            registry.Register(new Item("c", "C", "desc", ItemType.KeyItem));

            var all = registry.GetAll().ToList();
            Assert.AreEqual(3, all.Count);
        }

        // ── Default Items ──────────────────────────────────

        [Test]
        public void DefaultItems_RegisteredAfterAwake()
        {
            // Manually invoke Awake to trigger RegisterDefaultItems
            var awakeMethod = typeof(ItemRegistry).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            awakeMethod?.Invoke(registry, null);

            Assert.IsTrue(registry.Contains("wrench"), "wrench should be registered");
            Assert.IsTrue(registry.Contains("shovel"), "shovel should be registered");
            Assert.IsTrue(registry.Contains("postcard"), "postcard should be registered");

            var postcard = registry.GetById("postcard");
            Assert.AreEqual(ItemType.KeyItem, postcard.itemType);
        }
    }
}
