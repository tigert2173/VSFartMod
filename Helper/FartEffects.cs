using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace VSFartMod
{
    /// <summary>
    /// Plays varied gas VFX + a large bank of fart SFX with shuffle-bag
    /// anti-repeat so the same clip is rarely heard twice in a row.
    /// </summary>
    public class FartEffects : MonoBehaviour
    {
        public static FartEffects Instance { get; private set; }

        public float scanInterval = 0.35f;
        // Default = realistic facesit pacing (not a gas machine).
        public float minBurstGap = 9f;
        public float maxBurstGap = 22f;
        public float volume = 0.7f;
        public bool playOnFacesitScenes = true;

        /// <summary>Press this key anytime to force a fart SFX + gas burst (for testing).</summary>
        public KeyCode debugBurstKey = KeyCode.F9;
        public KeyCode debugBurstKeyAlt = KeyCode.F8;

        /// <summary>User / Content Select gassiness. Random re-rolls every ~60–90s.</summary>
        public enum GasinessMode
        {
            Medium = 0,
            Low = 1,
            High = 2,
            Constant = 3,
            Random = 4
        }

        public static GasinessMode Gasiness
        {
            get => _gasiness;
            set
            {
                _gasiness = value;
                OnGasinessChanged?.Invoke(value);
            }
        }

        public static Action<GasinessMode> OnGasinessChanged;
        public static Action<float> OnFrequencyScaleChanged;
        public static bool PantyShartingEnabled { get; set; }

        /// <summary>
        /// When true (default), gas clouds stay translucent and sit off the lens so FaceSit never blacks out.
        /// </summary>
        public static bool KeepCameraClear { get; set; } = true;

        /// <summary>Max particle / fog alpha when KeepCameraClear is on (0.05–0.45).</summary>
        public static float MaxCloudOpacity
        {
            get => _maxCloudOpacity;
            set => _maxCloudOpacity = Mathf.Clamp(value, 0.05f, 0.45f);
        }

        /// <summary>
        /// Multiplier on fart rate. 1 = preset default. Higher = more frequent (shorter gaps).
        /// Clamped ~0.25–4. Adjust with [ ] or BepInEx config.
        /// </summary>
        public static float FrequencyScale
        {
            get => _frequencyScale;
            set
            {
                _frequencyScale = Mathf.Clamp(value, 0.25f, 4f);
                OnFrequencyScaleChanged?.Invoke(_frequencyScale);
            }
        }

        private static GasinessMode _gasiness = GasinessMode.Medium;
        private static float _frequencyScale = 1f;
        private static float _maxCloudOpacity = 0.2f;
        private GasinessMode _randomEffective = GasinessMode.Medium;
        private float _nextGasReroll;
        private bool _f11WasDown;
        private bool _lbrackWasDown;
        private bool _rbrackWasDown;
        private string _hudText;
        private float _hudUntil;

        private readonly List<AudioClip> _clips = new List<AudioClip>();
        private readonly Queue<int> _recent = new Queue<int>();
        private readonly List<int> _bag = new List<int>();
        private int _recentLimit = 24;

        private ParticleSystem _gas;
        private ParticleSystem _maskTube;
        private ParticleSystem _dutchCloud;
        private AudioSource _audio;
        private AudioSource _audioBody;
        private float _nextScan;
        private float _nextBurst;
        private Texture2D _gasTex;
        private bool _autoTestDone;
        private bool _loggedFirstUpdate;
        private bool _f8WasDown;
        private bool _f9WasDown;
        private bool _shiftFWasDown;

        // Anus / cheek morph state (Butthole blend shape + Butt_L/R bones).
        private SkinnedMeshRenderer _bodySmr;
        private int _buttholeShapeIndex = -1;
        private Transform _buttL;
        private Transform _buttR;
        private Quaternion _buttLBaseRot;
        private Quaternion _buttRBaseRot;
        private bool _buttBasesCached;
        private float _anusTarget;
        private float _anusCurrent;
        private float _anusHoldUntil;
        private float _cheekPulse;
        private float _loggedContextAt = -999f;

        private static readonly string[] SceneHooks =
        {
            // Actual SessionScene object names under SpecialAnimations:
            "NoIK_ButtFaceSit",
            "NoIK_FaceSit",
            "VSFart_ButtFaceSit",
            "VSFart_FaceSit",
            // Legacy / tasklist tokens (kept as fallback):
            "Special_NoIK_ButtFaceSit",
            "Special_NoIK_FaceSit",
            "Special_VSFart_ButtFaceSit",
            "Special_VSFart_FaceSit",
        };

        /// <summary>Set from the Farting Content Select toggle so session FX know the kink is on.</summary>
        public static bool FartingKinkEnabled { get; set; }

        /// <summary>Fart Torture mode — denser bursts when fart task text is active.</summary>
        public static bool FartTortureEnabled { get; set; }

        /// <summary>One-shot auto test on boot. Off by default once audio path is proven.</summary>
        public bool autoTestOnBoot = false;

        private static readonly Color[] GasColors =
        {
            new Color(0.45f, 0.75f, 0.35f, 0.28f),
            new Color(0.55f, 0.70f, 0.25f, 0.25f),
            new Color(0.35f, 0.65f, 0.40f, 0.30f),
            new Color(0.60f, 0.62f, 0.30f, 0.24f),
            new Color(0.40f, 0.58f, 0.28f, 0.26f),
        };

        public static void EnsureCreated(GameObject host = null)
        {
            if (Instance != null)
            {
                return;
            }

            // IMPORTANT: Do not create a free-floating GameObject during plugin load —
            // it often never receives Update (or gets destroyed on first scene load).
            // Attach to BepInEx's persistent plugin GameObject instead.
            if (host != null)
            {
                VSFartMod.Logger.LogInfo($"Attaching FartEffects to host '{host.name}'.");
                host.AddComponent<FartEffects>();
                return;
            }

            var go = new GameObject("VSFartEffects");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<FartEffects>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            LoadClips();
            BuildGasSystem();
            BuildMaskTubeSystem();
            BuildDutchOvenCloud();
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.loop = false;
            _audio.mute = false;
            _audio.spatialBlend = 0f; // always 2D so menu/session hearing is reliable
            _audio.bypassListenerEffects = true;
            _audio.ignoreListenerPause = true;
            _audio.priority = 0;
            _audio.volume = 1f;
            _audio.rolloffMode = AudioRolloffMode.Linear;
            _audio.maxDistance = 50f;

            // Quieter low body layer — makes clips feel less thin/cartoon.
            _audioBody = gameObject.AddComponent<AudioSource>();
            _audioBody.playOnAwake = false;
            _audioBody.loop = false;
            _audioBody.spatialBlend = 0f;
            _audioBody.bypassListenerEffects = true;
            _audioBody.ignoreListenerPause = true;
            _audioBody.priority = 32;
            _audioBody.volume = 0.45f;
            _nextBurst = Time.unscaledTime + 8f;

            // Ensure streamed/compressed bundle clips are ready.
            int loaded = 0;
            for (int i = 0; i < _clips.Count; i++)
            {
                var c = _clips[i];
                if (c == null) continue;
                if (c.loadState != AudioDataLoadState.Loaded)
                {
                    c.LoadAudioData();
                }
                if (c.loadState == AudioDataLoadState.Loaded || c.samples > 0)
                {
                    loaded++;
                }
            }

            VSFartMod.Logger.LogInfo(
                $"FartEffects ready with {_clips.Count} clips ({loaded} audio-data ready). " +
                $"Debug: F8/F9 fart, F11 gassiness cycle. Anus morph uses Body 'Butthole' blend shape. AudioListener={(AudioListener.pause ? "PAUSED" : "ok")}");
            _nextGasReroll = Time.unscaledTime + 5f;
            RerollRandomGasiness(force: true);
        }

        private void LateUpdate()
        {
            // Run after PlayMaker ButtholeManager so our fart wink wins for the burst window.
            UpdateAnusMorph(Time.unscaledDeltaTime);
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(_hudText) || Time.unscaledTime > _hudUntil) return;
            float a = Mathf.Clamp01((_hudUntil - Time.unscaledTime) / 0.4f);
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, a);
            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            GUI.Box(new Rect(Screen.width * 0.5f - 220f, 28f, 440f, 44f), _hudText, style);
            GUI.color = prev;
        }

        private void ShowHud(string text)
        {
            _hudText = text;
            _hudUntil = Time.unscaledTime + 2.4f;
        }

        private void Update()
        {
            if (!_loggedFirstUpdate)
            {
                _loggedFirstUpdate = true;
                VSFartMod.Logger.LogInfo("FartEffects Update is running (input poll active).");
            }

            if (!_autoTestDone && autoTestOnBoot && Time.unscaledTime >= 4f)
            {
                _autoTestDone = true;
                ForceBurst("auto startup test");
            }

            if (WasDebugBurstPressed())
            {
                ForceBurst("debug key");
            }

            if (WasF11Pressed())
            {
                CycleGasiness();
            }

            PollFrequencyKeys();

            if (Gasiness == GasinessMode.Random && Time.unscaledTime >= _nextGasReroll)
            {
                RerollRandomGasiness(force: true);
            }

            if (Time.unscaledTime < _nextScan)
            {
                return;
            }

            _nextScan = Time.unscaledTime + scanInterval;
            if (!IsFartContextActive())
            {
                return;
            }

            if (Time.unscaledTime >= _nextBurst)
            {
                bool mask = TaskMentionsMaskTube();
                bool dutch = TaskMentionsDutchOven();
                bool shart = TaskMentionsPantyShart();
                PlayBurst(maskMode: mask, dutchMode: dutch, shartMode: shart);
                GetBurstGaps(mask || dutch, out float minGap, out float maxGap);
                float gap = UnityEngine.Random.Range(minGap, maxGap);
                // Natural pause — sometimes she just sits there without another one right away.
                var g = GetEffectiveGasiness();
                if (g == GasinessMode.Medium || g == GasinessMode.Low)
                {
                    if (UnityEngine.Random.value < 0.3f)
                    {
                        gap *= UnityEngine.Random.Range(1.6f, 2.8f);
                    }
                }
                _nextBurst = Time.unscaledTime + gap;
            }
        }

        private bool WasF11Pressed()
        {
            bool f11 = NativeInput.IsKeyDown(0x7A); // VK_F11
            bool edge = f11 && !_f11WasDown;
            _f11WasDown = f11;
            try
            {
                if (Input.GetKeyDown(KeyCode.F11)) return true;
            }
            catch { /* ignored */ }
            return edge;
        }

        private void PollFrequencyKeys()
        {
            bool l = NativeInput.IsKeyDown(0xDB); // [
            bool r = NativeInput.IsKeyDown(0xDD); // ]
            bool lEdge = l && !_lbrackWasDown;
            bool rEdge = r && !_rbrackWasDown;
            _lbrackWasDown = l;
            _rbrackWasDown = r;
            try
            {
                if (Input.GetKeyDown(KeyCode.LeftBracket)) lEdge = true;
                if (Input.GetKeyDown(KeyCode.RightBracket)) rEdge = true;
            }
            catch { /* ignored */ }

            if (lEdge) AdjustFrequency(-0.25f);
            if (rEdge) AdjustFrequency(+0.25f);
        }

        public static void CycleGasiness()
        {
            int next = ((int)Gasiness + 1) % 5;
            Gasiness = (GasinessMode)next;
            if (Instance != null)
            {
                Instance.RerollRandomGasiness(force: true);
                Instance.ShowHud($"Fart pace: {DescribePace()}");
            }
            VSFartMod.Logger.LogInfo($"Farting gassiness → {Gasiness}, freq×{FrequencyScale:0.##}");
            // Keep Content Select gas rows in sync when F11 is used.
            try { VSFartMod.NotifyGasinessCycled(Gasiness); } catch { /* menu may not be up */ }
        }

        public static void AdjustFrequency(float delta)
        {
            FrequencyScale = FrequencyScale + delta;
            if (Instance != null)
            {
                Instance.ShowHud($"Fart pace: {DescribePace()}");
            }
            VSFartMod.Logger.LogInfo($"Fart frequency scale → ×{FrequencyScale:0.##} ({DescribePace()})");
        }

        public static string DescribePace()
        {
            GetPreviewGaps(out float minG, out float maxG);
            return $"{Gasiness}  ×{FrequencyScale:0.##}  (~{minG:0.#}–{maxG:0.#}s between)";
        }

        private static void GetPreviewGaps(out float minGap, out float maxGap)
        {
            if (Instance != null)
            {
                Instance.GetBurstGaps(false, out minGap, out maxGap);
                return;
            }
            minGap = 9f / Mathf.Max(0.25f, FrequencyScale);
            maxGap = 22f / Mathf.Max(0.25f, FrequencyScale);
        }

        /// <summary>Legacy helper — gas toggles removed; prefer CycleGasiness / config.</summary>
        public static void SetGasinessFromToggle(GasinessMode mode, bool enabled)
        {
            if (enabled) Gasiness = mode;
            else if (Gasiness == mode) Gasiness = GasinessMode.Medium;
            if (Instance != null) Instance.RerollRandomGasiness(force: true);
        }

        private void RerollRandomGasiness(bool force = false)
        {
            _nextGasReroll = Time.unscaledTime + UnityEngine.Random.Range(55f, 95f);
            if (Gasiness != GasinessMode.Random && !force) return;
            if (Gasiness != GasinessMode.Random)
            {
                _randomEffective = Gasiness;
                return;
            }

            // Weighted toward realistic: Low/Medium most of the time. Constant is rare.
            float roll = UnityEngine.Random.value;
            if (roll < 0.35f) _randomEffective = GasinessMode.Low;
            else if (roll < 0.75f) _randomEffective = GasinessMode.Medium;
            else if (roll < 0.92f) _randomEffective = GasinessMode.High;
            else _randomEffective = GasinessMode.Constant;

            VSFartMod.Logger.LogInfo($"Random gassiness stretch → {_randomEffective} (reroll in ~{_nextGasReroll - Time.unscaledTime:F0}s)");
        }

        private GasinessMode GetEffectiveGasiness()
        {
            return Gasiness == GasinessMode.Random ? _randomEffective : Gasiness;
        }

        private void GetBurstGaps(bool denseScenario, out float minGap, out float maxGap)
        {
            switch (GetEffectiveGasiness())
            {
                case GasinessMode.Low:
                    // Sparse — a few over a long facesit.
                    minGap = 16f; maxGap = 35f;
                    break;
                case GasinessMode.High:
                    // Busy but not a machine gun.
                    minGap = 4f; maxGap = 9f;
                    break;
                case GasinessMode.Constant:
                    // Opt-in only (Content Select / F11).
                    minGap = 1.2f; maxGap = 2.8f;
                    break;
                default:
                    // Medium = realistic facesit amount.
                    minGap = minBurstGap; maxGap = maxBurstGap;
                    break;
            }

            // Dutch oven / mask / torture: a bit denser, still not constant unless Constant mode.
            if (denseScenario || FartTortureEnabled)
            {
                if (GetEffectiveGasiness() != GasinessMode.Constant)
                {
                    minGap *= 0.7f;
                    maxGap *= 0.75f;
                    minGap = Mathf.Max(3.5f, minGap);
                }
                else
                {
                    minGap *= 0.65f;
                    maxGap *= 0.7f;
                    minGap = Mathf.Max(0.7f, minGap);
                }
            }

            // Skunk girl: mild bump only.
            if (SkunkGirlEffects.SkunkGirlEnabled && GetEffectiveGasiness() != GasinessMode.Constant)
            {
                minGap *= 0.9f;
                maxGap *= 0.92f;
            }

            // User frequency slider ([ ] / config). Higher = more farts = shorter gaps.
            float scale = Mathf.Clamp(FrequencyScale, 0.25f, 4f);
            minGap /= scale;
            maxGap /= scale;
            minGap = Mathf.Max(0.45f, minGap);
            maxGap = Mathf.Max(minGap + 0.2f, maxGap);
        }

        private bool WasDebugBurstPressed()
        {
            // Win32 async keys — works even when Unity Input / debug consoles eat F-keys.
            bool f8 = NativeInput.IsKeyDown(0x77); // VK_F8
            bool f9 = NativeInput.IsKeyDown(0x78); // VK_F9
            bool shift = NativeInput.IsKeyDown(0xA1); // VK_RSHIFT
            bool f = NativeInput.IsKeyDown(0x46); // 'F'

            bool f8Edge = f8 && !_f8WasDown;
            bool f9Edge = f9 && !_f9WasDown;
            bool shiftFEdge = shift && f && !_shiftFWasDown;

            _f8WasDown = f8;
            _f9WasDown = f9;
            _shiftFWasDown = shift && f;

            if (f8Edge || f9Edge || shiftFEdge)
            {
                return true;
            }

            // Also keep Unity Input as a secondary path.
            try
            {
                if (Input.GetKeyDown(debugBurstKey) || Input.GetKeyDown(debugBurstKeyAlt))
                {
                    return true;
                }
                if (Input.GetKey(KeyCode.RightShift) && Input.GetKeyDown(KeyCode.F))
                {
                    return true;
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }

        /// <summary>Force a fart burst regardless of facesit/task context.</summary>
        public void ForceBurst(string reason = "manual")
        {
            if (_clips.Count == 0)
            {
                VSFartMod.Logger.LogWarning($"ForceBurst ({reason}): no clips loaded.");
                return;
            }

            var clip = NextClip();
            if (clip == null)
            {
                VSFartMod.Logger.LogWarning($"ForceBurst ({reason}): NextClip returned null.");
                return;
            }

            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                clip.LoadAudioData();
            }

            VSFartMod.Logger.LogInfo(
                $"ForceBurst ({reason}): '{clip.name}' len={clip.length:F2}s samples={clip.samples} " +
                $"loadState={clip.loadState} listenerPause={AudioListener.pause}");

            if (AudioListener.pause)
            {
                AudioListener.pause = false;
                VSFartMod.Logger.LogInfo("ForceBurst: cleared AudioListener.pause");
            }

            if (_audio == null)
            {
                _audio = gameObject.AddComponent<AudioSource>();
            }

            _audio.spatialBlend = 0f;
            _audio.mute = false;
            _audio.enabled = true;
            _audio.ignoreListenerPause = true;
            _audio.bypassListenerEffects = true;
            _audio.volume = 1f;
            _audio.pitch = 1f;
            _audio.Stop();
            _audio.clip = clip;
            _audio.Play();

            AnimateAnusForFart(strong: true);

            // Particles if available (best-effort).
            try
            {
                PlayGasOnly(strong: true);
            }
            catch (Exception ex)
            {
                VSFartMod.Logger.LogWarning($"ForceBurst gas failed: {ex.Message}");
            }

            _nextBurst = Time.unscaledTime + UnityEngine.Random.Range(minBurstGap, maxBurstGap);
        }

        private void PlayGasOnly(bool strong = false)
        {
            if (_gas == null) return;

            var cam = Camera.main;
            Vector3 pos = cam != null
                ? cam.transform.position + cam.transform.forward * ClarityCamDistance(0.55f)
                + cam.transform.up * -0.12f
                : transform.position;

            var hips = FindBone("Hips") ?? FindBone("hip") ?? FindBone("Pelvis");
            if (hips != null)
            {
                pos = hips.position + hips.TransformDirection(new Vector3(0f, -0.04f, -0.14f));
            }

            Color gas = ClarityColor(GasColors[UnityEngine.Random.Range(0, GasColors.Length)]);
            var main = _gas.main;
            main.startColor = gas;
            float sizeMul = ClaritySizeMul();
            main.startSize = strong
                ? new ParticleSystem.MinMaxCurve(0.1f * sizeMul, 0.24f * sizeMul)
                : new ParticleSystem.MinMaxCurve(0.07f * sizeMul, 0.18f * sizeMul);
            _gas.transform.position = pos;
            if (cam != null)
            {
                _gas.transform.rotation = Quaternion.LookRotation(
                    (cam.transform.forward + Vector3.up * UnityEngine.Random.Range(0.05f, 0.35f)).normalized);
            }

            var emission = _gas.emission;
            int lo = ClarityCount(strong ? 10 : 6);
            int hi = ClarityCount(strong ? 16 : 11);
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, (short)lo, (short)hi)
            });

            _gas.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _gas.Play();
        }

        private static Color ClarityColor(Color c) => KinkFxShared.ClarityColor(c);

        private static float ClaritySizeMul() => KinkFxShared.ClaritySizeMul();

        private static float ClarityCamDistance(float preferred) => KinkFxShared.ClarityCamDistance(preferred);

        private static int ClarityCount(int n) => KinkFxShared.ClarityCount(n);

        private void LoadClips()
        {
            _clips.Clear();

            if (Assets.FartSounds != null)
            {
                for (int i = 0; i < Assets.FartSounds.Length; i++)
                {
                    if (Assets.FartSounds[i] != null)
                    {
                        _clips.Add(Assets.FartSounds[i]);
                    }
                }
            }

            // Always also try StreamingAssets so sounds work before a Unity rebuild.
            // NOTE: folder must NOT be named "VSFartMod" — on Windows that collides with the
            // case-insensitive asset bundle file "vsfartmod".
            string[] soundFolders =
            {
                Path.Combine(Application.streamingAssetsPath, "VSFartSounds", "Farts"),
                Path.Combine(Application.streamingAssetsPath, "Farts"),
                Path.Combine(Application.streamingAssetsPath, "VSFartMod", "Farts"), // legacy
            };

            foreach (string folder in soundFolders)
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                foreach (string file in Directory.GetFiles(folder, "Fart_*.wav"))
                {
                    try
                    {
                        var clip = WavLoader.LoadClip(file);
                        if (clip != null)
                        {
                            _clips.Add(clip);
                        }
                    }
                    catch (Exception ex)
                    {
                        VSFartMod.Logger.LogWarning($"Failed loading {file}: {ex.Message}");
                    }
                }
            }

            // Deduplicate by name (bundle + folder may overlap after rebuild).
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = _clips.Count - 1; i >= 0; i--)
            {
                string key = _clips[i].name;
                if (!seen.Add(key))
                {
                    _clips.RemoveAt(i);
                }
            }

            if (_clips.Count == 0)
            {
                VSFartMod.Logger.LogWarning("No fart WAVs found; generating emergency procedural clips (will sound synthetic).");
                for (int i = 0; i < 12; i++)
                {
                    _clips.Add(ProceduralFart.Create($"ProcFart_{i}", 2000 + i * 31));
                }
            }
            else
            {
                VSFartMod.Logger.LogInfo($"Loaded {_clips.Count} fart clips from disk (prefer real recordings in VSFartSounds/Farts).");
            }

            _recentLimit = Mathf.Clamp(_clips.Count / 2, 8, 32);
            RefillBag();
        }

        private void RefillBag()
        {
            _bag.Clear();
            for (int i = 0; i < _clips.Count; i++)
            {
                _bag.Add(i);
            }

            // Fisher-Yates
            for (int i = _bag.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                int tmp = _bag[i];
                _bag[i] = _bag[j];
                _bag[j] = tmp;
            }
        }

        private AudioClip NextClip()
        {
            if (_clips.Count == 0)
            {
                return null;
            }

            if (_bag.Count == 0)
            {
                RefillBag();
            }

            while (_bag.Count > 0)
            {
                int idx = _bag[_bag.Count - 1];
                _bag.RemoveAt(_bag.Count - 1);

                // Skip recently used clips when alternatives remain.
                if (_recent.Contains(idx) && _bag.Count > 0)
                {
                    continue;
                }

                _recent.Enqueue(idx);
                while (_recent.Count > _recentLimit)
                {
                    _recent.Dequeue();
                }

                return _clips[idx];
            }

            return _clips[UnityEngine.Random.Range(0, _clips.Count)];
        }

        private bool IsFartContextActive()
        {
            if (!KinkFxShared.IsSessionActive())
            {
                return false;
            }

            // Farting kink must be on — otherwise leftover UI text must not trigger FX.
            if (!FartingKinkEnabled && !PantyShartingEnabled)
            {
                return false;
            }

            // If a sock/armpit torture instruction is on screen, do NOT play fart FX
            // unless that same line also explicitly says she farts (true combo task).
            // Fixes Gas-toggle / Farting blurbs leaking FX into Stinky Armpits tasks.
            string smellHit = KinkFxShared.FindActiveInstructionContaining(SmellTortureEffects.ArmpitNeedlesPublic)
                              ?? KinkFxShared.FindActiveInstructionContaining(SmellTortureEffects.SockNeedlesPublic);
            if (smellHit != null && !IsExplicitFartAction(smellHit))
            {
                return false;
            }

            // Task instruction text only. Pose+kink alone caused endless bursts on FaceSit intros.
            string hit = KinkFxShared.FindActiveInstructionContaining(FartTaskNeedles);
            if (hit == null && PantyShartingEnabled)
            {
                hit = KinkFxShared.FindActiveInstructionContaining(PantyShartNeedles);
            }

            if (hit == null)
            {
                return false;
            }

            if (Time.unscaledTime - _loggedContextAt > 8f)
            {
                _loggedContextAt = Time.unscaledTime;
                string preview = hit.Length > 90 ? hit.Substring(0, 90) + "…" : hit;
                VSFartMod.Logger.LogInfo($"Fart FX from task text: {preview}");
            }

            return true;
        }

        private static bool IsExplicitFartAction(string text)
        {
            return KinkFxShared.ContainsAny(text, ExplicitFartNeedles);
        }

        private static readonly string[] ExplicitFartNeedles =
        {
            "fart", "farts", "farting", "shart", "dutch oven", "dutch-oven", "fart mask", "gas chamber"
        };

        private static readonly string[] FartTaskNeedles =
        {
            "fart", "farts", "farting", "dutch oven", "fart mask", "gas chamber"
        };

        private static readonly string[] PantyShartNeedles =
        {
            "shart", "panty shart", "sharts in", "through her panties"
        };

        private bool IsAnyPoseHookActive()
        {
            for (int i = 0; i < SceneHooks.Length; i++)
            {
                var hook = GameObject.Find(SceneHooks[i]);
                if (hook != null && hook.activeInHierarchy)
                {
                    return true;
                }
            }

            // Only look under SpecialAnimations (session poses), not whole scene/UI.
            var special = GameObject.Find("SpecialAnimations");
            if (special == null)
            {
                return false;
            }

            var transforms = special.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (t == null || !t.gameObject.activeInHierarchy)
                {
                    continue;
                }

                string n = t.name;
                if (n.IndexOf("ButtFaceSit", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("VSFart_ButtFaceSit", StringComparison.OrdinalIgnoreCase) >= 0
                    || (n.IndexOf("FaceSit", StringComparison.OrdinalIgnoreCase) >= 0
                        && n.IndexOf("FaceStomp", StringComparison.OrdinalIgnoreCase) < 0))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TaskTextMentionsFart()
        {
            return KinkFxShared.AnyActiveUiTextContains(FartTaskNeedles);
        }

        private static bool TaskMentionsPantyShart()
        {
            return KinkFxShared.AnyActiveUiTextContains(PantyShartNeedles);
        }

        private static bool TaskMentionsMaskTube()
        {
            return KinkFxShared.AnyActiveUiTextContains("fart mask", "through the tube", "down the tube");
        }

        private static bool TaskMentionsDutchOven()
        {
            return KinkFxShared.AnyActiveUiTextContains("dutch oven", "dutch-oven");
        }

        private void PlayBurst(bool maskMode = false, bool dutchMode = false, bool shartMode = false)
        {
            var cam = Camera.main;
            Vector3 hipPos = transform.position;
            var hips = FindBone("Hips") ?? FindBone("hip") ?? FindBone("Pelvis");
            if (hips != null)
            {
                hipPos = hips.position + hips.TransformDirection(new Vector3(0f, -0.04f, -0.14f));
            }
            else if (cam != null)
            {
                hipPos = cam.transform.position + cam.transform.forward * 0.55f + cam.transform.up * -0.12f;
            }

            float intensity = UnityEngine.Random.Range(maskMode || dutchMode || shartMode ? 0.55f : 0.4f, 0.85f);
            bool strong = intensity > 0.65f || maskMode || dutchMode;
            Color gas = shartMode
                ? ClarityColor(new Color(0.55f, 0.42f, 0.18f, 0.32f))
                : ClarityColor(GasColors[UnityEngine.Random.Range(0, GasColors.Length)]);
            float sizeMul = ClaritySizeMul();

            // Standard hip burst (still plays for dutch oven / normal / shart).
            if (_gas != null && !maskMode)
            {
                var main = _gas.main;
                main.startColor = gas;
                main.startSize = shartMode
                    ? new ParticleSystem.MinMaxCurve((0.06f + intensity * 0.05f) * sizeMul, (0.14f + intensity * 0.1f) * sizeMul)
                    : new ParticleSystem.MinMaxCurve((0.07f + intensity * 0.06f) * sizeMul, (0.18f + intensity * 0.14f) * sizeMul);
                _gas.transform.position = hipPos;
                if (cam != null)
                {
                    Vector3 look = shartMode
                        ? (hips != null ? hips.TransformDirection(new Vector3(0f, -0.2f, -0.8f)) : -cam.transform.forward)
                        : (cam.transform.forward + Vector3.up * UnityEngine.Random.Range(0.05f, 0.35f));
                    if (look.sqrMagnitude < 0.01f) look = Vector3.down;
                    _gas.transform.rotation = Quaternion.LookRotation(look.normalized);
                }

                var shape = _gas.shape;
                shape.angle = shartMode
                    ? UnityEngine.Random.Range(18f, 30f)
                    : dutchMode ? UnityEngine.Random.Range(20f, 36f) : UnityEngine.Random.Range(14f, 28f);

                var emission = _gas.emission;
                // Keep bursts light so FaceSit/smothering stays readable.
                int lo = ClarityCount(5 + (int)(intensity * 6f));
                int hi = ClarityCount(9 + (int)(intensity * 10f));
                if (dutchMode) { lo += ClarityCount(2); hi += ClarityCount(4); }
                if (shartMode) { lo = Mathf.Max(3, lo - 2); hi = Mathf.Max(5, hi - 3); }
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)lo, (short)hi) });

                _gas.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _gas.Play();
            }

            // Tube → mask: stream from hips toward camera face, then fill around the lens.
            if (maskMode && cam != null)
            {
                PlayMaskTubeGas(cam, hips != null ? hips.position : hipPos, gas, intensity);
                if (FartMaskVisuals.Instance != null)
                {
                    float fog = KeepCameraClear
                        ? Mathf.Min(0.22f + intensity * 0.18f, MaxCloudOpacity)
                        : 0.22f + intensity * 0.18f;
                    FartMaskVisuals.Instance.PulseGasFog(fog);
                }
            }

            // Dutch oven / smother: light haze only — never black out her ass.
            if (dutchMode && cam != null && _dutchCloud != null)
            {
                _dutchCloud.transform.position = cam.transform.position
                    + cam.transform.forward * ClarityCamDistance(0.42f);
                _dutchCloud.transform.rotation = cam.transform.rotation;
                var dm = _dutchCloud.main;
                dm.startColor = ClarityColor(new Color(gas.r, gas.g, gas.b, 0.18f));
                dm.startSize = new ParticleSystem.MinMaxCurve(0.08f * sizeMul, 0.2f * sizeMul);
                var de = _dutchCloud.emission;
                de.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)ClarityCount(6), (short)ClarityCount(12)) });
                _dutchCloud.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _dutchCloud.Play();
            }

            // Soft face fill for panty sharts — very light so pose stays visible.
            if (shartMode && cam != null && _dutchCloud != null)
            {
                _dutchCloud.transform.position = cam.transform.position
                    + cam.transform.forward * ClarityCamDistance(0.4f);
                var dm = _dutchCloud.main;
                dm.startColor = ClarityColor(new Color(0.5f, 0.38f, 0.15f, 0.14f));
                dm.startSize = new ParticleSystem.MinMaxCurve(0.07f * sizeMul, 0.16f * sizeMul);
                var de = _dutchCloud.emission;
                de.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)ClarityCount(4), (short)ClarityCount(8)) });
                _dutchCloud.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _dutchCloud.Play();
            }

            var clip = NextClip();
            if (_audio != null && clip != null)
            {
                _audio.transform.position = maskMode && cam != null
                    ? cam.transform.position + cam.transform.forward * 0.2f
                    : hipPos;
                _audio.spatialBlend = 0f;
                // Real recordings: keep pitch locked. Tiny drift only for long banks.
                _audio.pitch = shartMode ? 0.96f : 1f;
                float volMul = maskMode || dutchMode ? 1.05f : shartMode ? 0.5f : 1f;
                float vol = volume * UnityEngine.Random.Range(0.92f, 1.05f) * (0.92f + intensity * 0.1f) * volMul;
                _audio.PlayOneShot(clip, vol);

                // Never double-layer the same clip — that made synth banks sound rubbery/fake.
                if (_audioBody != null && shartMode)
                {
                    _audioBody.pitch = 0.95f;
                    _audioBody.PlayOneShot(clip, vol * 0.2f);
                }
            }

            AnimateAnusForFart(shartMode ? false : strong);
        }

        private void PlayMaskTubeGas(Camera cam, Vector3 from, Color gas, float intensity)
        {
            if (_maskTube == null) return;

            Vector3 face = cam.transform.position + cam.transform.forward * ClarityCamDistance(0.28f);
            Vector3 dir = (face - from).normalized;
            if (dir.sqrMagnitude < 0.01f) dir = cam.transform.forward;

            _maskTube.transform.position = from;
            _maskTube.transform.rotation = Quaternion.LookRotation(dir);
            var main = _maskTube.main;
            main.startColor = ClarityColor(new Color(gas.r, gas.g, gas.b, Mathf.Min(gas.a, 0.35f)));
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f + intensity * 0.5f, 2.2f + intensity * 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f * ClaritySizeMul(), 0.07f * ClaritySizeMul());
            var emission = _maskTube.emission;
            float rate = KeepCameraClear ? (12f + intensity * 8f) : (18f + intensity * 14f);
            emission.rateOverTime = rate;
            _maskTube.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _maskTube.Play();

            // Light lens haze only — keep the view of her clear.
            if (_dutchCloud != null)
            {
                _dutchCloud.transform.position = face;
                _dutchCloud.transform.rotation = Quaternion.LookRotation(-cam.transform.forward);
                var dm = _dutchCloud.main;
                dm.startColor = ClarityColor(new Color(gas.r * 0.9f, gas.g, gas.b * 0.85f, 0.16f));
                dm.startSize = new ParticleSystem.MinMaxCurve(0.08f * ClaritySizeMul(), 0.18f * ClaritySizeMul());
                var de = _dutchCloud.emission;
                de.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)ClarityCount(4), (short)ClarityCount(8)) });
                _dutchCloud.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _dutchCloud.Play();
            }
        }

        /// <summary>
        /// Pulse Body blend shape "Butthole" open then closed, with a light butt-cheek twitch.
        /// </summary>
        public void AnimateAnusForFart(bool strong)
        {
            EnsureBodyMorphRefs();
            float peak = strong ? UnityEngine.Random.Range(70f, 100f) : UnityEngine.Random.Range(35f, 75f);
            float hold = strong ? UnityEngine.Random.Range(0.28f, 0.55f) : UnityEngine.Random.Range(0.12f, 0.28f);
            _anusTarget = peak;
            _anusHoldUntil = Time.unscaledTime + hold;
            _cheekPulse = strong ? 1f : 0.55f;
        }

        private void EnsureBodyMorphRefs()
        {
            if (_bodySmr != null && _buttholeShapeIndex >= 0)
            {
                return;
            }

            GameObject bodyGo = GameObject.Find("Body")
                ?? GameObject.Find("GirlCharacter/Body");

            if (bodyGo != null)
            {
                _bodySmr = bodyGo.GetComponent<SkinnedMeshRenderer>();
                if (_bodySmr == null)
                {
                    _bodySmr = bodyGo.GetComponentInChildren<SkinnedMeshRenderer>(true);
                }
            }

            if (_bodySmr == null)
            {
                var renderers = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();
                for (int i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (r == null || r.sharedMesh == null) continue;
                    if (r.sharedMesh.GetBlendShapeIndex("Butthole") >= 0)
                    {
                        _bodySmr = r;
                        break;
                    }
                }
            }

            if (_bodySmr != null && _bodySmr.sharedMesh != null)
            {
                _buttholeShapeIndex = _bodySmr.sharedMesh.GetBlendShapeIndex("Butthole");
                if (_buttholeShapeIndex >= 0)
                {
                    VSFartMod.Logger.LogInfo($"Anus morph ready: '{_bodySmr.name}' blend shape Butthole index {_buttholeShapeIndex}.");
                }
                else
                {
                    VSFartMod.Logger.LogWarning("Body mesh found but has no 'Butthole' blend shape.");
                }
            }

            _buttL = FindBoneExact("Butt_L") ?? FindBone("Butt_L");
            _buttR = FindBoneExact("Butt_R") ?? FindBone("Butt_R");
            if (_buttL != null && _buttR != null && !_buttBasesCached)
            {
                _buttLBaseRot = _buttL.localRotation;
                _buttRBaseRot = _buttR.localRotation;
                _buttBasesCached = true;
            }
        }

        private void UpdateAnusMorph(float dt)
        {
            if (_anusTarget <= 0.01f && _anusCurrent <= 0.01f && _cheekPulse <= 0.01f)
            {
                return;
            }

            EnsureBodyMorphRefs();

            if (Time.unscaledTime >= _anusHoldUntil)
            {
                _anusTarget = 0f;
            }

            float speed = _anusTarget > _anusCurrent ? 280f : 160f;
            _anusCurrent = Mathf.MoveTowards(_anusCurrent, _anusTarget, speed * dt);

            if (_bodySmr != null && _buttholeShapeIndex >= 0)
            {
                // Tiny flutter while open so it looks alive during a long push.
                float flutter = _anusCurrent > 5f
                    ? Mathf.Sin(Time.unscaledTime * 22f) * (_anusCurrent * 0.04f)
                    : 0f;
                _bodySmr.SetBlendShapeWeight(_buttholeShapeIndex, Mathf.Clamp(_anusCurrent + flutter, 0f, 100f));
            }

            if (_cheekPulse > 0f && _buttBasesCached)
            {
                _cheekPulse = Mathf.MoveTowards(_cheekPulse, 0f, dt * 2.2f);
                float ang = Mathf.Sin(Time.unscaledTime * 18f) * 4.5f * _cheekPulse;
                if (_buttL != null)
                {
                    _buttL.localRotation = _buttLBaseRot * Quaternion.Euler(ang, 0f, ang * 0.35f);
                }
                if (_buttR != null)
                {
                    _buttR.localRotation = _buttRBaseRot * Quaternion.Euler(-ang, 0f, -ang * 0.35f);
                }
            }
            else if (_buttBasesCached && _cheekPulse <= 0.01f)
            {
                if (_buttL != null) _buttL.localRotation = _buttLBaseRot;
                if (_buttR != null) _buttR.localRotation = _buttRBaseRot;
            }
        }

        private static Transform FindBoneExact(string exactName)
        {
            var animators = UnityEngine.Object.FindObjectsOfType<Animator>();
            for (int a = 0; a < animators.Length; a++)
            {
                var anim = animators[a];
                if (anim == null) continue;
                var transforms = anim.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    if (transforms[i].name == exactName)
                    {
                        return transforms[i];
                    }
                }
            }

            return null;
        }

        private static Transform FindBone(string namePart)
        {
            var animators = UnityEngine.Object.FindObjectsOfType<Animator>();
            for (int a = 0; a < animators.Length; a++)
            {
                var anim = animators[a];
                if (anim == null || !anim.isActiveAndEnabled)
                {
                    continue;
                }

                var transforms = anim.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    if (transforms[i].name.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return transforms[i];
                    }
                }
            }

            return null;
        }

        private void BuildGasSystem()
        {
            var fx = new GameObject("FartGas");
            fx.transform.SetParent(transform, false);
            _gas = fx.AddComponent<ParticleSystem>();

            var main = _gas.main;
            main.loop = false;
            main.duration = 0.9f;
            main.startLifetime = 0.85f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.45f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.startColor = GasColors[0];
            main.gravityModifier = -0.04f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 36;
            main.playOnAwake = false;

            var emission = _gas.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 6, 12) });

            var shape = _gas.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 22f;
            shape.radius = 0.04f;

            var colorOverLifetime = _gas.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(GasColors[0], 0f),
                    new GradientColorKey(new Color(0.55f, 0.65f, 0.25f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.32f, 0.12f),
                    new GradientAlphaKey(0.12f, 0.65f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = grad;

            var sizeOverLifetime = _gas.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.7f, 1f, 1.35f));

            var renderer = fx.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            var shader = Shader.Find("Particles/Standard Unlit")
                ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended")
                ?? Shader.Find("Sprites/Default");
            renderer.material = new Material(shader);

            _gasTex = Assets.FartGasTexture;
            if (_gasTex != null && renderer.material.HasProperty("_MainTex"))
            {
                renderer.material.mainTexture = _gasTex;
            }
        }

        private void BuildMaskTubeSystem()
        {
            var fx = new GameObject("FartMaskTube");
            fx.transform.SetParent(transform, false);
            _maskTube = fx.AddComponent<ParticleSystem>();

            var main = _maskTube.main;
            main.loop = false;
            main.duration = 0.85f;
            main.startLifetime = 0.55f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.startColor = GasColors[0];
            main.gravityModifier = 0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            main.playOnAwake = false;

            var emission = _maskTube.emission;
            emission.rateOverTime = 22f;

            var shape = _maskTube.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 3.5f;
            shape.radius = 0.012f;

            var trails = _maskTube.trails;
            trails.enabled = true;
            trails.lifetime = 0.12f;
            trails.ratio = 0.3f;

            var renderer = fx.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 2.2f;
            renderer.velocityScale = 0.12f;
            renderer.material = KinkFxShared.MakeParticleMaterial();
            renderer.trailMaterial = KinkFxShared.MakeParticleMaterial();
            if (Assets.FartGasTexture != null && renderer.material.HasProperty("_MainTex"))
            {
                renderer.material.mainTexture = Assets.FartGasTexture;
            }
        }

        private void BuildDutchOvenCloud()
        {
            var fx = new GameObject("DutchOvenCloud");
            fx.transform.SetParent(transform, false);
            _dutchCloud = fx.AddComponent<ParticleSystem>();

            var main = _dutchCloud.main;
            main.loop = false;
            main.duration = 0.9f;
            main.startLifetime = 0.7f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.04f, 0.16f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.28f);
            main.startColor = new Color(0.45f, 0.7f, 0.3f, 0.2f);
            main.gravityModifier = -0.02f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 28;
            main.playOnAwake = false;

            var emission = _dutchCloud.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 6, 12) });

            var shape = _dutchCloud.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.1f;

            var colorOverLifetime = _dutchCloud.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(GasColors[0], 0f),
                    new GradientColorKey(new Color(0.5f, 0.6f, 0.25f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.22f, 0.15f),
                    new GradientAlphaKey(0.1f, 0.7f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = grad;

            var sizeOverLifetime = _dutchCloud.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.8f, 1f, 1.4f));

            var renderer = fx.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = KinkFxShared.MakeParticleMaterial();
            if (Assets.FartGasTexture != null && renderer.material.HasProperty("_MainTex"))
            {
                renderer.material.mainTexture = Assets.FartGasTexture;
            }
        }
    }

    internal static class NativeInput
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public static bool IsKeyDown(int vKey)
        {
            // High bit set means key is currently down.
            return (GetAsyncKeyState(vKey) & 0x8000) != 0;
        }
    }

    internal static class WavLoader
    {
        public static AudioClip LoadClip(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 44)
            {
                return null;
            }

            // Minimal PCM WAV reader (16-bit mono/stereo).
            int channels = BitConverter.ToInt16(bytes, 22);
            int sampleRate = BitConverter.ToInt32(bytes, 24);
            int bits = BitConverter.ToInt16(bytes, 34);
            int dataIndex = 12;
            int dataSize = 0;
            while (dataIndex + 8 < bytes.Length)
            {
                string chunk = System.Text.Encoding.ASCII.GetString(bytes, dataIndex, 4);
                int size = BitConverter.ToInt32(bytes, dataIndex + 4);
                if (chunk == "data")
                {
                    dataSize = size;
                    dataIndex += 8;
                    break;
                }

                dataIndex += 8 + size;
            }

            if (dataSize <= 0 || bits != 16)
            {
                return null;
            }

            int sampleCount = dataSize / 2;
            float[] data = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short s = BitConverter.ToInt16(bytes, dataIndex + i * 2);
                data[i] = s / 32768f;
            }

            int frames = sampleCount / Math.Max(1, channels);
            string name = Path.GetFileNameWithoutExtension(path);
            var clip = AudioClip.Create(name, frames, channels, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }

    internal static class ProceduralFart
    {
        public static AudioClip Create(string name, int seed)
        {
            var rng = new System.Random(seed);
            int sampleRate = 44100;
            float seconds = 0.35f + (float)rng.NextDouble() * 0.85f;
            int samples = Mathf.CeilToInt(sampleRate * seconds);
            float[] data = new float[samples];

            float fBody = 40f + (float)rng.NextDouble() * 70f;
            float fBody2 = fBody * (1.6f + (float)rng.NextDouble() * 0.5f);
            float fRasp = 12f + (float)rng.NextDouble() * 50f;
            float phase1 = 0f, phase2 = 0f, phaseAm = 0f, pink = 0f, lp = 0f;
            float attack = 0.015f + (float)rng.NextDouble() * 0.03f;
            float release = 0.12f + (float)rng.NextDouble() * 0.35f;

            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)sampleRate;
                float tn = i / (float)samples;
                float env;
                if (t < attack) env = t / attack;
                else if (tn > 0.55f) env = Mathf.Pow(1f - ((tn - 0.55f) / 0.45f), 1.4f);
                else env = 1f;

                float w = (float)rng.NextDouble() * 2f - 1f;
                pink = pink * 0.97f + w * 0.03f;
                float noise = w * 0.35f + pink * 0.65f;

                phase1 += 2f * Mathf.PI * fBody / sampleRate;
                phase2 += 2f * Mathf.PI * fBody2 / sampleRate;
                phaseAm += 2f * Mathf.PI * fRasp / sampleRate;
                float body = Mathf.Sin(phase1) * 0.5f + Mathf.Sin(phase2) * 0.2f;
                float am = 0.55f + 0.45f * Mathf.Sin(phaseAm);
                lp = lp + 0.12f * (noise * am - lp);
                float mix = body * 0.35f + lp * 0.55f + noise * 0.1f;
                if (t < 0.01f) mix += w * (1f - t / 0.01f) * 0.2f;
                // Soft saturate
                mix = (float)Math.Tanh(mix * 1.3f);
                data[i] = mix * env * 0.75f;
            }

            // Normalize
            float peak = 1e-5f;
            for (int i = 0; i < samples; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            float g = 0.85f / peak;
            for (int i = 0; i < samples; i++) data[i] *= g;

            var clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
