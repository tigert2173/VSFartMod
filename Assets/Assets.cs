using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VSFartMod;
public class Assets
{
    static AssetBundle assets;
    public static Texture2D NewLogo;
    public static Texture2D FartGasTexture;
    public static TextAsset ListOfToggles;
    public static TextAsset ToggleInfo;
    public static TextAsset MenuExplanations;
    public static TextAsset TimedTasklist;
    public static TextAsset InteractionStrings;
    public static GameObject FartingToggle;
    public static GameObject ContentSelectMenu;
    public static AudioClip[] FartSounds = Array.Empty<AudioClip>();

    public static void LoadAssets()
    {
        string root = Application.streamingAssetsPath;
        VSFartMod.Logger.LogInfo("StreamingAssets path: " + root);

        if (Directory.Exists(root))
        {
            foreach (string entry in Directory.GetFileSystemEntries(root))
            {
                var info = new FileInfo(entry);
                bool isDir = Directory.Exists(entry);
                VSFartMod.Logger.LogInfo($"  SA entry: {Path.GetFileName(entry)} isDir={isDir} len={(isDir ? 0 : info.Length)}");
            }
        }
        else
        {
            VSFartMod.Logger.LogError("StreamingAssets folder does not exist: " + root);
        }

        string assetPath = FindBundlePath(root);
        if (assetPath == null)
        {
            VSFartMod.Logger.LogWarning(
                "Asset bundle not found. Tried vsfartmod / vsfartmod.bundle / vsfartmod.unity3d. " +
                "Sounds still load from StreamingAssets/VSFartSounds/Farts. " +
                "Farting toggle will be cloned from Smothering at runtime.");
        }
        else
        {
            VSFartMod.Logger.LogInfo("Loading asset bundle from " + assetPath);

            try
            {
                assets = AssetBundle.LoadFromFile(assetPath);
                if (assets == null)
                {
                    // Some Unity/Mono setups dislike extensionless files — try memory load.
                    byte[] bytes = File.ReadAllBytes(assetPath);
                    assets = AssetBundle.LoadFromMemory(bytes);
                }
            }
            catch (Exception ex)
            {
                VSFartMod.Logger.LogError($"Exception loading asset bundle: {ex}");
                assets = null;
            }

            if (assets == null)
            {
                VSFartMod.Logger.LogError("Failed to load vsfartmod asset bundle (LoadFromFile and LoadFromMemory both null).");
            }
            else
            {
                foreach (string name in assets.GetAllAssetNames())
                {
                    VSFartMod.Logger.LogInfo("Bundle asset: " + name);
                }

                NewLogo = LoadTexture("NewLogo", "NewLogo.png");
                FartGasTexture = LoadTexture("FartGasSoft", "FartGasSoft.png");

                ListOfToggles = LoadText("ListOfToggles", "ListOfToggles.txt");
                ToggleInfo = LoadText("TogggleInfo", "TogggleInfo.txt");
                MenuExplanations = LoadText("MenuExplanations", "MenuExplanations.txt");
                TimedTasklist = LoadText("Tasklist - Timed New", "Tasklist - Timed New.txt");
                InteractionStrings = LoadText("InteractionStrings", "InteractionStrings.txt");

                // Never pull ContentSelectMenu / FartingToggle from the broken audio bundle (MissingScript flood).
                FartingToggle = null;
                ContentSelectMenu = null;

                var clips = new List<AudioClip>();
                foreach (var clip in assets.LoadAllAssets<AudioClip>())
                {
                    if (clip != null && clip.name.StartsWith("Fart_", StringComparison.OrdinalIgnoreCase))
                    {
                        clips.Add(clip);
                    }
                }
                FartSounds = clips.ToArray();

                VSFartMod.Logger.LogInfo($"Bundle loaded. Fart sounds in bundle: {FartSounds.Length}");
                VSFartMod.Logger.LogInfo(
                    "FartingToggle = null (CRITICAL: will clone live Smothering under KinkToggles — never ContentSelectMenu).");

                if (ListOfToggles != null)
                    VSFartMod.Logger.LogInfo("Loaded ListOfToggles successfully.");
                else
                    VSFartMod.Logger.LogWarning("ListOfToggles not found in asset bundle.");
            }
        }

        // Current Unity rebuilds often omit NewLogo from the bundle — always allow a PNG on disk.
        if (NewLogo == null)
        {
            NewLogo = LoadTextureFromDisk(root, "NewLogo.png");
        }

        // TextAssets are usually missing from the rebuilt audio-only bundle — load CSVs from disk.
        LoadMissingTextAssetsFromDisk(root);

        VSFartMod.Logger.LogInfo(NewLogo != null
            ? $"NewLogo ready ({NewLogo.width}x{NewLogo.height})"
            : "NewLogo missing — put NewLogo.png in StreamingAssets/VSFartSounds/");

        VSFartMod.Logger.LogInfo(
            $"TextAssets: Tasklist={(TimedTasklist != null)} ListOfToggles={(ListOfToggles != null)} " +
            $"ToggleInfo={(ToggleInfo != null)} MenuExplanations={(MenuExplanations != null)} " +
            $"InteractionStrings={(InteractionStrings != null)}");
    }

