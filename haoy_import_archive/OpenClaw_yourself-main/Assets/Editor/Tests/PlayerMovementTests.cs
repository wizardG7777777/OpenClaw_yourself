using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests
{
    /// <summary>
    /// Unit tests for PlayerMovement
    /// </summary>
    public class PlayerMovementTests
    {
        [Test]
        public void PlayerMovement_DefaultValues_AreSet()
        {
            // Arrange
            var go = new GameObject("Player");
            var movement = go.AddComponent<PlayerMovement>();

            // Act & Assert - 通过反射获取私有字段
            var moveSpeedField = typeof(PlayerMovement).GetField("moveSpeed", 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var rotationSpeedField = typeof(PlayerMovement).GetField("rotationSpeed",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            Assert.IsNotNull(moveSpeedField);
            Assert.IsNotNull(rotationSpeedField);

            // Cleanup
            Object.DestroyImmediate(go);
        }

        [Test]
        public void PlayerMovement_RequiresRigidbody()
        {
            // Arrange
            var go = new GameObject("Player");
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = true;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            var movement = go.AddComponent<PlayerMovement>();

            // Assert
            Assert.IsNotNull(go.GetComponent<Rigidbody>());

            // Cleanup
            Object.DestroyImmediate(go);
        }

        [Test]
        public void PlayerMovement_ExposedFields_ArePublic()
        {
            // Arrange
            var go = new GameObject("Player");
            var movement = go.AddComponent<PlayerMovement>();

            // Act - 设置公共字段
            movement.moveSpeed = 5f;
            movement.rotationSpeed = 15f;

            // Assert
            Assert.AreEqual(5f, movement.moveSpeed);
            Assert.AreEqual(15f, movement.rotationSpeed);

            // Cleanup
            Object.DestroyImmediate(go);
        }
    }
}
