using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Builds Mod/Resources/rsc.unity3d from the FBX models that Tools/Art/rsc_art.py
// writes to Art/Models. Run headless (Tools/Unity/BuildBundle.ps1 does this):
//
//   Unity.exe -batchmode -quit -projectPath Tools/Unity/RscBundle
//             -executeMethod RscBundleBuilder.Build -rscRoot <repo root>
//
// Each block becomes a prefab named like the block, so blocks.xml can use
//   <property name="Model" value="#@modfolder:Resources/rsc.unity3d?Assets/RSC/Prefabs/rscTerminal.prefab" />
// The game's conventions for model blocks: the prefab name must match the
// asset name; the collider sits on the prefab root with the "T_Block" tag so
// aiming, the E prompt, the wire tool and damage find the block (the game maps
// a hit back to its block through the root); a child named "WireOffset" is where
// wires attach. The model stands on y = 0, centred on x/z.
public static class RscBundleBuilder
{
    private const string BundleName = "rsc.unity3d";
    private const string Base = "Assets/RSC";
    private const string ModelDir = Base + "/Models";
    private const string PrefabDir = Base + "/Prefabs";
    private const string MaterialDir = Base + "/Materials";
    private const string BlockTag = "T_Block";

    private static readonly string[] Blocks =
    {
        "rscControllerSimple", "rscControllerAdvanced", "rscHub", "rscInterface", "rscTerminal", "rscDriveCabinet"
    };

    // Emission is toned down from the Blender renders: the game adds its own
    // bloom, and icons are rendered separately.
    private const float EmissionScale = 0.6f;

