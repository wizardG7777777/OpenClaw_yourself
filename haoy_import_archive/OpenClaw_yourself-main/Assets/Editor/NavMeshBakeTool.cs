using UnityEditor;
using UnityEngine;
using Unity.AI.Navigation;

public static class NavMeshBakeTool
{
    [MenuItem("Tools/Bake NavMesh Surface")]
    public static void BakeNavMesh()
    {
        var surface = Object.FindAnyObjectByType<NavMeshSurface>();
        if (surface == null)
        {
            Debug.LogError("[NavMeshBakeTool] No NavMeshSurface found in scene.");
            return;
        }
        surface.BuildNavMesh();
        Debug.Log($"[NavMeshBakeTool] NavMesh baked successfully on '{surface.gameObject.name}'.");
        EditorUtility.SetDirty(surface);
    }
}
