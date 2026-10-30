using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HaiTNDev.SpritePrefabGenerator
{
    static class PrefabGeneratorMenu
    {
        const string SettingsFolder = "Assets/Settings/SpritePrefabGenerator";
        const string FolderMenu = "Assets/Sprite Prefab Generator/";

        [MenuItem("Tools/Sprite Prefab Generator/Generate Missing (All Profiles)")]
        static void GenerateAllProfiles()
        {
            var profiles = AssetDatabase.FindAssets("t:" + nameof(PrefabGeneratorSettings))
                .Select(guid => AssetDatabase.LoadAssetAtPath<PrefabGeneratorSettings>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(p => p != null)
                .ToList();
            if (profiles.Count == 0)
            {
                EditorUtility.DisplayDialog("Sprite Prefab Generator",
                    "Chưa có profile nào. Chuột phải một thư mục > Sprite Prefab Generator > Create Settings For Folder.", "OK");
                return;
            }
            foreach (var profile in profiles)
                PrefabGenerator.Generate(profile, overwrite: false);
        }

        [MenuItem(FolderMenu + "Create Settings For Folder")]
        static void CreateSettingsForFolder()
        {
            var folder = (DefaultAsset)Selection.activeObject;
            string folderName = folder.name;

            var settings = ScriptableObject.CreateInstance<PrefabGeneratorSettings>();
            settings.sourceFolder = folder;
            settings.outputFolder = $"Assets/Prefabs/{folderName}";

            PrefabGenerator.EnsureFolder(SettingsFolder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{SettingsFolder}/{folderName}.asset");
            AssetDatabase.CreateAsset(settings, path);
            AssetDatabase.SaveAssets();

            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        [MenuItem(FolderMenu + "Quick Generate (Default Settings)")]
        static void QuickGenerate()
        {
            var folder = (DefaultAsset)Selection.activeObject;
            var settings = ScriptableObject.CreateInstance<PrefabGeneratorSettings>();
            try
            {
                settings.name = folder.name;
                settings.sourceFolder = folder;
                settings.outputFolder = $"Assets/Prefabs/{folder.name}";

                var warnings = new List<string>();
                var jobs = PrefabGenerator.Plan(settings, warnings);
                int newCount = jobs.Count(j => !j.PrefabExists);
                Debug.Log($"[SpritePrefabGenerator] Quick Generate {folder.name}: {PrefabGenerator.Describe(jobs, warnings)}");

                if (newCount == 0)
                {
                    EditorUtility.DisplayDialog("Quick Generate", "Không có prefab mới nào để tạo. Xem Console để biết chi tiết.", "OK");
                    return;
                }
                if (!EditorUtility.DisplayDialog("Quick Generate",
                        $"Sẽ tạo {newCount} prefab mới trong {settings.outputFolder}.\nDanh sách chi tiết đã ghi ra Console.",
                        "Tạo", "Huỷ"))
                    return;

                var result = PrefabGenerator.Execute(settings, jobs, overwrite: false);
                Debug.Log($"[SpritePrefabGenerator] Quick Generate {folder.name}: {result}.");
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [MenuItem(FolderMenu + "Create Settings For Folder", true)]
        [MenuItem(FolderMenu + "Quick Generate (Default Settings)", true)]
        static bool IsFolderSelected() =>
            Selection.activeObject is DefaultAsset &&
            AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(Selection.activeObject));
    }
}
