using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Tests
{
    /// <summary>
    /// Tests for UIManager panel coordination: exclusive/mutual-exclusion logic.
    /// Uses lightweight MockPanel stubs instead of real MonoBehaviour panels.
    /// </summary>
    public class UIManagerTests
    {
        private GameObject managerGo;
        private UIManager manager;

        [SetUp]
        public void SetUp()
        {
            managerGo = new GameObject("UIManager");
            manager = managerGo.AddComponent<UIManager>();

            typeof(UIManager)
                .GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)
                ?.GetSetMethod(true)
                ?.Invoke(null, new object[] { manager });
        }

        [TearDown]
        public void TearDown()
        {
            if (managerGo != null)
                Object.DestroyImmediate(managerGo);

            typeof(UIManager)
                .GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)
                ?.GetSetMethod(true)
                ?.Invoke(null, new object[] { null });
        }

        // ── Basic Open/Close ───────────────────────────────

        [Test]
        public void RequestOpen_NoPanelActive_OpensSuccessfully()
        {
            var panel = new MockPanel("Inventory", exclusive: false);

            bool result = manager.RequestOpen(panel);

            Assert.IsTrue(result);
            Assert.IsTrue(panel.IsOpen);
        }

        [Test]
        public void RequestOpen_Null_ReturnsFalse()
        {
            bool result = manager.RequestOpen(null);
            Assert.IsFalse(result);
        }

        [Test]
        public void NotifyClose_ClearsActivePanel()
        {
            var panel = new MockPanel("Inventory", exclusive: false);
            manager.RequestOpen(panel);
            panel.Close();
            manager.NotifyClose(panel);

            Assert.IsFalse(manager.IsBlocked());
        }

        // ── Exclusive Panel Blocking ───────────────────────

        [Test]
        public void ExclusivePanel_BlocksOtherPanels()
        {
            var dialogue = new MockPanel("Dialogue", exclusive: true);
            var inventory = new MockPanel("Inventory", exclusive: false);

            manager.RequestOpen(dialogue);

            bool result = manager.RequestOpen(inventory);

            Assert.IsFalse(result, "Inventory should be blocked while dialogue is open");
            Assert.IsFalse(inventory.IsOpen, "Inventory should not have opened");
            Assert.IsTrue(dialogue.IsOpen, "Dialogue should remain open");
        }

        [Test]
        public void ExclusivePanel_BlocksOtherExclusivePanels()
        {
            var dialogue1 = new MockPanel("Dialogue1", exclusive: true);
            var dialogue2 = new MockPanel("Dialogue2", exclusive: true);

            manager.RequestOpen(dialogue1);

            bool result = manager.RequestOpen(dialogue2);

            Assert.IsFalse(result);
            Assert.IsTrue(dialogue1.IsOpen);
            Assert.IsFalse(dialogue2.IsOpen);
        }

        [Test]
        public void IsBlocked_WhenExclusiveOpen_ReturnsTrue()
        {
            var dialogue = new MockPanel("Dialogue", exclusive: true);
            manager.RequestOpen(dialogue);

            Assert.IsTrue(manager.IsBlocked());
        }

        [Test]
        public void IsBlocked_WhenNormalOpen_ReturnsFalse()
        {
            var inventory = new MockPanel("Inventory", exclusive: false);
            manager.RequestOpen(inventory);

            Assert.IsFalse(manager.IsBlocked());
        }

        [Test]
        public void ExclusiveClosed_ThenOtherCanOpen()
        {
            var dialogue = new MockPanel("Dialogue", exclusive: true);
            var inventory = new MockPanel("Inventory", exclusive: false);

            manager.RequestOpen(dialogue);
            dialogue.Close();
            manager.NotifyClose(dialogue);

            bool result = manager.RequestOpen(inventory);

            Assert.IsTrue(result);
            Assert.IsTrue(inventory.IsOpen);
        }

        // ── Mutual Exclusion (Normal Panels) ───────────────

        [Test]
        public void NormalPanels_AreMutuallyExclusive()
        {
            var inventory = new MockPanel("Inventory", exclusive: false);
            var creation = new MockPanel("Creation", exclusive: false);

            manager.RequestOpen(inventory);
            Assert.IsTrue(inventory.IsOpen);

            manager.RequestOpen(creation);

            Assert.IsFalse(inventory.IsOpen, "Inventory should have been closed");
            Assert.IsTrue(creation.IsOpen, "Creation should be open");
        }

        [Test]
        public void ThreeNormalPanels_OnlyLastStaysOpen()
        {
            var panelA = new MockPanel("A", exclusive: false);
            var panelB = new MockPanel("B", exclusive: false);
            var panelC = new MockPanel("C", exclusive: false);

            manager.RequestOpen(panelA);
            manager.RequestOpen(panelB);
            manager.RequestOpen(panelC);

            Assert.IsFalse(panelA.IsOpen);
            Assert.IsFalse(panelB.IsOpen);
            Assert.IsTrue(panelC.IsOpen);
        }

        [Test]
        public void ReopenSamePanel_StaysOpen()
        {
            var panel = new MockPanel("Inventory", exclusive: false);

            manager.RequestOpen(panel);
            manager.RequestOpen(panel);

            Assert.IsTrue(panel.IsOpen);
        }

        // ── Mock Panel ─────────────────────────────────────

        private class MockPanel : IUIPanel
        {
            public string Name { get; }
            public bool IsExclusive { get; }
            public bool IsOpen { get; private set; }

            public MockPanel(string name, bool exclusive)
            {
                Name = name;
                IsExclusive = exclusive;
            }

            public void Open() => IsOpen = true;
            public void Close() => IsOpen = false;
        }
    }
}