    public static TextAsset GetReplacementTextAsset(string originalName)
    {
        if (string.IsNullOrEmpty(originalName))
        {
            return null;
        }

        string n = originalName.Trim();
        if (n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            n = Path.GetFileNameWithoutExtension(n);
        }

        // Hard block: Primary New has a different CSV schema than Timed New.
        // Injecting Timed over Primary causes GetCsvFieldByKey ArgumentOutOfRangeException spam.
        if (n.IndexOf("Primary", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return null;
        }

        // Exact match only for Timed New (never fuzzy "Tasklist" / "Timed").
        if (string.Equals(n, "Tasklist - Timed New", StringComparison.OrdinalIgnoreCase))
        {
            return TimedTasklist;
        }

        if (string.Equals(n, "ListOfToggles", StringComparison.OrdinalIgnoreCase))
        {
            return ListOfToggles;
        }

        if (string.Equals(n, "TogggleInfo", StringComparison.OrdinalIgnoreCase))
        {
            return ToggleInfo;
        }

        if (string.Equals(n, "MenuExplanations", StringComparison.OrdinalIgnoreCase))
        {
            return MenuExplanations;
        }

        if (string.Equals(n, "InteractionStrings", StringComparison.OrdinalIgnoreCase))
        {
            return InteractionStrings;
        }

        return null;
    }

    private static void LoadMissingTextAssetsFromDisk(string root)
    {
        TimedTasklist ??= LoadTextFileFromDisk(root, "Tasklist - Timed New.txt", "Tasklist - Timed New");
        ListOfToggles ??= LoadTextFileFromDisk(root, "ListOfToggles.txt", "ListOfToggles");
        ToggleInfo ??= LoadTextFileFromDisk(root, "TogggleInfo.txt", "TogggleInfo");
        MenuExplanations ??= LoadTextFileFromDisk(root, "MenuExplanations.txt", "MenuExplanations");
        InteractionStrings ??= LoadTextFileFromDisk(root, "InteractionStrings.txt", "InteractionStrings");
    }

    private static TextAsset LoadTextFileFromDisk(string streamingRoot, string fileName, string assetName)
    {
        string[] candidates =
        {
            Path.Combine(streamingRoot, "VSFartSounds", fileName),
            Path.Combine(streamingRoot, fileName),
        };

        foreach (string path in candidates)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                string text = File.ReadAllText(path);
                // Normalize CRLF so PlayMaker CSV parsers don't spam \r warnings as badly.
                text = text.Replace("\r\n", "\n").Replace('\r', '\n');
                // Unity TextAsset can reintroduce platform newlines; keep LF-only for PlayMaker CSV.
                text = text.Replace("\r", "");
                var ta = new TextAsset(text);
                ta.name = assetName;
                VSFartMod.Logger.LogInfo($"Loaded TextAsset from disk: {path} as '{assetName}' ({text.Length} chars)");
                return ta;
            }
            catch (Exception ex)
            {
                VSFartMod.Logger.LogError($"Failed reading {path}: {ex.Message}");
            }
        }

        return null;
    }

    private static string FindBundlePath(string root)
    {
        string[] candidates =
        {
            Path.Combine(root, "vsfartmod"),
            Path.Combine(root, "vsfartmod.bundle"),
            Path.Combine(root, "vsfartmod.unity3d"),
            Path.Combine(root, "AssetBundles", "vsfartmod"),
            Path.Combine(root, "AssetBundles", "vsfartmod.bundle"),
        };

        foreach (string path in candidates)
        {
            if (File.Exists(path) && !Directory.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static Texture2D LoadTexture(params string[] names)
    {
        if (assets == null) return null;
        foreach (string name in names)
        {
            var tex = assets.LoadAsset<Texture2D>(name);
            if (tex != null) return tex;
        }
        return null;
    }

    private static Texture2D LoadTextureFromDisk(string streamingRoot, string fileName)
    {
        string[] candidates =
        {
            Path.Combine(streamingRoot, "VSFartSounds", fileName),
            Path.Combine(streamingRoot, fileName),
            Path.Combine(streamingRoot, "VSFartSounds", "Farts", fileName),
        };

        foreach (string path in candidates)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, bytes, markNonReadable: true))
                {
                    VSFartMod.Logger.LogWarning($"Failed to decode logo PNG at {path}");
                    UnityEngine.Object.Destroy(tex);
                    continue;
                }

                tex.name = Path.GetFileNameWithoutExtension(fileName);
                VSFartMod.Logger.LogInfo($"Loaded logo PNG from disk: {path} ({tex.width}x{tex.height})");
                return tex;
            }
            catch (Exception ex)
            {
                VSFartMod.Logger.LogError($"Error loading logo from {path}: {ex}");
            }
        }

        return null;
    }

    private static TextAsset LoadText(params string[] names)
    {
        if (assets == null) return null;
        foreach (string name in names)
        {
            var text = assets.LoadAsset<TextAsset>(name);
            if (text != null) return text;
        }
        return null;
    }
}
