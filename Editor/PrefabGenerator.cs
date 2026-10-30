using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HaiTNDev.SpritePrefabGenerator
{
    public enum JobKind { FromController, FromSpriteSheet, Static }

    /// <summary>Một prefab sẽ được tạo. Plan() tạo danh sách job, Execute() mới thực sự ghi asset.</summary>
    public class GenerationJob
    {
        public JobKind Kind;
        public string Name;
        public string PrefabPath;
        public Sprite PreviewSprite;
        public bool PrefabExists;

        // FromController
        public AnimatorController Controller;

        // FromSpriteSheet
        public string ControllerPath;
        public List<ClipSpec> Clips;
        public string DefaultClipName;
    }

    public class ClipSpec
    {
        public string Name;
        public string Path;
        public List<Sprite> Frames;
        public bool Loop;
    }

    public struct GenerationResult
    {
        public int Created, Skipped, Failed;
        public override string ToString() => $"tạo {Created}, bỏ qua {Skipped} (đã tồn tại), lỗi {Failed}";
    }

    public static class PrefabGenerator
    {
        const string LogPrefix = "[SpritePrefabGenerator]";

        static readonly EditorCurveBinding SpriteBinding =
            EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");

        // ---------------------------------------------------------------- Plan

        /// <summary>Quét sourceFolder và trả về danh sách prefab sẽ tạo. Không ghi gì vào project.</summary>
        public static List<GenerationJob> Plan(PrefabGeneratorSettings s, List<string> warnings)
        {
            var jobs = new List<GenerationJob>();
            string source = s.SourcePath;
            if (string.IsNullOrEmpty(source) || !AssetDatabase.IsValidFolder(source))
            {
                warnings.Add("Chưa chọn sourceFolder hợp lệ.");
                return jobs;
            }
            string output = NormalizeFolder(s.outputFolder);
            if (string.IsNullOrEmpty(output) || !(output == "Assets" || output.StartsWith("Assets/")))
            {
                warnings.Add($"outputFolder phải nằm trong Assets/: \"{s.outputFolder}\".");
                return jobs;
            }

            var ctx = new PlanContext(s, source, output);

            // Texture đã được clip có sẵn dùng tới thì không tạo animation/prefab tĩnh trùng lặp.
            var animatedTextures = CollectAnimatedTextures(source);

            if (s.fromAnimatorControllers)
                PlanControllers(ctx, jobs, warnings);
            if (s.fromSpriteSheets || s.fromStaticSprites)
                PlanTextures(ctx, animatedTextures, jobs, warnings);

            // Hai nguồn khác nhau có thể ra cùng một đường dẫn (vd mirrorFolderStructure = false).
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unique = new List<GenerationJob>();
            foreach (var job in jobs)
            {
                if (seen.Add(job.PrefabPath))
                    unique.Add(job);
                else
                    warnings.Add($"Trùng đường dẫn prefab, bỏ qua bản sau: {job.PrefabPath}");
            }

            foreach (var job in unique)
                job.PrefabExists = AssetDatabase.LoadAssetAtPath<GameObject>(job.PrefabPath) != null;
            return unique;
        }

        class PlanContext
        {
            public readonly PrefabGeneratorSettings Settings;
            public readonly string Source, Output;
            public readonly Regex Collapse, Exclude, NonLooping, DefaultState;

            public PlanContext(PrefabGeneratorSettings s, string source, string output)
            {
                Settings = s;
                Source = source;
                Output = output;
                Collapse = MakeRegex(s.collapseFolderRegex);
                Exclude = MakeRegex(s.excludePathRegex);
                NonLooping = MakeRegex(s.nonLoopingRegex);
                DefaultState = MakeRegex(s.defaultStateRegex);
            }

            public string Relative(string assetPath) =>
                assetPath.Length > Source.Length ? assetPath.Substring(Source.Length).TrimStart('/') : "";

            public bool IsExcluded(string assetPath) => Exclude != null && Exclude.IsMatch(Relative(assetPath));

            /// <summary>Thư mục chứa asset nguồn -> thư mục đầu ra tương ứng.</summary>
            public string MapFolder(string assetFolder)
            {
                if (!Settings.mirrorFolderStructure)
                    return Output;
                var segments = Relative(assetFolder)
                    .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(seg => Collapse == null || !Collapse.IsMatch(seg));
                return string.Join("/", new[] { Output }.Concat(segments));
            }
        }

        static void PlanControllers(PlanContext ctx, List<GenerationJob> jobs, List<string> warnings)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:AnimatorController", new[] { ctx.Source }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (ctx.IsExcluded(path))
                    continue;
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (controller == null)
                    continue;

                Sprite sprite = GetDefaultSprite(controller);
                if (sprite == null)
                {
                    warnings.Add($"Controller không đổi sprite trên object gốc (SpriteRenderer.m_Sprite, path rỗng), bỏ qua: {path}");
                    continue;
                }

                string folder = ParentFolder(path);
                string name = ApplyPattern(ctx.Settings.animatedNamePattern, controller.name, LastSegment(folder));
                jobs.Add(new GenerationJob
                {
                    Kind = JobKind.FromController,
                    Name = name,
                    PrefabPath = $"{ctx.MapFolder(folder)}/{name}.prefab",
                    PreviewSprite = sprite,
                    Controller = controller,
                });
            }
        }

        static void PlanTextures(PlanContext ctx, HashSet<string> animatedTextures, List<GenerationJob> jobs, List<string> warnings)
        {
            var s = ctx.Settings;
            // key nhóm -> (thư mục, tên nhóm, các texture)
            var groups = new Dictionary<string, (string folder, string name, List<(string path, List<Sprite> sprites)> sheets)>();

            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ctx.Source }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (ctx.IsExcluded(path) || animatedTextures.Contains(path))
                    continue;
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer) || importer.textureType != TextureImporterType.Sprite)
                    continue;

                var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
                    .OrderBy(sp => sp.name, NaturalComparer.Instance).ToList();
                string folder = ParentFolder(path);
                string textureName = Path.GetFileNameWithoutExtension(path);

                if (sprites.Count == 1)
                {
                    if (!s.fromStaticSprites)
                        continue;
                    string name = ApplyPattern(s.staticNamePattern, textureName, LastSegment(folder));
                    jobs.Add(new GenerationJob
                    {
                        Kind = JobKind.Static,
                        Name = name,
                        PrefabPath = $"{ctx.MapFolder(folder)}/{name}.prefab",
                        PreviewSprite = sprites[0],
                    });
                    continue;
                }

                if (!s.fromSpriteSheets || sprites.Count == 0)
                    continue;
                if (sprites.Count < s.minFrames)
                {
                    warnings.Add($"Sprite sheet có {sprites.Count} sprite (< minFrames = {s.minFrames}), bỏ qua: {path}");
                    continue;
                }

                string groupName, key;
                switch (s.grouping)
                {
                    case SpriteSheetGrouping.Folder:
                        groupName = LastSegment(folder);
                        key = folder;
                        break;
                    case SpriteSheetGrouping.NamePrefix:
                        int cut = string.IsNullOrEmpty(s.namePrefixSeparators) ? -1 : textureName.IndexOfAny(s.namePrefixSeparators.ToCharArray());
                        groupName = cut > 0 ? textureName.Substring(0, cut) : textureName;
                        key = folder + "/" + groupName;
                        break;
                    default:
                        groupName = textureName;
                        key = path;
                        break;
                }

                if (!groups.TryGetValue(key, out var group))
                    groups[key] = group = (folder, groupName, new List<(string, List<Sprite>)>());
                group.sheets.Add((path, sprites));
            }

            foreach (var (folder, groupName, sheets) in groups.Values)
            {
                string name = ApplyPattern(s.animatedNamePattern, groupName, LastSegment(folder));
                string outFolder = ctx.MapFolder(folder);
                string animFolder = outFolder + "/Animations";

                var clips = sheets
                    .OrderBy(sh => Path.GetFileNameWithoutExtension(sh.path), NaturalComparer.Instance)
                    .Select(sh =>
                    {
                        string textureName = Path.GetFileNameWithoutExtension(sh.path);
                        // "Idle" trong thư mục Monk -> "Monk_Idle"; "Warrior_Idle" giữ nguyên.
                        string clipName = textureName.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                            ? textureName
                            : $"{name}_{textureName}";
                        return new ClipSpec
                        {
                            Name = clipName,
                            Path = $"{animFolder}/{clipName}.anim",
                            Frames = sh.sprites,
                            Loop = ctx.NonLooping == null || !ctx.NonLooping.IsMatch(clipName),
                        };
                    })
                    .ToList();

                var defaultClip = clips.FirstOrDefault(c => ctx.DefaultState != null && ctx.DefaultState.IsMatch(c.Name)) ?? clips[0];
                jobs.Add(new GenerationJob
                {
                    Kind = JobKind.FromSpriteSheet,
                    Name = name,
                    PrefabPath = $"{outFolder}/{name}.prefab",
                    PreviewSprite = defaultClip.Frames[0],
                    ControllerPath = $"{animFolder}/{name}.controller",
                    Clips = clips,
                    DefaultClipName = defaultClip.Name,
                });
            }
        }

        // ------------------------------------------------------------- Execute

        /// <summary>Tạo prefab theo profile. overwrite = false chỉ tạo prefab còn thiếu.</summary>
        public static GenerationResult Generate(PrefabGeneratorSettings settings, bool overwrite)
        {
            var warnings = new List<string>();
            var jobs = Plan(settings, warnings);
            foreach (string w in warnings)
                Debug.LogWarning($"{LogPrefix} {w}", settings);
            var result = Execute(settings, jobs, overwrite);
            Debug.Log($"{LogPrefix} {settings.name}: {result}. Thư mục: {NormalizeFolder(settings.outputFolder)}", settings);
            return result;
        }

        public static GenerationResult Execute(PrefabGeneratorSettings s, List<GenerationJob> jobs, bool overwrite)
        {
            var result = new GenerationResult();
            try
            {
                for (int i = 0; i < jobs.Count; i++)
                {
                    var job = jobs[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Sprite Prefab Generator", job.PrefabPath, (float)i / jobs.Count))
                        break;
                    if (job.PrefabExists && !overwrite)
                    {
                        result.Skipped++;
                        continue;
                    }

                    try
                    {
                        RuntimeAnimatorController controller = job.Controller;
                        if (job.Kind == JobKind.FromSpriteSheet)
                            controller = BuildController(job, s.frameRate);

                        SavePrefab(s, job, controller);
                        result.Created++;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"{LogPrefix} Lỗi khi tạo {job.PrefabPath}: {e}", s);
                        result.Failed++;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
            }
            return result;
        }

        static AnimatorController BuildController(GenerationJob job, float frameRate)
        {
            EnsureFolder(ParentFolder(job.ControllerPath));

            var clips = job.Clips.ToDictionary(c => c.Name, c => BuildClip(c, frameRate));

            // Ghi đè tại chỗ để giữ GUID nếu controller đã có.
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(job.ControllerPath);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(job.ControllerPath);
            var stateMachine = controller.layers[0].stateMachine;
            foreach (var child in stateMachine.states.ToArray())
                stateMachine.RemoveState(child.state);

            foreach (var spec in job.Clips)
            {
                var state = stateMachine.AddState(spec.Name);
                state.motion = clips[spec.Name];
                if (spec.Name == job.DefaultClipName)
                    stateMachine.defaultState = state;
            }
            EditorUtility.SetDirty(controller);
            return controller;
        }

        static AnimationClip BuildClip(ClipSpec spec, float frameRate)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(spec.Path);
            bool isNew = clip == null;
            if (isNew)
                clip = new AnimationClip { name = spec.Name };
            else
                clip.ClearCurves();

            clip.frameRate = frameRate;
            // Unity tự cộng thêm một frame vào độ dài clip sprite, nên frame cuối hiển thị đủ thời gian.
            var keys = spec.Frames
                .Select((sprite, i) => new ObjectReferenceKeyframe { time = i / frameRate, value = sprite })
                .ToArray();
            AnimationUtility.SetObjectReferenceCurve(clip, SpriteBinding, keys);

            var clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
            clipSettings.loopTime = spec.Loop;
            AnimationUtility.SetAnimationClipSettings(clip, clipSettings);

            if (isNew)
                AssetDatabase.CreateAsset(clip, spec.Path);
            else
                EditorUtility.SetDirty(clip);
            return clip;
        }

        static void SavePrefab(PrefabGeneratorSettings s, GenerationJob job, RuntimeAnimatorController controller)
        {
            EnsureFolder(ParentFolder(job.PrefabPath));

            var go = s.template != null ? Object.Instantiate(s.template) : new GameObject();
            try
            {
                go.name = job.Name;

                // Không dùng ?? với UnityEngine.Object: GetComponent trong Editor có thể trả về "fake null".
                var renderer = go.GetComponent<SpriteRenderer>();
                if (renderer == null)
                    renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = job.PreviewSprite;
                if (s.overrideSorting)
                {
                    renderer.sortingLayerName = s.sortingLayerName;
                    renderer.sortingOrder = s.orderInLayer;
                }

                var animator = go.GetComponent<Animator>();
                if (controller != null)
                {
                    if (animator == null)
                        animator = go.AddComponent<Animator>();
                    animator.runtimeAnimatorController = controller;
                }
                else if (animator != null && s.template != null)
                {
                    // Prefab tĩnh: bỏ Animator thừa từ template để không ghi đè sprite.
                    Object.DestroyImmediate(animator);
                }

                // SaveAsPrefabAsset lên đường dẫn đã có sẽ giữ GUID, tham chiếu trong scene không bị hỏng.
                PrefabUtility.SaveAsPrefabAsset(go, job.PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------- Helpers

        /// <summary>Frame đầu của clip ở state mặc định, để prefab hiển thị đúng hình khi chưa Play.</summary>
        public static Sprite GetDefaultSprite(AnimatorController controller)
        {
            var clips = new List<AnimationClip>();
            var defaultState = controller.layers.Length > 0 ? controller.layers[0].stateMachine.defaultState : null;
            if (defaultState != null && defaultState.motion is AnimationClip defaultClip)
                clips.Add(defaultClip);
            clips.AddRange(controller.animationClips);

            foreach (var clip in clips)
            {
                if (clip == null)
                    continue;
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, SpriteBinding);
                if (keys != null && keys.Length > 0 && keys[0].value is Sprite sprite)
                    return sprite;
            }
            return null;
        }

        static HashSet<string> CollectAnimatedTextures(string source)
        {
            var textures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { source }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip == null)
                    continue;
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                foreach (var key in AnimationUtility.GetObjectReferenceCurve(clip, binding))
                    if (key.value is Sprite sprite && sprite.texture != null)
                        textures.Add(AssetDatabase.GetAssetPath(sprite.texture));
            }
            return textures;
        }

        static readonly Regex PatternToken = new Regex(@"\{(name|parent)(?::(\d+))?\}");

        /// <summary>{name}, {parent}, {parent:N} (từ thứ N trong tên thư mục chứa, tách bởi khoảng trắng/_/-).</summary>
        public static string ApplyPattern(string pattern, string name, string parent)
        {
            if (string.IsNullOrEmpty(pattern))
                return name;
            return PatternToken.Replace(pattern, m =>
            {
                string value = m.Groups[1].Value == "name" ? name : parent;
                if (!m.Groups[2].Success)
                    return value;
                var words = value.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
                int index = int.Parse(m.Groups[2].Value);
                return index < words.Length ? words[index] : "";
            });
        }

        static Regex MakeRegex(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return null;
            try { return new Regex(pattern); }
            catch (ArgumentException e)
            {
                Debug.LogWarning($"{LogPrefix} Regex không hợp lệ \"{pattern}\": {e.Message}");
                return null;
            }
        }

        public static string NormalizeFolder(string folder) =>
            string.IsNullOrWhiteSpace(folder) ? null : folder.Replace('\\', '/').Trim().TrimEnd('/');

        static string ParentFolder(string assetPath) => Path.GetDirectoryName(assetPath).Replace('\\', '/');

        static string LastSegment(string folder) => folder.Substring(folder.LastIndexOf('/') + 1);

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            string parent = ParentFolder(folder);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, LastSegment(folder));
        }

        public static string Describe(List<GenerationJob> jobs, List<string> warnings)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{jobs.Count} prefab ({jobs.Count(j => !j.PrefabExists)} mới, {jobs.Count(j => j.PrefabExists)} đã tồn tại):");
            foreach (var group in jobs.GroupBy(j => j.Kind))
            {
                sb.AppendLine($"  {group.Key}: {group.Count()}");
                foreach (var job in group)
                {
                    string extra = job.Kind == JobKind.FromSpriteSheet
                        ? $"  [{string.Join(", ", job.Clips.Select(c => $"{c.Name} ({c.Frames.Count})"))}]"
                        : "";
                    sb.AppendLine($"    {(job.PrefabExists ? "=" : "+")} {job.PrefabPath}{extra}");
                }
            }
            foreach (string w in warnings)
                sb.AppendLine("  ! " + w);
            return sb.ToString();
        }

        /// <summary>So sánh tên có số theo giá trị số: Run_2 đứng trước Run_10.</summary>
        class NaturalComparer : IComparer<string>
        {
            public static readonly NaturalComparer Instance = new NaturalComparer();
            static readonly Regex Chunks = new Regex(@"\d+|\D+");

            public int Compare(string a, string b)
            {
                var ca = Chunks.Matches(a ?? "");
                var cb = Chunks.Matches(b ?? "");
                for (int i = 0; i < Math.Min(ca.Count, cb.Count); i++)
                {
                    string x = ca[i].Value, y = cb[i].Value;
                    int cmp = char.IsDigit(x[0]) && char.IsDigit(y[0])
                        ? long.Parse(x).CompareTo(long.Parse(y))
                        : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
                    if (cmp != 0)
                        return cmp;
                }
                return ca.Count.CompareTo(cb.Count);
            }
        }
    }
}
