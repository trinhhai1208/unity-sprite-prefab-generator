using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HaiTNDev.SpritePrefabGenerator
{
    [CustomEditor(typeof(PrefabGeneratorSettings))]
    public class PrefabGeneratorSettingsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var settings = (PrefabGeneratorSettings)target;
            EditorGUILayout.Space();

            string source = settings.SourcePath;
            if (string.IsNullOrEmpty(source) || !AssetDatabase.IsValidFolder(source))
            {
                EditorGUILayout.HelpBox("Kéo một thư mục từ Project window vào Source Folder.", MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Preview"))
                    LogPreview(settings);
                if (GUILayout.Button("Generate Missing"))
                    PrefabGenerator.Generate(settings, overwrite: false);
                if (GUILayout.Button("Rebuild All") && ConfirmRebuild(settings))
                    PrefabGenerator.Generate(settings, overwrite: true);
            }
        }

        internal static void LogPreview(PrefabGeneratorSettings settings)
        {
            var warnings = new List<string>();
            var jobs = PrefabGenerator.Plan(settings, warnings);
            Debug.Log($"[SpritePrefabGenerator] Preview {settings.name}: {PrefabGenerator.Describe(jobs, warnings)}", settings);
        }

        internal static bool ConfirmRebuild(PrefabGeneratorSettings settings) =>
            EditorUtility.DisplayDialog("Rebuild All",
                $"Ghi đè mọi prefab (và animation tự tạo) của profile \"{settings.name}\" trong {settings.outputFolder}?\n\n" +
                "Chỉnh sửa thủ công trên các prefab đó sẽ mất. GUID được giữ nguyên nên tham chiếu trong scene không bị hỏng.",
                "Ghi đè", "Huỷ");
    }
}
