using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VSFartMod
{
    /// <summary>
    /// Stinky sock / disgusting armpit torture: light musk haze from feet or pits toward the camera.
    /// Task-text gated only. Kept translucent so FaceStomp / PitSmother stay visible.
    /// </summary>
    public class SmellTortureEffects : MonoBehaviour
    {
        public static SmellTortureEffects Instance { get; private set; }
        public static bool StinkySocksEnabled { get; set; }
        public static bool StinkyArmpitsEnabled { get; set; }

        public float scanInterval = 0.7f;
        public float minBurstGap = 7f;
        public float maxBurstGap = 16f;
        public float volume = 0.45f;

        private enum SmellMode { None, Socks, Armpits }

        private readonly List<AudioClip> _clips = new List<AudioClip>();
        private ParticleSystem _mist;
        private AudioSource _audio;
        private float _nextScan;
        private float _nextBurst;
        private float _loggedContextAt = -999f;
        private bool _f4WasDown;

        private static readonly string[] SockNeedles =
        {
            "stinky sock", "stinky socks", "smelly sock", "smelly socks",
            "dirty sock", "dirty socks", "socked foot", "socked feet",
            "socks onto your face", "socks on your face", "socked sole",
            "foot-smother", "foot smothers", "her dirty socked",
            "sniff deep while her socked", "stinky socked feet"
        };

        private static readonly string[] ArmpitNeedles =
        {
            "stinky armpit", "stinky armpits", "smelly armpit", "smelly armpits",
            "disgusting armpit", "disgusting armpits", "her armpit on your",
            "armpit smother", "sniff her armpit", "sniff her pit",
            "bury your nose in her armpit", "pit smother", "armpit torture",
            "inhale her armpit", "her pit stink"
        };

        // Exposed so FartEffects can refuse to fire over smell-torture instructions.
        public static string[] SockNeedlesPublic => SockNeedles;
        public static string[] ArmpitNeedlesPublic => ArmpitNeedles;

        public static void EnsureCreated(GameObject host)
        {
            if (Instance != null || host == null) return;
            host.AddComponent<SmellTortureEffects>();
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
            BuildMist();
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.loop = false;
            _audio.spatialBlend = 0f;
            _audio.ignoreListenerPause = true;
            _audio.bypassListenerEffects = true;
            _audio.volume = volume;
            _nextBurst = Time.unscaledTime + 15f;
            VSFartMod.Logger.LogInfo($"SmellTortureEffects ready ({_clips.Count} clips). Debug: F4. Socks/Armpit task text only.");
        }

        private void Update()
        {
            if (WasF4Pressed())
            {
                bool pits = StinkyArmpitsEnabled && !StinkySocksEnabled;
                ForceBurst("debug F4", armpits: pits);
            }

            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + scanInterval;

            SmellMode active = DetectMode();
            if (active == SmellMode.None) return;

            if (Time.unscaledTime - _loggedContextAt > 8f)
            {
                _loggedContextAt = Time.unscaledTime;
                VSFartMod.Logger.LogInfo($"Smell torture active ({active}).");
            }

            if (Time.unscaledTime >= _nextBurst)
            {
                PlayBurst(active);
                _nextBurst = Time.unscaledTime + UnityEngine.Random.Range(minBurstGap, maxBurstGap);
            }
        }

        public void ForceBurst(string reason = "manual", bool armpits = false)
        {
            PlayBurst(armpits ? SmellMode.Armpits : SmellMode.Socks, strong: true);
            VSFartMod.Logger.LogInfo($"Smell ForceBurst ({reason}).");
            _nextBurst = Time.unscaledTime + UnityEngine.Random.Range(minBurstGap, maxBurstGap);
        }

        private bool WasF4Pressed()
        {
            bool f4 = NativeInput.IsKeyDown(0x73); // VK_F4
            bool edge = f4 && !_f4WasDown;
            _f4WasDown = f4;
            try
            {
                if (Input.GetKeyDown(KeyCode.F4)) return true;
            }
            catch { /* ignored */ }
            return edge;
        }

        private SmellMode DetectMode()
        {
            if (!KinkFxShared.IsSessionActive()) return SmellMode.None;

            // Task pool is gated by Content Select kinks; FX follows instruction text.
            string sockHit = KinkFxShared.FindActiveInstructionContaining(SockNeedles);
            if (!string.IsNullOrEmpty(sockHit)) return SmellMode.Socks;

            string pitHit = KinkFxShared.FindActiveInstructionContaining(ArmpitNeedles);
            if (!string.IsNullOrEmpty(pitHit)) return SmellMode.Armpits;

            return SmellMode.None;
        }

        private void PlayBurst(SmellMode mode, bool strong = false)
        {
            var cam = Camera.main;
            Vector3 origin = ResolveOrigin(mode, cam);
            Vector3 aim = cam != null
                ? (cam.transform.position + cam.transform.forward * KinkFxShared.ClarityCamDistance(0.35f) - origin).normalized
                : Vector3.forward;
            if (aim.sqrMagnitude < 0.01f) aim = Vector3.forward;

            float intensity = strong ? 1f : UnityEngine.Random.Range(0.45f, 0.85f);
            Color musk = mode == SmellMode.Socks
                ? KinkFxShared.ClarityColor(new Color(0.55f, 0.48f, 0.28f, 0.22f + intensity * 0.12f))
                : KinkFxShared.ClarityColor(new Color(0.42f, 0.5f, 0.28f, 0.2f + intensity * 0.12f));
            float sizeMul = KinkFxShared.ClaritySizeMul();

            if (_mist != null)
            {
                _mist.transform.position = origin;
                _mist.transform.rotation = Quaternion.LookRotation(aim);
                var main = _mist.main;
                main.startColor = musk;
                main.startSize = new ParticleSystem.MinMaxCurve(
                    (0.08f + intensity * 0.06f) * sizeMul,
                    (0.2f + intensity * 0.12f) * sizeMul);
                var emission = _mist.emission;
                short lo = (short)KinkFxShared.ClarityCount(5 + (int)(intensity * 5));
                short hi = (short)KinkFxShared.ClarityCount(9 + (int)(intensity * 8));
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, lo, hi) });
                _mist.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _mist.Play();
            }

            if (_clips.Count > 0 && _audio != null)
            {
                var clip = _clips[UnityEngine.Random.Range(0, _clips.Count)];
                if (clip != null)
                {
                    if (AudioListener.pause) AudioListener.pause = false;
                    _audio.pitch = UnityEngine.Random.Range(0.9f, 1.08f);
                    _audio.PlayOneShot(clip, volume * (0.75f + intensity * 0.2f));
                }
            }
        }

        private static Vector3 ResolveOrigin(SmellMode mode, Camera cam)
        {
            if (mode == SmellMode.Socks)
            {
                var foot = KinkFxShared.FindBone("Foot")
                           ?? KinkFxShared.FindBone("Toe")
                           ?? KinkFxShared.FindBone("Ankle")
                           ?? KinkFxShared.FindBoneExact("Foot_R")
                           ?? KinkFxShared.FindBoneExact("Foot_L");
                if (foot != null)
                {
                    return foot.position + foot.TransformDirection(new Vector3(0f, 0.02f, 0.04f));
                }
            }
            else
            {
                var pit = KinkFxShared.FindBone("UpperArm")
                          ?? KinkFxShared.FindBone("Shoulder")
                          ?? KinkFxShared.FindBone("Clavicle")
                          ?? KinkFxShared.FindBone("Arm");
                if (pit != null)
                {
                    return pit.position + pit.TransformDirection(new Vector3(0f, -0.04f, 0.02f));
                }
            }

            if (cam != null)
            {
                return cam.transform.position + cam.transform.forward * KinkFxShared.ClarityCamDistance(0.4f)
                       + (mode == SmellMode.Socks ? cam.transform.up * -0.15f : cam.transform.up * 0.08f);
            }

            return Vector3.zero;
        }

        private void BuildMist()
        {
            var go = new GameObject("SmellMist");
            go.transform.SetParent(transform, false);
            _mist = go.AddComponent<ParticleSystem>();

            var main = _mist.main;
            main.loop = false;
            main.duration = 0.85f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.55f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.25f);
            main.startColor = new Color(0.5f, 0.45f, 0.28f, 0.25f);
            main.gravityModifier = -0.03f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 32;
            main.playOnAwake = false;

            var emission = _mist.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 6, 12) });

            var shape = _mist.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 18f;
            shape.radius = 0.03f;

            var col = _mist.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.55f, 0.5f, 0.3f), 0f),
                    new GradientColorKey(new Color(0.4f, 0.42f, 0.25f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.28f, 0.2f),
                    new GradientAlphaKey(0.1f, 0.7f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = g;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.material = KinkFxShared.MakeParticleMaterial();
        }

        private void LoadClips()
        {
            _clips.Clear();
            string[] folders =
            {
                Path.Combine(Application.streamingAssetsPath, "VSFartSounds", "Smell"),
                Path.Combine(Application.streamingAssetsPath, "Smell"),
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
                        VSFartMod.Logger.LogWarning($"Smell WAV fail {file}: {ex.Message}");
                    }
                }
            }

            if (_clips.Count == 0)
            {
                for (int i = 0; i < 3; i++)
                {
                    _clips.Add(ProceduralSmellHiss.Create($"ProcSmell_{i}", 5100 + i * 41));
                }
            }
        }
    }

    internal static class ProceduralSmellHiss
    {
        public static AudioClip Create(string name, int seed)
        {
            var rng = new System.Random(seed);
            int sampleRate = 22050;
            float seconds = 0.45f;
            int samples = Mathf.CeilToInt(sampleRate * seconds);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)sampleRate;
                float n = ((float)rng.NextDouble() * 2f - 1f);
                float sniff = n * Mathf.Exp(-t * 5f) * 0.3f;
                float soft = Mathf.Sin(t * 18f) * Mathf.Exp(-t * 4f) * 0.12f;
                float edge = t < 0.02f ? t / 0.02f : (seconds - t < 0.08f ? (seconds - t) / 0.08f : 1f);
                data[i] = (sniff + soft) * edge;
            }
            var clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
