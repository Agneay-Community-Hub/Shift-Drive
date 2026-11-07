using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Generates all materials/textures, the main scene and player settings, and builds the game.
/// Run from the menu "Shift Drive/..." or headless via the Unity CLI:
///   unity build --target WebGL --execute-method ShiftDriveSetup.BuildWebGL -o Builds/WebGL
public static class ShiftDriveSetup
{
    const string Gen = "Assets/Generated";
    const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("Shift Drive/Setup Project")]
    public static void Setup()
    {
        Directory.CreateDirectory(Gen);
        Directory.CreateDirectory("Assets/Scenes");

        var grid = MakeTexture("grid", 256, (x, y) =>
        {
            bool edge = x < 3 || x > 252 || y < 3 || y > 252;
            bool mid = Mathf.Abs(x - 128) < 1 || Mathf.Abs(y - 128) < 1;
            float v = edge ? 1f : mid ? 0.22f : 0f;
            return new Color(v, v, v, 1f);
        });
        var stripes = MakeTexture("stripes", 128, (x, y) =>
        {
            bool s = ((x + y) / 32) % 2 == 0;
            return s ? Color.white : new Color(0.12f, 0.12f, 0.12f, 1f);
        });
        var checker = MakeTexture("checker", 64, (x, y) => Color.white);

        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");

        Color g1 = GameAssets.GearColors[0];
        var gridFloor = Mat("GridFloor", lit, new Color(0.07f, 0.07f, 0.1f), grid, new Vector2(3, 3), g1 * 1.4f, 0.75f, 0.2f);
        var gridWall = Mat("GridWall", lit, new Color(0.05f, 0.05f, 0.08f), grid, new Vector2(3, 2), g1 * 0.7f, 0.6f, 0.1f);
        var strip = Mat("Strip", lit, Color.black, null, Vector2.one, g1 * 3.5f, 0.5f);
        var dark = Mat("Dark", lit, new Color(0.08f, 0.08f, 0.1f), null, Vector2.one, Color.black, 0.6f, 0.6f);
        var hazard = Mat("Hazard", lit, new Color(0.35f, 0.02f, 0.04f), stripes, new Vector2(4, 1), new Color(1f, 0.08f, 0.12f) * 2.2f, 0.4f);
        var warning = Mat("Warning", lit, new Color(0.2f, 0.05f, 0.02f), stripes, new Vector2(8, 2), new Color(1f, 0.25f, 0.05f) * 0.8f, 0.4f);
        var checkpoint = Mat("Checkpoint", lit, Color.white, null, Vector2.one, Color.white * 2.5f, 0.5f);
        var finish = Mat("Finish", lit, Color.white, checker, Vector2.one, Color.white * 1.8f, 0.5f);
        var gears = new Material[4];
        for (int i = 0; i < 4; i++)
            gears[i] = Mat("Gear" + (i + 1), lit, GameAssets.GearColors[i] * 0.3f, null, Vector2.one, GameAssets.GearColors[i] * 3f, 0.5f);

        var carBody = Mat("CarBody", lit, new Color(0.92f, 0.93f, 0.96f), null, Vector2.one, Color.black, 0.85f, 0.3f);
        var carGlass = Mat("CarGlass", lit, new Color(0.03f, 0.04f, 0.08f), null, Vector2.one, Color.black, 0.95f, 0.8f);
        var carWheel = Mat("CarWheel", lit, new Color(0.04f, 0.04f, 0.05f), null, Vector2.one, Color.black, 0.3f, 0f);
        var carAccent = Mat("CarAccent", lit, Color.black, null, Vector2.one, g1 * 3f, 0.5f);
        var headlight = Mat("Headlight", lit, Color.white, null, Vector2.one, Color.white * 4f, 0.5f);
        var trail = TrailMat(particles);

        var profile = PostProfile();

        // ---------------- scene ----------------
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = new GameObject("Game");
        var ga = go.AddComponent<GameAssets>();
        ga.gridFloor = gridFloor; ga.gridWall = gridWall; ga.strip = strip; ga.dark = dark;
        ga.hazard = hazard; ga.warning = warning; ga.checkpoint = checkpoint; ga.finish = finish;
        ga.gearMats = gears;
        ga.carBody = carBody; ga.carGlass = carGlass; ga.carWheel = carWheel; ga.carAccent = carAccent;
        ga.headlight = headlight; ga.trail = trail;
        ga.postProfile = profile;
        go.AddComponent<RaceManager>();
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        // ---------------- player settings ----------------
        PlayerSettings.companyName = "ShiftDrive";
        PlayerSettings.productName = "Shift Drive";
        PlayerSettings.runInBackground = true;
        PlayerSettings.defaultWebScreenWidth = 1280;
        PlayerSettings.defaultWebScreenHeight = 720;
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[ShiftDrive] Setup complete.");
    }

    // ------------------------------------------------------------------ builds

    [MenuItem("Shift Drive/Build WebGL")]
    public static void BuildWebGL() => Build(BuildTarget.WebGL, Arg("-buildOutput") ?? "Builds/WebGL");

    [MenuItem("Shift Drive/Build Windows")]
    public static void BuildWindows()
    {
        string dir = Arg("-buildOutput") ?? "Builds/Windows";
        Build(BuildTarget.StandaloneWindows64, Path.Combine(dir, "ShiftDrive.exe"));
    }

    static void Build(BuildTarget target, string path)
    {
        Setup();
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = path,
            target = target,
            targetGroup = BuildPipeline.GetBuildTargetGroup(target),
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log($"[ShiftDrive] Build {target} -> {path}: {report.summary.result}, {report.summary.totalSize / (1024f * 1024f):0.0} MB, {report.summary.totalErrors} errors");
        if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    // ------------------------------------------------------------------ asset helpers

    static Texture2D MakeTexture(string name, int size, Func<int, int, Color> f)
    {
        string path = $"{Gen}/{name}.png";
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, f(x, y));
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.wrapMode = TextureWrapMode.Repeat;
        imp.mipmapEnabled = true;
        imp.anisoLevel = 8;
        imp.filterMode = FilterMode.Trilinear;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material Mat(string name, Shader sh, Color baseCol, Texture tex, Vector2 tiling, Color emission, float smooth, float metal = 0f)
    {
        string path = $"{Gen}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        else m.shader = sh;
        m.SetColor("_BaseColor", baseCol);
        m.SetTexture("_BaseMap", tex);
        m.SetTextureScale("_BaseMap", tiling);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        if (emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.SetTexture("_EmissionMap", tex);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            m.DisableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material TrailMat(Shader sh)
    {
        string path = $"{Gen}/Trail.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 2f); // additive
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.One);
        m.SetFloat("_ZWrite", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(m);
        return m;
    }

    /// A profile asset containing every effect used at runtime, so URP never strips their shaders.
    static VolumeProfile PostProfile()
    {
        string path = $"{Gen}/PostProfile.asset";
        var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (existing != null) return existing;
        var p = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(p, path);
        void Add<T>() where T : VolumeComponent
        {
            var c = p.Add<T>(true);
            c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(c, p);
        }
        Add<Bloom>();
        Add<Vignette>();
        Add<ColorAdjustments>();
        Add<ChromaticAberration>();
        Add<LensDistortion>();
        EditorUtility.SetDirty(p);
        return p;
    }
}