    public static void Build()
    {
        try
        {
            string root = Arg("-rscRoot") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../../.."));
            string fbxSource = Path.Combine(root, "Art", "Models");
            string output = Path.Combine(root, "Mod", "Resources");
            Debug.Log("[RSC] repo root: " + root);

            EnsureTag(BlockTag);
            foreach (var dir in new[] { Base, ModelDir, PrefabDir, MaterialDir })
                Directory.CreateDirectory(dir);

            foreach (string name in Blocks)
            {
                string src = Path.Combine(fbxSource, name + ".fbx");
                if (!File.Exists(src))
                    throw new FileNotFoundException("missing model (run Tools/Art/rsc_art.py first)", src);
                File.Copy(src, ModelDir + "/" + name + ".fbx", true);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            foreach (string name in Blocks)
            {
                string path = ModelDir + "/" + name + ".fbx";
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.importCameras = false;
                importer.importLights = false;
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.isReadable = false;
                importer.meshCompression = ModelImporterMeshCompression.Medium;
                importer.SaveAndReimport();
            }

            var prefabs = new List<string>();
            foreach (string name in Blocks)
                prefabs.Add(MakePrefab(name));

            foreach (string p in prefabs)
                AssetImporter.GetAtPath(p).SetAssetBundleNameAndVariant(BundleName, "");
            AssetDatabase.SaveAssets();

            string tmp = Path.GetFullPath("BundleOut");
            Directory.CreateDirectory(tmp);
            var manifest = BuildPipeline.BuildAssetBundles(tmp, BuildAssetBundleOptions.ChunkBasedCompression,
                BuildTarget.StandaloneWindows64);
            if (manifest == null)
                throw new Exception("BuildAssetBundles failed");

            Directory.CreateDirectory(output);
            File.Copy(Path.Combine(tmp, BundleName), Path.Combine(output, BundleName), true);
            Debug.Log("[RSC] bundle written: " + Path.Combine(output, BundleName));
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("[RSC] bundle build failed: " + e);
            EditorApplication.Exit(1);
        }
    }

    private static string MakePrefab(string name)
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + "/" + name + ".fbx");
        var rootGo = new GameObject(name);
        var model = UnityEngine.Object.Instantiate(fbx);
        model.name = "model";
        model.transform.SetParent(rootGo.transform, false);

        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                mats[i] = MaterialFor(mats[i] != null ? mats[i].name : "rsc_body");
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.receiveShadows = true;
        }

        // Stand on y = 0, centred on x/z, whatever the exporter's pivot.
        var bounds = Bounds(model);
        model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        bounds = Bounds(model);

        // The collider must sit on the prefab root: a hit is traced back to its
        // block by comparing the hit transform (or its RootTransformRefParent
        // target) with the block entity's transform, which is this root.
        rootGo.tag = BlockTag;
        rootGo.layer = 0;
        var box = rootGo.AddComponent<BoxCollider>();
        var size = new Vector3(Mathf.Min(bounds.size.x, 1f), Mathf.Min(bounds.size.y, 1f), Mathf.Min(bounds.size.z, 1f));
        box.size = size;
        box.center = new Vector3(0f, size.y / 2f, 0f);

        var wire = new GameObject("WireOffset");
        wire.transform.SetParent(rootGo.transform, false);
        wire.transform.localPosition = new Vector3(0f, Mathf.Min(bounds.max.y, 1f) - 0.03f, 0f);

        string path = PrefabDir + "/" + name + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(rootGo, path);
        UnityEngine.Object.DestroyImmediate(rootGo);
        Debug.Log("[RSC] prefab " + path + " size " + bounds.size.ToString("F2"));
        return path;
    }

    private static Bounds Bounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        var b = renderers[0].bounds;
        foreach (var r in renderers)
            b.Encapsulate(r.bounds);
        b.center -= go.transform.parent != null ? go.transform.parent.position : Vector3.zero;
        return b;
    }

    // Materials are rebuilt from the Blender material names, so the look is
    // defined in one place (rsc_art.py) and survives the FBX round trip.
    private static readonly Dictionary<string, Material> Made = new Dictionary<string, Material>();

    private static Material MaterialFor(string sourceName)
    {
        string name = Regex.Replace(sourceName, @"(\.\d+)?( \(Instance\))?$", "");
        if (Made.TryGetValue(name, out var m))
            return m;

        Color color = Hex("23272E");
        float metallic = 0.75f, smoothness = 0.58f;
        Color? emission = null;
        var glow = Regex.Match(name, @"^rsc_glow_([0-9A-Fa-f]{6})_([0-9.]+)$");
        var accent = Regex.Match(name, @"^rsc_accent_([0-9A-Fa-f]{6})$");
        if (glow.Success)
        {
            color = Hex(glow.Groups[1].Value);
            metallic = 0f;
            smoothness = 0.7f;
            float strength = float.Parse(glow.Groups[2].Value, CultureInfo.InvariantCulture);
            emission = color * (strength * EmissionScale);
        }
        else if (accent.Success)
        {
            color = Hex(accent.Groups[1].Value);
            metallic = 0.4f;
            smoothness = 0.65f;
        }
        else
            switch (name)
            {
                case "rsc_panel": color = Hex("3A4049"); metallic = 0.7f; smoothness = 0.5f; break;
                case "rsc_trim": color = Hex("9AA3AD"); metallic = 0.95f; smoothness = 0.75f; break;
                case "rsc_recess": color = Hex("0C0E11"); metallic = 0.3f; smoothness = 0.4f; break;
                case "rsc_gold": color = Hex("D6A53A"); metallic = 1f; smoothness = 0.75f; break;
                case "rsc_labeltext": color = Hex("101214"); metallic = 0f; smoothness = 0.5f; break;
                case "rsc_screencell": color = Hex("0F2A12"); metallic = 0f; smoothness = 0.5f; emission = Hex("1E6B24"); break;
            }

        m = new Material(Shader.Find("Standard")) { name = name };
        m.SetColor("_Color", color);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", smoothness);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        AssetDatabase.CreateAsset(m, MaterialDir + "/" + name + ".mat");
        Made[name] = m;
        return m;
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out var c);
        return c;
    }

    private static void EnsureTag(string tag)
    {
        var manager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var tags = manager.FindProperty("tags");
        for (int i = 0; i < tags.arraySize; i++)
            if (tags.GetArrayElementAtIndex(i).stringValue == tag)
                return;
        tags.InsertArrayElementAtIndex(tags.arraySize);
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
        manager.ApplyModifiedProperties();
    }

    private static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name)
                return args[i + 1];
        return null;
    }
}
