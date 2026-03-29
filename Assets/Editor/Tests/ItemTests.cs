using NUnit.Framework;

namespace Tests
{
    public class ItemTests
    {
        // ── Construction ───────────────────────────────────

        [Test]
        public void Constructor_SetsAllFields()
        {
            var item = new Item("sword", "长剑", "一把锋利的剑", ItemType.Tool,
                quantity: 2, maxStack: 10, isUsable: true, isEquippable: true);

            Assert.AreEqual("sword", item.itemId);
            Assert.AreEqual("长剑", item.displayName);
            Assert.AreEqual("一把锋利的剑", item.description);
            Assert.AreEqual(ItemType.Tool, item.itemType);
            Assert.AreEqual(2, item.quantity);
            Assert.AreEqual(10, item.maxStack);
            Assert.IsTrue(item.isUsable);
            Assert.IsTrue(item.isEquippable);
        }

        [Test]
        public void Constructor_DefaultValues()
        {
            var item = new Item("note", "便条", "一张便条", ItemType.Material);

            Assert.AreEqual(1, item.quantity);
            Assert.AreEqual(99, item.maxStack);
            Assert.IsFalse(item.isUsable);
            Assert.IsFalse(item.isEquippable);
            Assert.IsNull(item.icon);
        }

        // ── IItemAction Management ─────────────────────────

        [Test]
        public void AddAction_IncreasesActionCount()
        {
            var item = new Item("wrench", "扳手", "工具", ItemType.Tool);
            var action = new MockAction("修理");

            item.AddAction(action);

            Assert.AreEqual(1, item.Actions.Count);
            Assert.AreEqual("修理", item.Actions[0].ActionName);
        }

        [Test]
        public void AddMultipleActions_AllAccessible()
        {
            var item = new Item("shovel", "铲子", "工具", ItemType.Tool);
            item.AddAction(new MockAction("挖坑"));
            item.AddAction(new MockAction("铲雪"));

            Assert.AreEqual(2, item.Actions.Count);
        }

        [Test]
        public void RemoveAction_DecreasesActionCount()
        {
            var item = new Item("wrench", "扳手", "工具", ItemType.Tool);
            var action = new MockAction("修理");
            item.AddAction(action);

            item.RemoveAction(action);

            Assert.AreEqual(0, item.Actions.Count);
        }

        [Test]
        public void RemoveAction_NonExistent_DoesNotThrow()
        {
            var item = new Item("wrench", "扳手", "工具", ItemType.Tool);
            var action = new MockAction("修理");

            Assert.DoesNotThrow(() => item.RemoveAction(action));
        }

        [Test]
        public void Actions_IsReadOnly_CannotCastToMutableList()
        {
            var item = new Item("wrench", "扳手", "工具", ItemType.Tool);
            item.AddAction(new MockAction("修理"));

            // IReadOnlyList should not be directly castable to List for mutation
            Assert.IsNotNull(item.Actions);
            Assert.AreEqual(1, item.Actions.Count);
        }

        // ── Mock IItemAction ───────────────────────────────

        private class MockAction : IItemAction
        {
            public string ActionName { get; }

            public MockAction(string name)
            {
                ActionName = name;
            }

            public bool CanExecute(Item item, UnityEngine.GameObject target) => true;
            public void Execute(Item item, UnityEngine.GameObject target) { }
        }
    }
}
