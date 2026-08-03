using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VSFartMod
{
    /// <summary>
    /// Domme watersports: yellow pee stream/splash aimed at the camera during FaceSit / ButtFaceSit
    /// when Her Watersports task text is active (separate from vanilla self-piss Watersports).
    /// </summary>
    public class PissEffects : MonoBehaviour
    {
        public static PissEffects Instance { get; private set; }

        /// <summary>Vanilla Watersports kink — your/self piss content.</summary>
        public static bool WatersportsKinkEnabled { get; set; }

        /// <summary>Mod kink — she pees on you (FaceSit FX + her-on-you timed tasks).</summary>
        public static bool HerWatersportsEnabled { get; set; }

        public float scanInterval = 0.75f;
        public float minStreamGap = 14f;
        public float maxStreamGap = 28f;
        public float streamDuration = 1.6f;
        public float volume = 0.55f;

        private readonly List<AudioClip> _clips = new List<AudioClip>();
        private ParticleSystem _stream;
        private ParticleSystem _splash;
        private AudioSource _audio;
        private float _nextScan;
        private float _nextStream;
        private float _streamUntil;
        private float _loggedContextAt = -999f;
        private bool _f6WasDown;
        private bool _streaming;

        private static readonly string[] ExactHooks =
        {
            "NoIK_FaceSit",
            "NoIK_ButtFaceSit",
            "VSFart_FaceSit",
            "VSFart_ButtFaceSit",
            "Special_NoIK_FaceSit",
            "Special_NoIK_ButtFaceSit",
            "Special_VSFart_FaceSit",
            "Special_VSFart_ButtFaceSit",
        };

        private static readonly string[] PoseContains = { "FaceSit", "ButtFaceSit", "VSFart_FaceSit", "VSFart_ButtFaceSit" };
        private static readonly string[] PoseExcludes = { "FaceStomp" };

        // Must be her-on-you. Avoid "piss on yourself" / bare watersports UI text.
        private static readonly string[] TaskNeedles =
        {
            "pees on your", "pees on you.", "pees on you,", "pees on you ",
            "peeing on your", "peeing on you",
            "pisses on your", "pisses on you",
            "pissing on your", "pissing on you",
            "pee on your face", "piss on your face",
            "golden shower on", "while she pees on you", "while she pisses on you",
            "for peeing on you", "keep pissing on your"
        };

        private static readonly string[] ExcludeNeedles =
        {
            "yourself", "your own", "on yourself"
        };

        public static void EnsureCreated(GameObject host)
        {
            if (Instance != null || host == null) return;
            host.AddComponent<PissEffects>();
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
            BuildPeeSystems();
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.loop = false; // never loop hiss — that made pee feel constant
            _audio.spatialBlend = 0f;
            _audio.ignoreListenerPause = true;
            _audio.bypassListenerEffects = true;
            _audio.volume = volume;
            // Long startup delay — never spam on boot / session load.
            _nextStream = Time.unscaledTime + 20f;
            VSFartMod.Logger.LogInfo($"PissEffects ready with {_clips.Count} clips. Debug: F6. Auto only on her-on-you pee task text.");
        }

        private void Update()
        {
            if (WasF6Pressed())
            {
                ForceStream("debug F6");
            }

            if (_streaming)
            {
                // Hard stop when task text is gone or duration ends.
                if (!IsPissContextActive() || Time.unscaledTime >= _streamUntil)
                {
                    StopStream();
                    // Cooldown so one lingering UI line can't immediately re-trigger.
                    _nextStream = Time.unscaledTime + UnityEngine.Random.Range(minStreamGap, maxStreamGap);
                    return;
                }

                UpdateStreamAim();
                return;
            }

            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + scanInterval;

            if (!IsPissContextActive()) return;

            if (Time.unscaledTime - _loggedContextAt > 8f)
            {
                _loggedContextAt = Time.unscaledTime;
                VSFartMod.Logger.LogInfo("Her-on-you pee task text active — one short stream.");
            }

            if (Time.unscaledTime >= _nextStream)
            {
                StartStream(streamDuration);
                _nextStream = Time.unscaledTime + UnityEngine.Random.Range(minStreamGap, maxStreamGap);
            }
        }

        private bool WasF6Pressed()
        {
            bool f6 = NativeInput.IsKeyDown(0x75); // VK_F6
            bool edge = f6 && !_f6WasDown;
            _f6WasDown = f6;
            try
            {
                if (Input.GetKeyDown(KeyCode.F6)) return true;
            }
            catch { /* ignored */ }
            return edge;
        }

        public void ForceStream(string reason = "manual")
        {
            StartStream(streamDuration * 1.4f);
            VSFartMod.Logger.LogInfo($"Piss ForceStream ({reason}).");
            _nextStream = Time.unscaledTime + UnityEngine.Random.Range(minStreamGap, maxStreamGap);
        }

        private bool IsPissContextActive()
        {
            if (!KinkFxShared.IsSessionActive()) return false;

            string hit = KinkFxShared.FindActiveInstructionContaining(TaskNeedles);
            if (string.IsNullOrEmpty(hit)) return false;
            if (KinkFxShared.ContainsAny(hit, ExcludeNeedles)) return false;
            return true;
        }

        private void StartStream(float duration)
        {
            _streaming = true;
            _streamUntil = Time.unscaledTime + duration;
            UpdateStreamAim();

            if (_stream != null)
            {
                _stream.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = _stream.main;
                main.startColor = KinkFxShared.ClarityLiquid(new Color(0.92f, 0.82f, 0.22f, 0.85f));
                float sizeMul = KinkFxShared.ClaritySizeMul();
                main.startSize = new ParticleSystem.MinMaxCurve(0.016f * sizeMul, 0.04f * sizeMul);
                _stream.Play();
            }

            if (_splash != null)
            {
                _splash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var sm = _splash.main;
                sm.startColor = KinkFxShared.ClarityLiquid(new Color(0.9f, 0.8f, 0.2f, 0.7f));
                float sizeMul = KinkFxShared.ClaritySizeMul();
                sm.startSize = new ParticleSystem.MinMaxCurve(0.018f * sizeMul, 0.065f * sizeMul);
                var se = _splash.emission;
                se.SetBursts(new[]
                {
                    new ParticleSystem.Burst(0.05f,
                        (short)KinkFxShared.ClarityCount(18),
                        (short)KinkFxShared.ClarityCount(36))
                });
                _splash.Play();
            }

            if (_clips.Count > 0 && _audio != null)
            {
                var clip = _clips[UnityEngine.Random.Range(0, _clips.Count)];
                if (clip != null)
                {
                    if (AudioListener.pause) AudioListener.pause = false;
                    _audio.Stop();
                    _audio.loop = false;
                    _audio.clip = clip;
                    _audio.pitch = UnityEngine.Random.Range(0.92f, 1.08f);
                    _audio.volume = volume;
                    _audio.Play();
                }
            }
        }

        private void StopStream()
        {
            _streaming = false;
            if (_stream != null) _stream.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (_splash != null) _splash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (_audio != null && _audio.isPlaying) _audio.Stop();
        }

        private void UpdateStreamAim()
        {
            var cam = Camera.main;
            var hips = KinkFxShared.FindBoneExact("Hips")
                ?? KinkFxShared.FindBone("Hips")
                ?? KinkFxShared.FindBone("Pelvis")
                ?? KinkFxShared.FindBone("hip");

            Vector3 origin;
            if (hips != null)
            {
                // Slightly forward/down from hips toward the camera (you under her).
                origin = hips.position + hips.TransformDirection(new Vector3(0f, -0.08f, 0.05f));
            }
            else if (cam != null)
            {
                origin = cam.transform.position + cam.transform.forward * 0.45f + cam.transform.up * 0.15f;
            }
            else
            {
                origin = transform.position;
            }

            Vector3 aim;
            if (cam != null)
            {
                aim = (cam.transform.position + cam.transform.forward * KinkFxShared.ClarityCamDistance(0.35f) - origin).normalized;
            }
            else
            {
                aim = Vector3.down;
            }

            if (_stream != null)
            {
                _stream.transform.position = origin;
                _stream.transform.rotation = Quaternion.LookRotation(aim);
            }

            if (_splash != null && cam != null)
            {
                // Splash near the camera / "on your face".
                _splash.transform.position = cam.transform.position
                    + cam.transform.forward * KinkFxShared.ClarityCamDistance(0.4f);
                _splash.transform.rotation = Quaternion.LookRotation(-cam.transform.forward);
            }
        }

        private void LoadClips()
        {
            _clips.Clear();
            string[] folders =
            {
                Path.Combine(Application.streamingAssetsPath, "VSFartSounds", "Piss"),
                Path.Combine(Application.streamingAssetsPath, "Piss"),
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
                        VSFartMod.Logger.LogWarning($"Piss WAV fail {file}: {ex.Message}");
                    }
                }
            }

            if (_clips.Count == 0)
            {
                VSFartMod.Logger.LogWarning("No piss WAVs — generating procedural stream loops. Drop Piss_*.wav into VSFartSounds/Piss/.");
                for (int i = 0; i < 4; i++)
                {
                    _clips.Add(ProceduralPiss.Create($"ProcPiss_{i}", 8000 + i * 91));
                }
            }
        }

        private void BuildPeeSystems()
        {
            var streamGo = new GameObject("PissStream");
            streamGo.transform.SetParent(transform, false);
            _stream = streamGo.AddComponent<ParticleSystem>();

            var main = _stream.main;
            main.loop = false;
            main.duration = 1.6f;
            main.startLifetime = 0.55f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 3.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.045f);
            main.startColor = new Color(0.92f, 0.82f, 0.22f, 0.85f);
            main.gravityModifier = 0.85f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 220;
            main.playOnAwake = false;

            var emission = _stream.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, 40, 70),
                new ParticleSystem.Burst(0.35f, 30, 55),
                new ParticleSystem.Burst(0.75f, 20, 40),
            });

            var shape = _stream.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 3.5f;
            shape.radius = 0.008f;

            var trail = _stream.trails;
            trail.enabled = true;
            trail.lifetime = 0.12f;
            trail.ratio = 0.35f;
            trail.sizeAffectsLifetime = true;

            var colorOverLifetime = _stream.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.95f, 0.88f, 0.25f), 0f),
                    new GradientColorKey(new Color(0.75f, 0.65f, 0.15f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.9f, 0f),
                    new GradientAlphaKey(0.7f, 0.7f),
                    new GradientAlphaKey(0.15f, 1f)
                });
            colorOverLifetime.color = grad;

            var streamRenderer = streamGo.GetComponent<ParticleSystemRenderer>();
            streamRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            streamRenderer.lengthScale = 2.4f;
            streamRenderer.velocityScale = 0.12f;
            streamRenderer.material = KinkFxShared.MakeParticleMaterial();
            streamRenderer.trailMaterial = KinkFxShared.MakeParticleMaterial();

            var splashGo = new GameObject("PissSplash");
            splashGo.transform.SetParent(transform, false);
            _splash = splashGo.AddComponent<ParticleSystem>();

            var sm = _splash.main;
            sm.loop = false;
            sm.duration = 1.4f;
            sm.startLifetime = 0.4f;
            sm.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.1f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
            sm.startColor = new Color(0.9f, 0.8f, 0.2f, 0.7f);
            sm.gravityModifier = 0.4f;
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            sm.maxParticles = 80;
            sm.playOnAwake = false;

            var se = _splash.emission;
            se.rateOverTime = 0f;
            se.SetBursts(new[] { new ParticleSystem.Burst(0.05f, 18, 36) });

            var ss = _splash.shape;
            ss.enabled = true;
            ss.shapeType = ParticleSystemShapeType.Hemisphere;
            ss.radius = 0.08f;

            var splashRenderer = splashGo.GetComponent<ParticleSystemRenderer>();
            splashRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            splashRenderer.material = KinkFxShared.MakeParticleMaterial();
        }
    }

    internal static class ProceduralPiss
    {
        public static AudioClip Create(string name, int seed)
        {
            var rng = new System.Random(seed);
            int sampleRate = 22050;
            float seconds = 2.2f; // loopable hiss
            int samples = Mathf.CeilToInt(sampleRate * seconds);
            float[] data = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)sampleRate;
                // Band-limited noise + soft pulsing for a stream feel.
                float n = ((float)rng.NextDouble() * 2f - 1f);
                float filtered = n * 0.55f + Mathf.Sin(t * 40f + seed) * 0.05f;
                float pulse = 0.75f + 0.25f * Mathf.Sin(t * 6.5f);
                // Soft loop edges
                float edge = 1f;
                float fade = 0.05f;
                if (t < fade) edge = t / fade;
                float end = seconds - t;
                if (end < fade) edge = end / fade;
                data[i] = filtered * pulse * edge * 0.35f;
            }

            var clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
