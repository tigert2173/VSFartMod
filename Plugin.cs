using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using VSFartMod;

namespace VSFartMod
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class VSFartMod : BaseUnityPlugin
    {
        public static new ManualLogSource Logger;
        public static ConfigEntry<FartEffects.GasinessMode> GasinessConfig;
        public static ConfigEntry<float> FrequencyScaleConfig;
        public static ConfigEntry<bool> KeepCameraClearConfig;
        public static ConfigEntry<float> MaxCloudOpacityConfig;
        public static ConfigEntry<bool> FaceSitCameraPullbackConfig;
        public static ConfigEntry<float> FaceSitPullBackConfig;
        public static ConfigEntry<float> FaceSitRaiseConfig;
        public static ConfigEntry<float> FaceSitLowerConfig;
        public static ConfigEntry<bool> UseCustomSmotherPosesConfig;
        public static ConfigEntry<float> SmotherBodyPullConfig;
        //private static GameObject face;
        //private static GameObject body;

        //private static GameObject prison;
        //private static GameObject hair;
        //private static Texture2D eye_texture;
        //private static Texture2D skin_texture;
        private static Texture2D NewLogo;
        private static GameObject KinkToggles;

        //private static Material skinmaterial;
        //private static Material faceskinmaterial;

        private static bool inSession = false;

        ////Disable the default in game background
        //private static bool DISABLE_DEFAULT_BACKGROUND = true;

        ////Disable default game hair
        //private static bool DISABLE_OTHER_HAIR = true;

        ////Heart eyes when thirsty
        //private static bool DISABLE_HEARTS = false;

        ////Override eye glow, make it one color or disable so it doesn't happen during moods.
        //private static bool CUSTOM_GLOW = false;
        //R G B A
        //0-1 (not 255)
        private static Color CUSTOM_GLOW_COLOR = new Color(0, 0, 0, 0);

        private void Awake()
        {
            /* 
             * Create logger object. You can use it to print to the console window with
             * LogInfo.LogInfo("message")
             */
            Logger = base.Logger;

            // Gassiness is part of Farting — no separate Content Select toggles.
            GasinessConfig = Config.Bind(
                "Farting",
                "Gassiness",
                FartEffects.GasinessMode.Medium,
                "Preset pacing while fart tasks are active. Cycle in-session with F11: Low, Medium, High, Constant, Random.");
            FrequencyScaleConfig = Config.Bind(
                "Farting",
                "FrequencyScale",
                1f,
                new ConfigDescription(
                    "Multiplier on fart rate (0.25–4). Higher = more frequent. In-session: [ less often, ] more often.",
                    new AcceptableValueRange<float>(0.25f, 4f)));
            KeepCameraClearConfig = Config.Bind(
                "Farting",
                "KeepCameraClear",
                true,
                "Always-on FX clarity (no Content Select row). Mist/spray stay translucent. Prefer No Blackout Smothering for pose distance.");
            MaxCloudOpacityConfig = Config.Bind(
                "Farting",
                "MaxCloudOpacity",
                0.2f,
                new ConfigDescription(
                    "Max mist/fog alpha when KeepCameraClear is on (0.05=very light, 0.45=heavier). Liquids use a slightly higher soft cap. Default 0.2.",
                    new AcceptableValueRange<float>(0.05f, 0.45f)));
            FaceSitCameraPullbackConfig = Config.Bind(
                "Farting",
                "FaceSitCameraPullback",
                false,
                "DEPRECATED — leave false. Large pullback framed her off-lens. Use FaceSitLower for a small aim nudge.");
            FaceSitPullBackConfig = Config.Bind(
                "Farting",
                "FaceSitPullBack",
                0f,
                new ConfigDescription(
                    "Optional local −Z pull during FaceSit. Default 0 — keep small if used.",
                    new AcceptableValueRange<float>(0f, 0.45f)));
            FaceSitRaiseConfig = Config.Bind(
                "Farting",
                "FaceSitRaise",
                0f,
                new ConfigDescription(
                    "Optional local +Y raise during FaceSit. Default 0.",
                    new AcceptableValueRange<float>(0f, 0.2f)));
            FaceSitLowerConfig = Config.Bind(
                "Farting",
                "FaceSitLower",
                0.035f,
                new ConfigDescription(
                    "Slight local −Y drop during FaceSit/ButtFaceSit so the view aims at the anus, not just the top of the crack.",
                    new AcceptableValueRange<float>(0f, 0.2f)));
            FartEffects.Gasiness = GasinessConfig.Value;
            FartEffects.FrequencyScale = FrequencyScaleConfig.Value;
            FartEffects.KeepCameraClear = KeepCameraClearConfig.Value;
            FartEffects.MaxCloudOpacity = MaxCloudOpacityConfig.Value;
            // Never re-enable deprecated large pullback flag.
            if (FaceSitCameraPullbackConfig != null && FaceSitCameraPullbackConfig.Value)
            {
                FaceSitCameraPullbackConfig.Value = false;
            }
            FaceSitCameraAdjust.PullBack = FaceSitPullBackConfig.Value;
            FaceSitCameraAdjust.Raise = FaceSitRaiseConfig.Value;
            FaceSitCameraAdjust.Lower = FaceSitLowerConfig.Value;
            FaceSitCameraAdjust.Enabled = FaceSitCameraAdjust.HasAnyOffset;
            UseCustomSmotherPosesConfig = Config.Bind(
                "Farting",
                "UseCustomSmotherPoses",
                true,
                "No Blackout Smothering: disable SkinOverlay during FaceSit. Body pull is separate (SmotherBodyPull; default 0).");
            SmotherBodyPullConfig = Config.Bind(
                "Farting",
                "SmotherBodyPull",
                0f,
                new ConfigDescription(
                    "Optional SuccuPosition push when No Blackout Smothering is on. Default 0 = no body move (overlay-only).",
                    new AcceptableValueRange<float>(0f, 0.35f)));
            FaceSitPoseInstaller.NoBlackoutSmothering = UseCustomSmotherPosesConfig.Value;
            FaceSitPoseInstaller.SmotherBodyPull = SmotherBodyPullConfig.Value;
            FartEffects.OnGasinessChanged = mode =>
            {
                if (GasinessConfig != null && GasinessConfig.Value != mode)
                {
                    GasinessConfig.Value = mode;
                }
            };
            FartEffects.OnFrequencyScaleChanged = scale =>
            {
                if (FrequencyScaleConfig != null &&
                    Math.Abs(FrequencyScaleConfig.Value - scale) > 0.001f)
                {
                    FrequencyScaleConfig.Value = scale;
                }
            };
            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

            // Load the assets into memory from your asset bundle file created in Unity.
            Assets.LoadAssets();

            NewLogo = Assets.NewLogo;

            // Inject Farting CSVs into PlayMaker ReadTextAsset (tasklist / toggles / strings).
            TextAssetInjection.Apply();

            // Gas / burp / pee VFX + SFX. Must attach to this plugin GameObject so Update runs.
            FartEffects.EnsureCreated(gameObject);
            FartMaskVisuals.EnsureCreated(gameObject);
            BurpEffects.EnsureCreated(gameObject);
            PissEffects.EnsureCreated(gameObject);
            ScatEffects.EnsureCreated(gameObject);
            SkunkGirlEffects.EnsureCreated(gameObject);
            SmellTortureEffects.EnsureCreated(gameObject);
            FaceSitCameraAdjust.EnsureCreated(gameObject);
            FaceSitPoseInstaller.EnsureCreated(gameObject);

            // This makes the "OnSceneLoaded" function call when a scene is loaded.
            SceneManager.sceneLoaded += OnSceneLoaded;

            StartCoroutine(EffectsBootWatchdog());
        }

        private IEnumerator EffectsBootWatchdog()
        {
            yield return new WaitForSecondsRealtime(2f);
            if (FartEffects.Instance == null)
            {
                Logger.LogWarning("FartEffects missing after 2s — recreating.");
                FartEffects.EnsureCreated(gameObject);
            }
            if (FartMaskVisuals.Instance == null)
            {
                Logger.LogWarning("FartMaskVisuals missing after 2s — recreating.");
                FartMaskVisuals.EnsureCreated(gameObject);
            }
            if (BurpEffects.Instance == null)
            {
                Logger.LogWarning("BurpEffects missing after 2s — recreating.");
                BurpEffects.EnsureCreated(gameObject);
            }
            if (PissEffects.Instance == null)
            {
                Logger.LogWarning("PissEffects missing after 2s — recreating.");
                PissEffects.EnsureCreated(gameObject);
            }
            if (ScatEffects.Instance == null)
            {
                Logger.LogWarning("ScatEffects missing after 2s — recreating.");
                ScatEffects.EnsureCreated(gameObject);
            }
            if (SkunkGirlEffects.Instance == null)
            {
                Logger.LogWarning("SkunkGirlEffects missing after 2s — recreating.");
                SkunkGirlEffects.EnsureCreated(gameObject);
            }
            if (SmellTortureEffects.Instance == null)
            {
                Logger.LogWarning("SmellTortureEffects missing after 2s — recreating.");
                SmellTortureEffects.EnsureCreated(gameObject);
            }
            if (FaceSitCameraAdjust.Instance == null)
            {
                Logger.LogWarning("FaceSitCameraAdjust missing after 2s — recreating.");
                FaceSitCameraAdjust.EnsureCreated(gameObject);
            }
            if (FaceSitPoseInstaller.Instance == null)
            {
                Logger.LogWarning("FaceSitPoseInstaller missing after 2s — recreating.");
                FaceSitPoseInstaller.EnsureCreated(gameObject);
            }
            Logger.LogInfo("Boot watchdog: FX ready (F8/F9 fart, F11 gas, F12 skunk, F10 mask, F7 burp, F6 pee, F5 scat, F4 smell).");
        }

        void ListGameObjectsInHierarchy(Transform parent)
        {
            // Print the current object's name
            Debug.Log(parent.name);

            // Recursively print all child objects
            foreach (Transform child in parent)
            {
                ListGameObjectsInHierarchy(child); // Recursively call for each child
            }
        }

        private Sprite TextureToSprite(Texture2D texture)
        {
            if (texture == null)
            {
                return null;
            }
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }

        private void TryApplyLogo(UnityEngine.UI.Image img, string context)
        {
            if (img == null)
            {
                return;
            }

            if (NewLogo == null)
            {
                Logger.LogWarning($"Skipping logo update ({context}): NewLogo texture not loaded.");
                return;
            }

            var sprite = TextureToSprite(NewLogo);
            if (sprite == null)
            {
                return;
            }

            img.sprite = sprite;
            Logger.LogInfo($"Logo image successfully updated ({context}).");
        }


        void LateUpdate()
        {
            if (inSession)
            {
                ////Set the custom glow color for eyes
                //if (CUSTOM_GLOW)
                //{
                //    if (face == null)
                //    {
                //        face = GameObjectHelper.GetGameObjectCheckFound("GirlCharacter/Face");
                //    }
                //    Material m = face.GetComponent<SkinnedMeshRenderer>().materials[1];
                //    m.SetColor("_EMISSION", CUSTOM_GLOW_COLOR);
                //}

                //// Nail emission color and body color example
                //// Delete this code if you dont want to change them
                //if (body == null)
                //{
                //    body = GameObjectHelper.GetGameObjectCheckFound("GirlCharacter/Body");
                //}
                ////nail
                //body.GetComponent<SkinnedMeshRenderer>().materials[2].SetColor("_Emission", new Color(0, 0, 0.3f, 1));
                ////body
                //body.GetComponent<SkinnedMeshRenderer>().materials[0].SetColor("_Color", new Color(0.2f, 0.2f, 1, 1));
            }
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            /* This checks if the scene being loaded is the "ExtraLoadScene" scene. It loads only after the entire session
             * scene has been loaded but before the loading screen dissapears, so it is a good time to override assets.
             */
            Debug.Log("Listing all GameObjects in the scene hierarchy:");
            ListGameObjectsInHierarchy(transform);

            Logger.LogInfo($"Scene {scene.name} is loaded!");
            if (scene.name == "SessionScene" || Equals(scene.name, Constants.SessionScene))
            {
                FaceSitPoseInstaller.EnsureCreated(gameObject);
                if (FaceSitPoseInstaller.Instance != null)
                {
                    FaceSitPoseInstaller.Instance.OnSessionLikelyLoaded();
                }
            }

            if (Equals(scene.name, Constants.SessionScene))
            {
                inSession = true;

                ////Add the custom background
                //ChangeBackground(Assets.background);

                ////Update Colors
                //ChangeColors();

                ////Add the custom hair
                //ChangeHair(Assets.hair);

                ////Update eye texture
                //ChangeEyes(Assets.eyes);
                
                //Change the "Yes" button text to "Yeah"
                //This is just to show how to mess with UI stuff
                EditButton("Yes", "Yeah");
            }
            else
            {
                inSession = false;
            }

            if (Equals(scene.name, Constants.LoaderScene))
            {
                try
                {
                    // Log all root objects in the scene
                    GameObject[] rootObjects = scene.GetRootGameObjects();
                    foreach (var rootObj in rootObjects)
                    {
                        Logger.LogInfo("Root object: " + rootObj.name);
                    }

                    // Try to find the Logo GameObject
                    GameObject logoObj = GameObject.Find("Loader/Main Camera/Canvas/Image/Image"); // If you know the name of the object
                    if (logoObj == null)
                    {
                        Logger.LogWarning("Logo GameObject not found by name.");
                    }
                    else
                    {
                        // Find the Image component
                        UnityEngine.UI.Image img = logoObj.GetComponent<UnityEngine.UI.Image>();
                        if (img != null)
                        {
                            TryApplyLogo(img, "LoaderScene");
                        }
                        else
                        {
                            Logger.LogWarning("Image component not found on Logo object.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to update logo: {ex}");
                }
            }
            if (Equals(scene.name, Constants.MenuScene))
            {
                try
                {
                    // Log all root objects in the scene
                    GameObject[] rootObjects = scene.GetRootGameObjects();
                    UnityEngine.UI.Image[] allImages = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Image>(true);
                    foreach (var img in allImages)
                    {
                        if (img.gameObject.name == "Logo")
                        {
                            TryApplyLogo(img, "Menu Logo scan");
                            break;
                        }
                    }


                    // Try to find the Logo GameObject
                    GameObject logoObj = GameObject.Find("Pre-Game/MainMenu/Camera Canvas/MenuManager/BaseMenu/Logo"); // If you know the name of the object
                    if (logoObj == null)
                    {
                        Logger.LogWarning("Logo GameObject not found by name.");
                    }
                    else
                    {
                        // Find the Image component
                        UnityEngine.UI.Image img = logoObj.GetComponent<UnityEngine.UI.Image>();
                        if (img != null)
                        {
                            TryApplyLogo(img, "BaseMenu/Logo");
                        }
                        else
                        {
                            Logger.LogWarning("Image component not found on Logo object.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to update logo: {ex}");
                }
                try
                {
                    // Start coroutine to replace the menu safely
                    StartCoroutine(WaitAndAddUnderKinkToggles());
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error replacing ContentSelectMenu: {ex}");
                }
            }

        }

        public static void NotifyGasinessCycled(FartEffects.GasinessMode mode)
        {
            if (GasinessConfig != null) GasinessConfig.Value = mode;
            SyncGasPaceToggleUi(mode);
        }

        private IEnumerator WaitAndAddUnderKinkToggles()
        {
            GameObject kinkToggles = null;

            // Wait until the object shows up in the scene
            while ((kinkToggles = GameObject.Find("Pre-Game/MainMenu/Camera Canvas/MenuManager/ContentSelectMenu/Categories/Kinks/Scroll View (1)/Viewport/Content/KinkToggles")) == null)
            {
                yield return null; // wait 1 frame
            }

            Logger.LogInfo("Found KinkToggles — wiring Farting / Skunk / Scat / Burping / Her Watersports / …");

            Transform donor =
                FindChildByName(kinkToggles.transform, "Smothering / Smothering")
                ?? FindChildByName(kinkToggles.transform, "Butt Worship / Butt Worship")
                ?? FindFirstKinkToggle(kinkToggles.transform);

            if (donor == null)
            {
                Logger.LogError("No donor kink toggle found to clone.");
                yield break;
            }

            EnsureClonedKink(kinkToggles.transform, donor, "Farting / Farting", "Farting",
                on =>
                {
                    FartEffects.FartingKinkEnabled = on;
                    if (on)
                    {
                        Logger.LogInfo($"Farting on — {FartEffects.DescribePace()} (F11 preset, [ ] frequency).");
                    }
                },
                v => FartEffects.FartingKinkEnabled = v);

            EnsureClonedKink(kinkToggles.transform, donor, "Fart Torture / Fart Torture", "Fart Torture",
                on => FartEffects.FartTortureEnabled = on,
                v => FartEffects.FartTortureEnabled = v);

            EnsureClonedKink(kinkToggles.transform, donor, "Burping / Burping", "Burping",
                on => BurpEffects.BurpingKinkEnabled = on,
                v => BurpEffects.BurpingKinkEnabled = v);

            EnsureClonedKink(kinkToggles.transform, donor, "Scat / Scat", "Scat",
                on => ScatEffects.ScatKinkEnabled = on,
                v => ScatEffects.ScatKinkEnabled = v);

            EnsureClonedKink(kinkToggles.transform, donor, "Skunk Girl / Skunk Girl", "Skunk Girl",
                on => SkunkGirlEffects.SkunkGirlEnabled = on,
                v => SkunkGirlEffects.SkunkGirlEnabled = v);

            EnsureClonedKink(kinkToggles.transform, donor, "Panty Sharting / Panty Sharting", "Panty Sharting",
                on => FartEffects.PantyShartingEnabled = on,
                v => FartEffects.PantyShartingEnabled = v);

            EnsureClonedKink(kinkToggles.transform, donor, "Stinky Socks / Stinky Socks", "Stinky Socks",
                on => SmellTortureEffects.StinkySocksEnabled = on,
                v => SmellTortureEffects.StinkySocksEnabled = v);

            EnsureClonedKink(kinkToggles.transform, donor, "Stinky Armpits / Stinky Armpits", "Stinky Armpits",
                on => SmellTortureEffects.StinkyArmpitsEnabled = on,
                v => SmellTortureEffects.StinkyArmpitsEnabled = v);

            // Visible settings in Content Select (not required by timed tasks).
            EnsureClonedKink(kinkToggles.transform, donor,
                "No Blackout Smothering / No Blackout Smothering", "No Blackout Smothering",
                on =>
                {
                    FaceSitPoseInstaller.NoBlackoutSmothering = on;
                    if (UseCustomSmotherPosesConfig != null) UseCustomSmotherPosesConfig.Value = on;
                    if (FaceSitPoseInstaller.Instance != null)
                    {
                        FaceSitPoseInstaller.Instance.ReapplyBodyPull();
                    }
                },
                _ =>
                {
                    bool want = UseCustomSmotherPosesConfig == null || UseCustomSmotherPosesConfig.Value;
                    FaceSitPoseInstaller.NoBlackoutSmothering = want;
                });
            ForceCheckState(
                FindChildByName(kinkToggles.transform, "No Blackout Smothering / No Blackout Smothering")?.gameObject,
                UseCustomSmotherPosesConfig == null || UseCustomSmotherPosesConfig.Value);

            // Remove old Keep Camera Clear row if a prior build cloned it.
            DestroyClonedIfPresent(kinkToggles.transform, "Keep Camera Clear / Keep Camera Clear");

            WireGasPaceToggles(kinkToggles.transform, donor);

            EnsureClonedKink(kinkToggles.transform, donor, "Her Watersports / Her Watersports", "Her Watersports",
                on => PissEffects.HerWatersportsEnabled = on,
                v => PissEffects.HerWatersportsEnabled = v);

            // Vanilla Watersports = your/self piss only (no her-on-you FX).
            Transform watersports =
                FindChildByName(kinkToggles.transform, "Watersports / Watersports")
                ?? FindChildByName(kinkToggles.transform, "Watersports");
            if (watersports != null)
            {
                WireKinkCheck(watersports.gameObject, "Watersports (you)",
                    on => PissEffects.WatersportsKinkEnabled = on,
                    v => PissEffects.WatersportsKinkEnabled = v);
            }
            else
            {
                Logger.LogWarning("Watersports toggle not found under KinkToggles.");
            }
        }

        private static readonly Dictionary<FartEffects.GasinessMode, string> GasPaceToggleNames =
            new Dictionary<FartEffects.GasinessMode, string>
            {
                { FartEffects.GasinessMode.Low, "Low Gas / Low Gas" },
                { FartEffects.GasinessMode.Medium, "Medium Gas / Medium Gas" },
                { FartEffects.GasinessMode.High, "High Gas / High Gas" },
                { FartEffects.GasinessMode.Constant, "Constant Gas / Constant Gas" },
                { FartEffects.GasinessMode.Random, "Random Gas / Random Gas" },
            };

        private static readonly Dictionary<FartEffects.GasinessMode, string> GasPaceLabels =
            new Dictionary<FartEffects.GasinessMode, string>
            {
                { FartEffects.GasinessMode.Low, "Low Gas" },
                { FartEffects.GasinessMode.Medium, "Medium Gas" },
                { FartEffects.GasinessMode.High, "High Gas" },
                { FartEffects.GasinessMode.Constant, "Constant Gas" },
                { FartEffects.GasinessMode.Random, "Random Gas" },
            };

        private static bool _suppressGasToggleEcho;
        private static Transform _gasToggleParent;

        private void WireGasPaceToggles(Transform kinkToggles, Transform donor)
        {
            _gasToggleParent = kinkToggles;
            foreach (var kv in GasPaceToggleNames)
            {
                var mode = kv.Key;
                EnsureClonedKink(kinkToggles, donor, kv.Value, GasPaceLabels[mode],
                    on => OnGasPaceToggleChanged(mode, on),
                    _ => { });
            }

            var want = GasinessConfig != null ? GasinessConfig.Value : FartEffects.Gasiness;
            FartEffects.Gasiness = want;
            SyncGasPaceToggleUi(want);
            Logger.LogInfo($"Gas pace UI synced to {want}.");
        }

        private static void OnGasPaceToggleChanged(FartEffects.GasinessMode mode, bool on)
        {
            if (_suppressGasToggleEcho) return;

            if (on)
            {
                FartEffects.Gasiness = mode;
                if (GasinessConfig != null) GasinessConfig.Value = mode;
                FartEffects.SetGasinessFromToggle(mode, true);
                SyncGasPaceToggleUi(mode);
                VSFartMod.Logger.LogInfo($"Fart pace → {FartEffects.DescribePace()}");
                return;
            }

            if (FartEffects.Gasiness == mode)
            {
                ForceCheckState(FindChildByName(_gasToggleParent, GasPaceToggleNames[mode])?.gameObject, true);
            }
        }

        private static void SyncGasPaceToggleUi(FartEffects.GasinessMode active)
        {
            if (_gasToggleParent == null) return;
            _suppressGasToggleEcho = true;
            try
            {
                foreach (var kv in GasPaceToggleNames)
                {
                    ForceCheckState(FindChildByName(_gasToggleParent, kv.Value)?.gameObject, kv.Key == active);
                }
            }
            finally
            {
                _suppressGasToggleEcho = false;
            }
        }

        private static void ForceCheckState(GameObject instance, bool on)
        {
            if (instance == null) return;

            var toggle = instance.GetComponentInChildren<UnityEngine.UI.Toggle>(true)
                         ?? instance.GetComponent<UnityEngine.UI.Toggle>();
            if (toggle != null)
            {
                toggle.SetIsOnWithoutNotify(on);
                return;
            }

            Transform check = FindChildByName(instance.transform, "Check");
            if (check != null)
            {
                check.gameObject.SetActive(on);
            }

            var tracker = instance.GetComponent<KinkCheckTracker>();
            if (tracker != null)
            {
                tracker.ForceLast(on);
            }
        }

        private static void DestroyClonedIfPresent(Transform parent, string targetName)
        {
            Transform existing = FindChildByName(parent, targetName);
            if (existing == null) return;
            VSFartMod.Logger.LogInfo($"Removing obsolete kink row '{targetName}'.");
            UnityEngine.Object.Destroy(existing.gameObject);
        }

        private static void EnsureClonedKink(
            Transform parent,
            Transform donor,
            string targetName,
            string label,
            Action<bool> onChanged,
            Action<bool> setInitial)
        {
            Transform existing = FindChildByName(parent, targetName);
            if (existing != null)
            {
                VSFartMod.Logger.LogInfo($"{targetName} already present.");
                WireKinkCheck(existing.gameObject, label, onChanged, setInitial);
                return;
            }

            VSFartMod.Logger.LogInfo($"Cloning '{donor.name}' → '{targetName}'.");
            GameObject instance = GameObject.Instantiate(donor.gameObject, parent);
            instance.name = targetName;
            instance.SetActive(true);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = new Vector3(0.0829f, 0.0472f, 0.1636f);
            RenameVisibleLabel(instance.transform, label);
            WireKinkCheck(instance, label, onChanged, setInitial);
        }

        private static void WireKinkCheck(
            GameObject instance,
            string label,
            Action<bool> onChanged,
            Action<bool> setInitial)
        {
            var toggle = instance.GetComponentInChildren<UnityEngine.UI.Toggle>(true)
                         ?? instance.GetComponent<UnityEngine.UI.Toggle>();

            if (toggle != null)
            {
                setInitial(toggle.isOn);
                toggle.onValueChanged.AddListener(on =>
                {
                    onChanged(on);
                    VSFartMod.Logger.LogInfo($"{label} kink {(on ? "ENABLED" : "disabled")}.");
                });
                VSFartMod.Logger.LogInfo($"Wired {label} UI.Toggle (start isOn={toggle.isOn}).");
                return;
            }

            Transform check = FindChildByName(instance.transform, "Check");
            if (check == null)
            {
                VSFartMod.Logger.LogWarning($"{label} has no Toggle/Check — leaving kink OFF.");
                setInitial(false);
                return;
            }

            var tracker = instance.GetComponent<KinkCheckTracker>();
            if (tracker == null)
            {
                tracker = instance.AddComponent<KinkCheckTracker>();
            }
            tracker.Bind(check.gameObject, label, onChanged, setInitial);
            VSFartMod.Logger.LogInfo($"Wired {label} via Check (start active={check.gameObject.activeSelf}).");
        }

        /// <summary>Polls the Check mark on PlayMaker-style kink toggles.</summary>
        private sealed class KinkCheckTracker : MonoBehaviour
        {
            private GameObject _check;
            private bool _last;
            private string _label;
            private Action<bool> _onChanged;

            public void Bind(GameObject check, string label, Action<bool> onChanged, Action<bool> setInitial)
            {
                _check = check;
                _label = label;
                _onChanged = onChanged;
                _last = check != null && check.activeInHierarchy;
                setInitial(_last);
            }

            public void ForceLast(bool on)
            {
                _last = on;
            }

            private void Update()
            {
                if (_check == null) return;
                bool on = _check.activeInHierarchy;
                if (on == _last) return;
                _last = on;
                _onChanged?.Invoke(on);
                VSFartMod.Logger.LogInfo($"{_label} kink {(on ? "ENABLED" : "disabled")} (Check).");
            }
        }

        private static Transform FindChildByName(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name)
                {
                    return child;
                }
            }
            // Also try recursive (layout wrappers).
            for (int i = 0; i < parent.childCount; i++)
            {
                var hit = FindChildByName(parent.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        private static Transform FindFirstKinkToggle(Transform parent)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name.Contains(" / ") && !child.name.TrimEnd().EndsWith("/"))
                {
                    return child;
                }
            }
            return null;
        }

        private static void RenameVisibleLabel(Transform root, string text)
        {
            var tmp = root.GetComponentsInChildren<TMPro.TMP_Text>(true);
            for (int i = 0; i < tmp.Length; i++)
            {
                if (tmp[i] == null || string.IsNullOrEmpty(tmp[i].text) || tmp[i].text.Length >= 40)
                {
                    continue;
                }

                if (tmp[i].text.IndexOf("Smothering", StringComparison.OrdinalIgnoreCase) >= 0
                    || tmp[i].text.IndexOf("Butt Worship", StringComparison.OrdinalIgnoreCase) >= 0
                    || tmp[i].text.IndexOf("Farting", StringComparison.OrdinalIgnoreCase) >= 0
                    || tmp[i].text.IndexOf("Burping", StringComparison.OrdinalIgnoreCase) >= 0
                    || tmp[i].text.IndexOf("Scat", StringComparison.OrdinalIgnoreCase) >= 0
                    || tmp[i].text.IndexOf("Skunk", StringComparison.OrdinalIgnoreCase) >= 0
                    || tmp[i].text.IndexOf("Panty", StringComparison.OrdinalIgnoreCase) >= 0
                    || tmp[i].text.IndexOf("Gas", StringComparison.OrdinalIgnoreCase) >= 0
                    || tmp[i].text.IndexOf("Stinky", StringComparison.OrdinalIgnoreCase) >= 0
                    || tmp[i].text.IndexOf("Watersports", StringComparison.OrdinalIgnoreCase) >= 0
                    || tmp[i].name.IndexOf("ButtonText", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    tmp[i].text = text;
                }
            }

            var ui = root.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            for (int i = 0; i < ui.Length; i++)
            {
                if (ui[i] != null && !string.IsNullOrEmpty(ui[i].text) && ui[i].text.Length < 40
                    && (ui[i].text.IndexOf("Smothering", StringComparison.OrdinalIgnoreCase) >= 0
                        || ui[i].text.IndexOf("Butt Worship", StringComparison.OrdinalIgnoreCase) >= 0
                        || ui[i].text.IndexOf("Farting", StringComparison.OrdinalIgnoreCase) >= 0
                        || ui[i].text.IndexOf("Burping", StringComparison.OrdinalIgnoreCase) >= 0
                        || ui[i].text.IndexOf("Scat", StringComparison.OrdinalIgnoreCase) >= 0
                        || ui[i].text.IndexOf("Skunk", StringComparison.OrdinalIgnoreCase) >= 0
                        || ui[i].text.IndexOf("Panty", StringComparison.OrdinalIgnoreCase) >= 0
                        || ui[i].text.IndexOf("Gas", StringComparison.OrdinalIgnoreCase) >= 0
                        || ui[i].text.IndexOf("Stinky", StringComparison.OrdinalIgnoreCase) >= 0
                        || ui[i].text.IndexOf("Watersports", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    ui[i].text = text;
                }
            }
        }




        // Add this helper method somewhere accessible (e.g. static class)
        GameObject FindInAllScenes(string path)
        {
            // Check active scenes
            List<GameObject> allRoots = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded) continue;
                allRoots.AddRange(s.GetRootGameObjects());
            }

            // Check DontDestroyOnLoad
            GameObject temp = new GameObject("TempSearchRoot");
            GameObject.DontDestroyOnLoad(temp);
            Scene ddolScene = temp.scene;
            GameObject.DestroyImmediate(temp);

            allRoots.AddRange(ddolScene.GetRootGameObjects());

            // Try to find the object by path
            foreach (var root in allRoots)
            {
                Transform t = root.transform.Find(path);
                if (t != null)
                    return t.gameObject;
            }

            return null;
        }


        //private void ChangeBackground(GameObject background)
        //{
        //    /*
        //    * Create an instance of the background asset in the 3D world.
        //    */
        //    prison = GameObject.Instantiate(background);

        //    if (DISABLE_DEFAULT_BACKGROUND)
        //    {
        //        try
        //        {
        //            GameObject defaultbg = GameObjectHelper.GetGameObjectCheckFound("Root/BasicWorld");
        //            defaultbg.SetActive(false);
        //            defaultbg = GameObjectHelper.GetGameObjectCheckFound("Root/World");
        //            defaultbg.SetActive(false);
        //        }
        //        catch (Exception ex)
        //        {
        //            //if the background was already disabled in settings
        //        }
        //    }

        //    //Put the prison in the session scene
        //    GameObject root = GameObjectHelper.GetGameObjectCheckFound("Root");
        //    prison.transform.SetParent(root.transform);
        //    //I found these positional values to work best for my hair background
        //    prison.transform.localPosition = new Vector3(0.3618f, 5.8927f, 34.4546f);
        //}

        //private void ChangeColors() {
        //    /*
        //     * These are a bunch of materials and parameters you can change the color of!
        //    */
        //    GameObject body = GameObjectHelper.GetGameObjectCheckFound("GirlCharacter/Body");
        //    GameObject face = GameObjectHelper.GetGameObjectCheckFound("GirlCharacter/Face");
        //    // Main body color
        //    // VS repeatedly sets this from somewhere else, similar to eye emission color.
        //    // SO MOVE THIS TO THE LateUpdate() FUNCTION TO MAKE IT ACTUALLY WORK
        //    body.GetComponent<SkinnedMeshRenderer>().materials[0].SetColor("_Color", new Color(0.2f, 0.2f, 1, 1));

        //    // Nail color
        //    body.GetComponent<SkinnedMeshRenderer>().materials[2].SetColor("_Color", new Color(0, 0, 1, 1));
        //    // Nail emmision color
        //    // VS repeatedly sets this from somewhere else, similar to eye emission color.
        //    // SO MOVE THIS TO THE LateUpdate() FUNCTION TO MAKE IT ACTUALLY WORK
        //    body.GetComponent<SkinnedMeshRenderer>().materials[2].SetColor("_Emission", new Color(0, 0, 0.3f, 1));

        //    // Face color, you probably want this to be the same as body color
        //    face.GetComponent<SkinnedMeshRenderer>().materials[7].SetColor("_Color", new Color(0.2f, 0.2f, 1, 1));
        //    // Blush color. The texture is already moslty red, so the colors are a bit finicky when edited. This will make it green.
        //    face.GetComponent<SkinnedMeshRenderer>().materials[7].SetColor("_FlushColor", new Color(0, 2, 0, 1));
        //}

        //private void ChangeHair(GameObject hairAsset)
        //{
        //    /*
        //     * Create an instance of the hair asset in the 3D world. We will still need to set it's position and parent, if we want
        //         * it to actually move with her head.
        //    */
        //    hair = GameObject.Instantiate(Assets.hair);
        //    /* 
        //     * GameObjectHelper.GetGameObjectCheckFound finds a game object given part of its location in the scene tree.
        //     * You should generally at least provide the gameobject you want to find (e.g. Hair1) and its parent (e.g. HairPosition)
        //     * to avoid name conflicts.
        //     * If it can't find a GameObject, an error will be sent to the console.
        //     * 
        //     * !!!NOTE: This function cannot find game objects that are not currently active.
        //     * For that, you would need to find a parent GameObject that is enabled, get its transform with gameobject.transform,
        //     * then use transform.Find("name") on the transform to get one of its children.
        //     * See https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Transform.Find.html
        //     */
        //    Logger.LogInfo("hair 1");
        //    GameObject base_hair = GameObjectHelper.GetGameObjectCheckFound("HairPosition/Hair1");
        //    Logger.LogInfo("hair 2");
        //    GameObject other_hair = GameObjectHelper.GetGameObjectCheckFound("HairPosition/AltHairs");
        //    Logger.LogInfo("hair done");

        //    /*
        //     * Disable any active hair. You should do this if you want to load a custom hairstyle.
        //     */
        //    if (DISABLE_OTHER_HAIR) {
        //        if (base_hair != null)
        //        {
        //            base_hair.SetActive(false);
        //            foreach (Transform hairstyle in base_hair.transform)
        //            {
        //                hairstyle.gameObject.SetActive(false);
        //            }
        //        }
        //        if (other_hair != null)
        //        {
        //            other_hair.SetActive(false);
        //            foreach (Transform hairstyle in other_hair.transform)
        //            {
        //                hairstyle.gameObject.SetActive(false);
        //            }
        //        }
        //    }

        //    //This is the object we want to parent the hair asset to.
        //    GameObject hair_pos = GameObjectHelper.GetGameObjectCheckFound("HairPosition");
        //    //Then we set the transform of our hair asset to be parented to the HairPosition GameObject. This means the hair will move with her haid now.
        //    hair.transform.SetParent(hair_pos.transform, false);

        //    //I found these positional values to work best for my hair asset. You will have to play with them in UnityExplorer to find the right positioning for yours.
        //    //You may also need to adjust scale.
        //    /*
        //     * The Quaternion.identity is just zero rotation. If you need something else:
        //     * Quaternion myRotation = Quaternion.identity;
        //     * myRotation.eulerAngles = new Vector3(0, 0, somezrotation);
        //     * is probably the easiest way to do it. 
        //     * Or you could just change the rotation of the hair in the editor before you export the asset bundle.
        //     */
        //    hair.transform.SetLocalPositionAndRotation(new Vector3(-0.002f, -0.025f, 0.031f), Quaternion.identity);
        //    //To adjust scale, if you need to:
        //    //hair.transform.localScale = new Vector3(1, 1, 1);
        //}

        //private void ChangeEyes(Texture2D eyeTexture)
        //{
        //    /*
        //     * Unlike a Prefab GameObject, you don't need to instance textures.
        //    */
        //    eye_texture = eyeTexture;
        //    //The material that holds the eye texture is on the Face GameObject.
        //    GameObject face = GameObjectHelper.GetGameObjectCheckFound("GirlCharacter/Face");
        //    face.GetComponent<SkinnedMeshRenderer>().materials[5].SetTexture("_MainTex", eye_texture);

        //    /*
        //     * This prevents the hearts from appearing on her eyes when in the "thirsty" mood.
        //     * You may want to do this depending on the character you are going for.
        //     * 
        //     * In general this method is a good way to disable features you can't turn off normally,
        //     * because sometimes just disabling a GameObject won't work because an FSM will re-enable it.
        //     * You will probably have to poke around and experiment to find the right FSM to empty out.
        //     */
        //    //Finds the FSM responsible for enabling the hearts (the 6th one attached to the face)
        //    if (DISABLE_HEARTS)
        //    {
        //        PlayMakerFSM thirsty_eyes = face.GetComponents<PlayMakerFSM>()[5];
        //        // Removes all of its actions in each event.
        //        foreach (HutongGames.PlayMaker.FsmState state in thirsty_eyes.FsmStates)
        //        {
        //            state.Actions = [];
        //        }
        //    }
        //}

        /*
         * This shows how to change some text on an object.
         * This specific method may not always work for all text, sometimes the text is reset by a script or FSM.
         */
        private void EditButton(string button_name, string new_text)
        {
            GameObject buttons = GameObjectHelper.GetGameObjectCheckFound("Positives ------------/");
            Transform button = buttons.transform.Find(button_name + "/DoneBG/DoneText/Text (TMP)");

            button.gameObject.GetComponent<TextMeshProUGUI>().SetText(new_text);
        }
    }
}
