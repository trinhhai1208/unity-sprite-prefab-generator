using UnityEditor;
using UnityEngine;

namespace HaiTNDev.SpritePrefabGenerator
{
    public enum SpriteSheetGrouping
    {
        /// <summary>Mọi sprite sheet trong cùng thư mục gộp thành một controller, tên theo thư mục.</summary>
        Folder,
        /// <summary>Gộp theo tiền tố tên file (phần trước ký tự tách đầu tiên), vd Knight_Idle + Knight_Run -> Knight.</summary>
        NamePrefix,
        /// <summary>Mỗi sprite sheet là một prefab riêng với một animation.</summary>
        PerTexture,
    }

    /// <summary>
    /// Một "profile" cấu hình: lấy asset từ đâu, tạo những loại prefab nào, lưu vào đâu.
    /// Tạo bằng Create > Sprite Prefab Generator > Settings, hoặc chuột phải một thư mục >
    /// Sprite Prefab Generator > Create Settings For Folder.
    /// </summary>
    [CreateAssetMenu(fileName = "PrefabGeneratorSettings", menuName = "Sprite Prefab Generator/Settings", order = 400)]
    public class PrefabGeneratorSettings : ScriptableObject
    {
        [Header("Thư mục")]
        [Tooltip("Thư mục chứa asset gốc (sprite sheet, .anim, .controller). Quét cả thư mục con.")]
        public DefaultAsset sourceFolder;

        [Tooltip("Thư mục lưu prefab (và animation tự tạo). Tự tạo nếu chưa có.")]
        public string outputFolder = "Assets/Prefabs";

        [Tooltip("Giữ cấu trúc thư mục con của sourceFolder trong outputFolder.")]
        public bool mirrorFolderStructure = true;

        [Tooltip("Regex: thư mục con có tên khớp sẽ bị bỏ khỏi đường dẫn đầu ra, vd \"Warrior Animations\". Để trống để giữ nguyên.")]
        public string collapseFolderRegex = @"(?i)\banimations?\b";

        [Tooltip("Regex trên đường dẫn tương đối so với sourceFolder; asset khớp sẽ bị bỏ qua, vd (?i)(tileset|ui).")]
        public string excludePathRegex = "";

        [Header("Nguồn tạo prefab")]
        [Tooltip("Mỗi Animator Controller có sẵn -> một prefab dùng controller đó.")]
        public bool fromAnimatorControllers = true;

        [Tooltip("Sprite sheet đã cắt (nhiều sprite) chưa có animation -> tự tạo .anim + .controller + prefab.")]
        public bool fromSpriteSheets = true;

        [Tooltip("Texture chỉ có một sprite và không thuộc animation nào -> prefab tĩnh (chỉ SpriteRenderer).")]
        public bool fromStaticSprites = true;

        [Header("Sprite sheet -> animation")]
        public SpriteSheetGrouping grouping = SpriteSheetGrouping.Folder;

        [Tooltip("Các ký tự dùng để tách tiền tố khi grouping = NamePrefix.")]
        public string namePrefixSeparators = "_- ";

        [Min(1f)] public float frameRate = 10f;

        [Tooltip("Sprite sheet có ít sprite hơn số này sẽ không được coi là animation.")]
        [Min(2)] public int minFrames = 2;

        [Tooltip("Regex trên tên clip: clip khớp sẽ KHÔNG lặp (vd animation chết).")]
        public string nonLoopingRegex = @"(?i)(death|die|dead)";

        [Tooltip("Regex trên tên clip để chọn state mặc định. Không khớp clip nào thì lấy clip đầu tiên.")]
        public string defaultStateRegex = @"(?i)idle";

        [Header("Prefab")]
        [Tooltip("Tên prefab có animation. {name} = tên controller/nhóm, {parent} = thư mục chứa asset, {parent:N} = từ thứ N (đếm từ 0) trong tên thư mục chứa.")]
        public string animatedNamePattern = "{name}";

        [Tooltip("Tên prefab tĩnh. Cùng các token như animatedNamePattern, {name} = tên texture.")]
        public string staticNamePattern = "{name}";

        [Tooltip("Prefab mẫu (tuỳ chọn), vd đã có Collider2D hay script. Sẽ được nhân bản rồi gán sprite/Animator vào object gốc.")]
        public GameObject template;

        [Tooltip("Ghi đè Sorting Layer / Order in Layer của SpriteRenderer.")]
        public bool overrideSorting;
        public string sortingLayerName = "Default";
        public int orderInLayer;

        public string SourcePath => sourceFolder != null ? AssetDatabase.GetAssetPath(sourceFolder) : null;
    }
}
