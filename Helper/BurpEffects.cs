using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VSFartMod
{
    /// <summary>
    /// Face-close burps: mist from mouth + optional mouth morph while FaceSit / BoobSmother is up.
    /// </summary>
    public class BurpEffects : MonoBehaviour
    {
        public static BurpEffects Instance { get; private set; }
        public static bool BurpingKinkEnabled { get; set; }

        public float scanInterval = 0.4f;
        public float minBurstGap = 2.2f;
        public float maxBurstGap = 5.5f;
        public float volume = 0.75f;

        private readonly List<AudioClip> _clips = new List<AudioClip>();
        private readonly Queue<int> _recent = new Queue<int>();
        private readonly List<int> _bag = new List<int>();
        private int _recentLimit = 8;
        private ParticleSystem _breath;
        private AudioSource _audio;
        private float _nextScan;
        private float _nextBurst;
        private float _loggedContextAt = -999f;

        private SkinnedMeshRenderer _faceSmr;
        private int _mouthShape = -1;
        private int _mouthShapeO = -1;
        private Transform _jaw;
        private Quaternion _jawBaseLocal;
        private bool _jawBaseCached;
        private float _mouthTarget;
        private float _mouthCurrent;
        private float _mouthHoldUntil;
        private bool _loggedMouthFail;

        private bool _f7WasDown;

        private static readonly string[] ExactHooks =
        {
            "NoIK_FaceSit",
            "VSFart_FaceSit",
            "NoIK_BoobSmother",
            "Special_NoIK_FaceSit",
            "Special_VSFart_FaceSit",
            "Special_NoIK_BoobSmother",
        };

        private static readonly string[] PoseContains =
        {
            "FaceSit",
            "VSFart_FaceSit",
            "BoobSmother",
            "HandShoulders",
        };

        private static readonly string[] PoseExcludes = { "FaceStomp", "ButtFaceSit" };

        private static readonly string[] TextNeedles = { "burp", "belch" };

        public static void EnsureCreated(GameObject host)
        {
            if (Instance != null || host == null) return;
            host.AddComponent<BurpEffects>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            LoadClips();
            BuildBreathSystem();
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _audio.ignoreListenerPause = true;
            _audio.bypassListenerEffects = true;
            _audio.volume = 1f;
            _nextBurst = Time.unscaledTime + 8f;
            VSFartMod.Logger.LogInfo($"BurpEffects ready with {_clips.Count} clips. Debug: F7. Auto only on burp task text.");
        }

        private void LateUpdate()
        {
            UpdateMouthMorph(Time.unscaledDeltaTime);
        }

        private void Update()
        {
            if (WasF7Pressed())
            {
                ForceBurst("debug F7");
            }

            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + scanInterval;

            if (!IsBurpContextActive()) return;

            if (Time.unscaledTime - _loggedContextAt > 8f)
            {
                _loggedContextAt = Time.unscaledTime;
                VSFartMod.Logger.LogInfo("Burp context active — face-close burps.");
            }

            if (Time.unscaledTime >= _nextBurst)
            {
                PlayBurst();
                _nextBurst = Time.unscaledTime + UnityEngine.Random.Range(minBurstGap, maxBurstGap);
            }
        }

        private bool WasF7Pressed()
        {
            bool f7 = NativeInput.IsKeyDown(0x76); // VK_F7
            bool edge = f7 && !_f7WasDown;
            _f7WasDown = f7;
            try
            {
                if (Input.GetKeyDown(KeyCode.F7)) return true;
            }
            catch { /* ignored */ }
            return edge;
        }

        public void ForceBurst(string reason = "manual")
        {
            PlayBurst(strong: true);
            VSFartMod.Logger.LogInfo($"Burp ForceBurst ({reason}).");
            _nextBurst = Time.unscaledTime + UnityEngine.Random.Range(minBurstGap, maxBurstGap);
        }

        private bool IsBurpContextActive()
        {
            if (!KinkFxShared.IsSessionActive()) return false;
            // Task text only — FaceSit + Burping kink alone spammed on session start.
            return KinkFxShared.AnyActiveUiTextContains(TextNeedles);
        }

        private void PlayBurst(bool strong = false)
        {
            var cam = Camera.main;
            var mouth = KinkFxShared.FindBoneExact("Jaw")
                ?? KinkFxShared.FindBone("Jaw")
                ?? KinkFxShared.FindBone("Mouth")
                ?? KinkFxShared.FindBone("Head")
                ?? KinkFxShared.FindBone("Face");

            Vector3 pos;
            if (mouth != null)
            {
                pos = mouth.position + mouth.TransformDirection(new Vector3(0f, -0.02f, 0.06f));
            }
            else if (cam != null)
            {
                // Face-in-camera: emit just in front of the lens (her face fills the view on FaceSit).
                pos = cam.transform.position + cam.transform.forward * KinkFxShared.ClarityCamDistance(0.35f)
                      + cam.transform.up * 0.02f;
            }
            else
            {
                pos = transform.position;
            }

            float intensity = strong ? 0.85f : UnityEngine.Random.Range(0.4f, 0.75f);
            float sizeMul = KinkFxShared.ClaritySizeMul();

            if (_breath != null)
            {
                var main = _breath.main;
                main.startSize = new ParticleSystem.MinMaxCurve(
                    (0.04f + intensity * 0.04f) * sizeMul,
                    (0.1f + intensity * 0.1f) * sizeMul);
                main.startColor = KinkFxShared.ClarityColor(new Color(0.85f, 0.9f, 0.75f, 0.18f + intensity * 0.12f));
                _breath.transform.position = pos;
                if (cam != null)
                {
                    Vector3 towardCam = (cam.transform.position - pos).normalized;
                    if (towardCam.sqrMagnitude < 0.01f) towardCam = cam.transform.forward;
                    _breath.transform.rotation = Quaternion.LookRotation(towardCam);
                }

                var emission = _breath.emission;
                int lo = KinkFxShared.ClarityCount(4 + (int)(intensity * 4f));
                int hi = KinkFxShared.ClarityCount(7 + (int)(intensity * 7f));
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)lo, (short)hi) });
                _breath.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _breath.Play();
            }

            if (_clips.Count > 0 && _audio != null)
            {
                var clip = NextClip();
                if (clip != null)
                {
                    if (AudioListener.pause) AudioListener.pause = false;
                    // Keep recorded burps at natural pitch — random pitch made them sound fake.
                    _audio.pitch = 1f;
                    _audio.PlayOneShot(clip, volume * (0.85f + intensity * 0.2f));
                }
            }

            // Always open her mouth for the burp — was gated on intensity before.
            AnimateMouth(strong: true, holdSeconds: strong ? 0.65f : 0.45f);
        }

        private void AnimateMouth(bool strong, float holdSeconds = 0.5f)
        {
            EnsureMouthRefs();
            _mouthTarget = strong ? UnityEngine.Random.Range(88f, 100f) : UnityEngine.Random.Range(60f, 85f);
            _mouthHoldUntil = Time.unscaledTime + holdSeconds;
        }

        private void EnsureMouthRefs()
        {
            if (_faceSmr != null && _mouthShape >= 0)
            {
                // Guard against stale wrong index (old bug matched Angry via bare "A").
                if (_faceSmr.sharedMesh != null)
                {
                    string n = _faceSmr.sharedMesh.GetBlendShapeName(_mouthShape);
                    if (n.IndexOf("MTH_A", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("MTH_O", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("MTH_Surprised", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("MouthOpen", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return;
                    }

                    _mouthShape = -1;
                }
                else return;
            }
            _faceSmr = KinkFxShared.FindFaceRenderer();

            // VRoid/VRM face in VS: Fcl_MTH_A = open "ah" mouth (burp). Do NOT search bare "A"
            // — that matched Face.*_ALL_Angry at index 0 and did nothing useful.
            _mouthShape = KinkFxShared.FindBlendShapePrefer(
                _faceSmr,
                "Fcl_MTH_A", "MTH_A", "Fcl_MTH_O", "MTH_O",
                "Fcl_MTH_Surprised", "MTH_Surprised", "Fcl_MTH_Down", "MTH_Down",
                "MouthOpen", "Mouth_Open", "JawOpen");
            _mouthShapeO = KinkFxShared.FindBlendShapePrefer(
                _faceSmr, "Fcl_MTH_O", "MTH_O");

            _jaw = KinkFxShared.FindBoneExact("Jaw")
                   ?? KinkFxShared.FindBoneExact("Jaw_M")
                   ?? KinkFxShared.FindBone("Jaw");
            if (_jaw != null && !_jawBaseCached)
            {
                _jawBaseLocal = _jaw.localRotation;
                _jawBaseCached = true;
            }

            if (_mouthShape >= 0 && _faceSmr != null)
            {
                string shapeName = _faceSmr.sharedMesh != null
                    ? _faceSmr.sharedMesh.GetBlendShapeName(_mouthShape)
                    : "?";
                VSFartMod.Logger.LogInfo($"Burp mouth morph: '{_faceSmr.name}' [{_mouthShape}] '{shapeName}' jaw={(_jaw != null ? _jaw.name : "none")}.");
            }
            else if (!_loggedMouthFail)
            {
                _loggedMouthFail = true;
                VSFartMod.Logger.LogWarning("Burp mouth morph: no Fcl_MTH_A / mouth blend shape found on Face.");
            }
        }

        private void UpdateMouthMorph(float dt)
        {
            if (_mouthTarget <= 0.01f && _mouthCurrent <= 0.01f)
            {
                RestoreJaw();
                return;
            }

            EnsureMouthRefs();
            if (Time.unscaledTime >= _mouthHoldUntil) _mouthTarget = 0f;

            // Snap open quickly, ease closed a bit slower so the burp reads.
            float speed = _mouthTarget > _mouthCurrent ? 480f : 220f;
            _mouthCurrent = Mathf.MoveTowards(_mouthCurrent, _mouthTarget, speed * dt);
            float w = Mathf.Clamp(_mouthCurrent, 0f, 100f);

            if (_faceSmr != null)
            {
                if (_mouthShape >= 0)
                {
                    _faceSmr.SetBlendShapeWeight(_mouthShape, w);
                }
                // Slight O-shape on top for a rounder burp mouth.
                if (_mouthShapeO >= 0 && _mouthShapeO != _mouthShape)
                {
                    _faceSmr.SetBlendShapeWeight(_mouthShapeO, w * 0.35f);
                }
            }

            if (_jaw != null && _jawBaseCached)
            {
                // Drop the jaw while open (local X on typical VS/VRM rigs).
                float openDeg = (w / 100f) * 18f;
                _jaw.localRotation = _jawBaseLocal * Quaternion.Euler(openDeg, 0f, 0f);
            }
        }

        private void RestoreJaw()
        {
            if (_jaw != null && _jawBaseCached)
            {
                _jaw.localRotation = _jawBaseLocal;
            }
        }

        private void LoadClips()
        {
            _clips.Clear();
            string[] folders =
            {
                Path.Combine(Application.streamingAssetsPath, "VSFartSounds", "Burps"),
                Path.Combine(Application.streamingAssetsPath, "Burps"),
            };

            foreach (string folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (string file in Directory.GetFiles(folder, "*.wav"))
                {
                    try
                    {
                        var clip = WavLoader.LoadClip(file);
                        if (clip != null) _clips.Add(clip);
                    }
                    catch (Exception ex)
                    {
                        VSFartMod.Logger.LogWarning($"Burp WAV fail {file}: {ex.Message}");
                    }
                }
            }

            if (_clips.Count == 0)
            {
                VSFartMod.Logger.LogWarning("No burp WAVs — generating procedural burps. Drop Burp_*.wav into VSFartSounds/Burps/.");
                for (int i = 0; i < 10; i++)
                {
                    _clips.Add(ProceduralBurp.Create($"ProcBurp_{i}", 4000 + i * 47));
                }
            }
            else
            {
                VSFartMod.Logger.LogInfo($"Loaded {_clips.Count} burp clips from VSFartSounds/Burps (real recordings preferred).");
            }
        }

        private AudioClip NextClip()
        {
            if (_clips.Count == 0) return null;
            if (_bag.Count == 0)
            {
                for (int i = 0; i < _clips.Count; i++) _bag.Add(i);
                for (int i = _bag.Count - 1; i > 0; i--)
                {
                    int j = UnityEngine.Random.Range(0, i + 1);
                    int tmp = _bag[i];
                    _bag[i] = _bag[j];
                    _bag[j] = tmp;
                }
            }

            while (_bag.Count > 0)
            {
                int idx = _bag[_bag.Count - 1];
                _bag.RemoveAt(_bag.Count - 1);
                if (_recent.Contains(idx) && _bag.Count > 0) continue;
                _recent.Enqueue(idx);
                while (_recent.Count > _recentLimit) _recent.Dequeue();
                return _clips[idx];
            }

            return _clips[UnityEngine.Random.Range(0, _clips.Count)];
        }

        private void BuildBreathSystem()
        {
            var fx = new GameObject("BurpBreath");
            fx.transform.SetParent(transform, false);
            _breath = fx.AddComponent<ParticleSystem>();

            var main = _breath.main;
            main.loop = false;
            main.duration = 0.7f;
            main.startLifetime = 0.65f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.startColor = new Color(0.9f, 0.95f, 0.8f, 0.22f);
            main.gravityModifier = -0.02f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 28;
            main.playOnAwake = false;

            var emission = _breath.emission;
            emission.rateOverTime = 0f;

            var shape = _breath.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = 0.015f;

            var colorOverLifetime = _breath.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.95f, 1f, 0.85f), 0f),
                    new GradientColorKey(new Color(0.7f, 0.8f, 0.55f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.28f, 0.1f),
                    new GradientAlphaKey(0.1f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = grad;

            var sizeOverLifetime = _breath.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.75f, 1f, 1.3f));

            var renderer = fx.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = KinkFxShared.MakeParticleMaterial();
        }
    }

    internal static class ProceduralBurp
    {
        public static AudioClip Create(string name, int seed)
        {
            var rng = new System.Random(seed);
            int sampleRate = 22050;
            float seconds = 0.28f + (float)rng.NextDouble() * 0.55f;
            int samples = Mathf.CeilToInt(sampleRate * seconds);
            float[] data = new float[samples];
            float phase = 0f;
            float f0 = 90f + (float)rng.NextDouble() * 70f;
            float f1 = 40f + (float)rng.NextDouble() * 40f;

            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)samples;
                // Sharp attack, gurgly mid, soft release — more burp than fart.
                float env = t < 0.12f
                    ? t / 0.12f
                    : Mathf.Pow(1f - ((t - 0.12f) / 0.88f), 1.4f);
                float freq = Mathf.Lerp(f0, f1, t);
                phase += 2f * Mathf.PI * freq / sampleRate;
                float tone = Mathf.Sin(phase) * 0.55f;
                float gurgle = Mathf.Sin(phase * 2.7f + t * 8f) * 0.2f;
                float noise = ((float)rng.NextDouble() * 2f - 1f) * 0.18f;
                data[i] = (tone + gurgle + noise) * env * 0.7f;
            }

            var clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
