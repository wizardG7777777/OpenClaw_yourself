using UnityEngine;

namespace MCP.Entity
{
    public class EntityIdentity : MonoBehaviour
    {
        [Tooltip("Base entity ID (e.g. 'table_lamp'). When autoSuffix is enabled, runtime ID becomes 'table_lamp_0', 'table_lamp_1', etc.")]
        [SerializeField] public string entityId;
        [SerializeField] public string displayName;
        [SerializeField] public string[] aliases;
        [SerializeField] public string entityType;
        [SerializeField] public bool interactable = true;

        [Tooltip("When enabled, automatically appends _0, _1, _2... to entityId to avoid conflicts between multiple instances of the same prefab.")]
        [SerializeField] public bool autoSuffix = false;

        /// <summary>
        /// The actual unique ID used at runtime. Equals entityId when autoSuffix is off,
        /// or entityId + "_N" when autoSuffix is on.
        /// </summary>
        [HideInInspector] public string runtimeId;

        private void OnEnable()
        {
            if (EntityRegistry.Instance != null)
                EntityRegistry.Instance.Register(this);
        }

        private void OnDisable()
        {
            if (EntityRegistry.Instance != null)
                EntityRegistry.Instance.Unregister(this);
        }
    }
}
