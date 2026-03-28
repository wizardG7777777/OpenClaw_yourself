using UnityEngine;
using UnityEditor;
using UnityEngine.UI;

/// <summary>
/// Creates the CharacterItemPrefab for CharacterCreationUI
/// </summary>
public class CreateCharacterItemPrefab
{
    [MenuItem("Tools/Setup/Create Character Item Prefab")]
    public static void CreatePrefab()
    {
        // Ensure directory exists
        if (!System.IO.Directory.Exists("Assets/Prefabs"))
        {
            System.IO.Directory.CreateDirectory("Assets/Prefabs");
            AssetDatabase.Refresh();
        }

        string prefabPath = "Assets/Prefabs/CharacterItem.prefab";

        // Check if already exists
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null)
        {
            Debug.Log("[CreateCharacterItemPrefab] Prefab already exists at: " + prefabPath);
            EditorUtility.DisplayDialog("Info", "CharacterItem prefab already exists!", "OK");
            return;
        }

        // Create the prefab root
        GameObject root = new GameObject("CharacterItem");
        RectTransform rootRT = root.AddComponent<RectTransform>();
        rootRT.sizeDelta = new Vector2(0, 40);

        // Background
        Image bg = root.AddComponent<Image>();
        bg.color = new Color(0.2f, 0.2f, 0.25f, 0.8f);

        // Character Name Text
        GameObject nameGO = new GameObject("NameText");
        nameGO.transform.SetParent(root.transform, false);
        Text nameText = nameGO.AddComponent<Text>();
        nameText.text = "Character Name";
        nameText.fontSize = 14;
        nameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        nameText.color = Color.white;
        nameText.alignment = TextAnchor.MiddleLeft;

        RectTransform nameRT = nameGO.GetComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0, 0);
        nameRT.anchorMax = new Vector2(0.6f, 1);
        nameRT.pivot = new Vector2(0, 0.5f);
        nameRT.sizeDelta = Vector2.zero;
        nameRT.anchoredPosition = new Vector2(10, 0);

        // Edit Button
        GameObject editBtnGO = new GameObject("EditButton");
        editBtnGO.transform.SetParent(root.transform, false);
        Image editBg = editBtnGO.AddComponent<Image>();
        editBg.color = new Color(0.3f, 0.5f, 0.7f, 1f);

        Button editBtn = editBtnGO.AddComponent<Button>();
        ColorBlock editColors = editBtn.colors;
        editColors.highlightedColor = new Color(0.4f, 0.6f, 0.8f, 1f);
        editColors.pressedColor = new Color(0.25f, 0.45f, 0.65f, 1f);
        editBtn.colors = editColors;

        RectTransform editRT = editBtnGO.GetComponent<RectTransform>();
        editRT.anchorMin = new Vector2(0.65f, 0.1f);
        editRT.anchorMax = new Vector2(0.8f, 0.9f);
        editRT.pivot = new Vector2(0.5f, 0.5f);
        editRT.sizeDelta = Vector2.zero;

        GameObject editTextGO = new GameObject("Text");
        editTextGO.transform.SetParent(editBtnGO.transform, false);
        Text editText = editTextGO.AddComponent<Text>();
        editText.text = "编辑";
        editText.fontSize = 12;
        editText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        editText.color = Color.white;
        editText.alignment = TextAnchor.MiddleCenter;

        RectTransform editTextRT = editTextGO.GetComponent<RectTransform>();
        editTextRT.anchorMin = Vector2.zero;
        editTextRT.anchorMax = Vector2.one;
        editTextRT.pivot = new Vector2(0.5f, 0.5f);
        editTextRT.sizeDelta = Vector2.zero;

        // Delete Button
        GameObject delBtnGO = new GameObject("DeleteButton");
        delBtnGO.transform.SetParent(root.transform, false);
        Image delBg = delBtnGO.AddComponent<Image>();
        delBg.color = new Color(0.7f, 0.3f, 0.3f, 1f);

        Button delBtn = delBtnGO.AddComponent<Button>();
        ColorBlock delColors = delBtn.colors;
        delColors.highlightedColor = new Color(0.8f, 0.4f, 0.4f, 1f);
        delColors.pressedColor = new Color(0.65f, 0.25f, 0.25f, 1f);
        delBtn.colors = delColors;

        RectTransform delRT = delBtnGO.GetComponent<RectTransform>();
        delRT.anchorMin = new Vector2(0.82f, 0.1f);
        delRT.anchorMax = new Vector2(0.97f, 0.9f);
        delRT.pivot = new Vector2(0.5f, 0.5f);
        delRT.sizeDelta = Vector2.zero;

        GameObject delTextGO = new GameObject("Text");
        delTextGO.transform.SetParent(delBtnGO.transform, false);
        Text delText = delTextGO.AddComponent<Text>();
        delText.text = "删除";
        delText.fontSize = 12;
        delText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        delText.color = Color.white;
        delText.alignment = TextAnchor.MiddleCenter;

        RectTransform delTextRT = delTextGO.GetComponent<RectTransform>();
        delTextRT.anchorMin = Vector2.zero;
        delTextRT.anchorMax = Vector2.one;
        delTextRT.pivot = new Vector2(0.5f, 0.5f);
        delTextRT.sizeDelta = Vector2.zero;

        // Save as prefab
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.Refresh();
        Debug.Log("[CreateCharacterItemPrefab] Created prefab at: " + prefabPath);
        EditorUtility.DisplayDialog("Success", "CharacterItem prefab created!\nLocation: " + prefabPath, "OK");
    }
}
