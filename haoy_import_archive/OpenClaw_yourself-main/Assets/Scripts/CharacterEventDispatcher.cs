using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 监听后端推送的角色事件（移动、对话等），分发到 Unity 场景中的角色。
/// 挂载到场景中的管理器 GameObject 上，确保 BackendBridge 实例已存在。
/// </summary>
public class CharacterEventDispatcher : MonoBehaviour
{
    [Header("角色注册")]
    [Tooltip("场景中所有由后端控制的角色，key = character_id")]
    [SerializeField] private List<CharacterBinding> characterBindings = new();

    private readonly Dictionary<string, CharacterBinding> _lookup = new();

    [System.Serializable]
    public class CharacterBinding
    {
        public string characterId;
        public GameObject characterObject;
        [HideInInspector] public NavMeshAgent navAgent;
    }

    private void Start()
    {
        foreach (var binding in characterBindings)
        {
            if (binding.characterObject != null)
            {
                binding.navAgent = binding.characterObject.GetComponent<NavMeshAgent>();
                _lookup[binding.characterId] = binding;
            }
        }

        if (BackendBridge.Instance != null)
            BackendBridge.Instance.OnBackendEvent += OnBackendEvent;
    }

    private void OnDestroy()
    {
        if (BackendBridge.Instance != null)
            BackendBridge.Instance.OnBackendEvent -= OnBackendEvent;
    }

    private void OnBackendEvent(string eventName, Dictionary<string, object> data)
    {
        switch (eventName)
        {
            case "character_move":
                HandleCharacterMove(data);
                break;
            case "character_created":
                Debug.Log($"[EventDispatcher] 新角色已创建: {data.GetValueOrDefault("name", "")}");
                break;
            case "character_deleted":
                HandleCharacterDeleted(data);
                break;
        }
    }

    private void HandleCharacterMove(Dictionary<string, object> data)
    {
        string characterId = data.GetValueOrDefault("character_id", "")?.ToString() ?? "";
        string targetId = data.GetValueOrDefault("target_id", "")?.ToString() ?? "";

        if (!_lookup.TryGetValue(characterId, out var binding) || binding.navAgent == null)
        {
            Debug.LogWarning($"[EventDispatcher] 未找到角色 {characterId} 的绑定或 NavMeshAgent");
            return;
        }

        // 尝试通过 EntityIdentity 查找目标位置
        var targetObj = FindTargetById(targetId);
        if (targetObj == null)
        {
            Debug.LogWarning($"[EventDispatcher] 未找到移动目标: {targetId}");
            ReportMovementFailed(characterId, $"目标 {targetId} 不存在");
            return;
        }

        Vector3 destination = targetObj.transform.position;
        NavMeshPath path = new NavMeshPath();
        bool found = NavMesh.CalculatePath(binding.navAgent.transform.position, destination, NavMesh.AllAreas, path);

        if (!found || path.status == NavMeshPathStatus.PathInvalid)
        {
            Debug.LogWarning($"[EventDispatcher] 无法导航到 {targetId}");
            ReportMovementFailed(characterId, $"无法到达 {targetId}");
            return;
        }

        binding.navAgent.SetPath(path);
        StartCoroutine(WaitForArrival(binding, characterId));
        Debug.Log($"[EventDispatcher] 角色 {characterId} 开始移动到 {targetId}");
    }

    private System.Collections.IEnumerator WaitForArrival(CharacterBinding binding, string characterId)
    {
        var agent = binding.navAgent;
        float timeout = 30f;
        float elapsed = 0f;

        yield return new WaitForSeconds(0.5f);

        while (elapsed < timeout)
        {
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.5f)
            {
                Vector3 pos = binding.characterObject.transform.position;
                ReportMovementCompleted(characterId, pos);
                yield break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        ReportMovementFailed(characterId, "移动超时");
    }

    private async void ReportMovementCompleted(string characterId, Vector3 position)
    {
        if (BackendBridge.Instance == null) return;
        var pos = new Dictionary<string, object>
        {
            { "character_id", characterId },
            { "position", new Dictionary<string, object>
                {
                    { "x", position.x },
                    { "y", position.y },
                    { "z", position.z }
                }
            }
        };
        await BackendBridge.Instance.SendRequest("movement_completed", pos);
    }

    private async void ReportMovementFailed(string characterId, string error)
    {
        if (BackendBridge.Instance == null) return;
        var data = new Dictionary<string, object>
        {
            { "character_id", characterId },
            { "error", error }
        };
        await BackendBridge.Instance.SendRequest("movement_failed", data);
    }

    private void HandleCharacterDeleted(Dictionary<string, object> data)
    {
        string characterId = data.GetValueOrDefault("character_id", "")?.ToString() ?? "";
        if (_lookup.TryGetValue(characterId, out var binding))
        {
            if (binding.characterObject != null)
                Destroy(binding.characterObject);
            _lookup.Remove(characterId);
        }
    }

    /// <summary>在场景中根据 entity_id 查找 GameObject。</summary>
    private GameObject FindTargetById(string targetId)
    {
        // 优先使用 EntityIdentity 组件（MCP 系统）
        var entities = FindObjectsByType<MCP.Entity.EntityIdentity>(FindObjectsSortMode.None);
        foreach (var entity in entities)
        {
            if (entity.runtimeId == targetId || entity.entityId == targetId)
                return entity.gameObject;
        }
        // 备选：按 GameObject 名称查找
        return GameObject.Find(targetId);
    }
}
