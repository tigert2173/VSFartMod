using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VSFartMod
{
    /// <summary>
    /// Domme scat: short brown particle bursts aimed at the camera during FaceSit / ButtFaceSit
    /// when Scat task text is on screen. Never continuous — burst + cooldown only.
    /// </summary>
    public class ScatEffects : MonoBehaviour
    {
        public static ScatEffects Instance { get; private set; }
        public static bool ScatKinkEnabled { get; set; }

        public float scanInterval = 0.75f;
        public float minBurstGap = 12f;
        public float maxBurstGap = 24f;
        public float volume = 0.5f;

        private readonly List<AudioClip> _clips = new List<AudioClip>();
        private ParticleSystem _spray;
        private ParticleSystem _splat;
        private AudioSource _audio;
        private float _nextScan;
        private float _nextBurst;
        private float _loggedContextAt = -999f;
        private bool _f5WasDown;

        // Her-on-you only. Avoid matching generic "shit" slang / self-play.
        private static readonly string[] TaskNeedles =
        {
            "scat",
            "shits on your", "shits on you",
            "shitting on your", "shitting on you",
            "shit on your face", "shit onto your",
            "poops on your", "poops on you",
            "pooping on your", "pooping on you",
            "brown shower",
            "feeds you shit", "her shit on your",
            "take her shit", "her scat on",
            "uses your face as her toilet for scat",
            "for shitting on you", "while she shits on you"
        };

        private static readonly string[] ExcludeNeedles =
        {
            "yourself", "your own", "on yourself", "fart toilet"
        };

        public static void EnsureCreated(GameObject host)
        {
            if (Instance != null || host == null) return;
            host.AddComponent<ScatEffects>();
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
            BuildSystems();
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.loop = false;
            _audio.spatialBlend = 0f;
            _audio.ignoreListenerPause = true;
            _audio.bypassListenerEffects = true;
            _audio.volume = volume;
            _nextBurst = Time.unscaledTime + 20f;
            VSFartMod.Logger.LogInfo($"ScatEffects ready with {_clips.Count} clips. Debug: F5. Auto only on her-on-you scat task text.");
        }

        private void Update()
        {
            if (WasF5Pressed())
            {
                ForceBurst("debug F5");
            }

            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + scanInterval;

            if (!IsScatContextActive()) return;

            if (Time.unscaledTime - _loggedContextAt > 8f)
            {
                _loggedContextAt = Time.unscaledTime;
                VSFartMod.Logger.LogInfo("Her-on-you scat task text active — short burst.");
            }

            if (Time.unscaledTime >= _nextBurst)
            {
                PlayBurst();
                _nextBurst = Time.unscaledTime + UnityEngine.Random.Range(minBurstGap, maxBurstGap);
            }
        }

        private bool WasF5Pressed()
        {
            bool f5 = NativeInput.IsKeyDown(0x74); // VK_F5
            bool edge = f5 && !_f5WasDown;
            _f5WasDown = f5;
            try
            {
                if (Input.GetKeyDown(KeyCode.F5)) return true;
            }
            catch { /* ignored */ }
            return edge;
        }

        public void ForceBurst(string reason = "manual")
        {
            PlayBurst(strong: true);
            VSFartMod.Logger.LogInfo($"Scat ForceBurst ({reason}).");
            _nextBurst = Time.unscaledTime + UnityEngine.Random.Range(minBurstGap, maxBurstGap);
        }

        private bool IsScatContextActive()
        {
            if (!KinkFxShared.IsSessionActive()) return false;

            string hit = KinkFxShared.FindActiveInstructionContaining(TaskNeedles);
            if (string.IsNullOrEmpty(hit)) return false;
            if (KinkFxShared.ContainsAny(hit, ExcludeNeedles)) return false;
            return true;
        }

        private void PlayBurst(bool strong = false)
        {
            var cam = Camera.main;
            var hips = KinkFxShared.FindBoneExact("Hips")
                       ?? KinkFxShared.FindBone("Hips")
                       ?? KinkFxShared.FindBone("Pelvis")
                       ?? KinkFxShared.FindBone("hip");

            Vector3 origin;
            if (hips != null)
            {
                origin = hips.position + hips.TransformDirection(new Vector3(0f, -0.05f, -0.12f));
            }
            else if (cam != null)
            {
                origin = cam.transform.position + cam.transform.forward * 0.55f + cam.transform.up * -0.1f;
            }
            else
            {
                origin = transform.position;
            }

            Vector3 aim;
            Vector3 face;
            if (cam != null)
            {
                face = cam.transform.position + cam.transform.forward * KinkFxShared.ClarityCamDistance(0.4f);
                aim = (face - origin).normalized;
                if (aim.sqrMagnitude < 0.01f) aim = Vector3.down;
            }
            else
            {
                face = origin + Vector3.down;
                aim = Vector3.down;
            }

            float intensity = strong ? 1f : UnityEngine.Random.Range(0.55f, 1f);
            float sizeMul = KinkFxShared.ClaritySizeMul();

            if (_spray != null)
            {
                _spray.transform.position = origin;
                _spray.transform.rotation = Quaternion.LookRotation(aim);
                var main = _spray.main;
                main.startColor = KinkFxShared.ClarityLiquid(new Color(0.42f, 0.22f, 0.08f, 0.92f));
                main.startSize = new ParticleSystem.MinMaxCurve(0.035f * sizeMul, 0.1f * sizeMul);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f + intensity, 2.6f + intensity);
                var emission = _spray.emission;
                short lo = (short)KinkFxShared.ClarityCount(10 + (int)(intensity * 12));
                short hi = (short)KinkFxShared.ClarityCount(18 + (int)(intensity * 20));
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, lo, hi) });
                _spray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _spray.Play();
            }

            if (_splat != null && cam != null)
            {
                _splat.transform.position = face;
                _splat.transform.rotation = Quaternion.LookRotation(-cam.transform.forward);
                var sm = _splat.main;
                sm.startColor = KinkFxShared.ClarityLiquid(new Color(0.38f, 0.18f, 0.06f, 0.85f));
                sm.startSize = new ParticleSystem.MinMaxCurve(0.025f * sizeMul, 0.08f * sizeMul);
                var se = _splat.emission;
                se.SetBursts(new[]
                {
                    new ParticleSystem.Burst(0.05f,
                        (short)KinkFxShared.ClarityCount(10),
                        (short)KinkFxShared.ClarityCount(22))
                });
                _splat.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _splat.Play();
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
                    _audio.pitch = UnityEngine.Random.Range(0.9f, 1.1f);
                    _audio.volume = volume * (0.85f + intensity * 0.2f);
                    _audio.Play();
                }
            }
        }

        private void LoadClips()
        {
            _clips.Clear();
            string[] folders =
            {
                Path.Combine(Application.streamingAssetsPath, "VSFartSounds", "Scat"),
                Path.Combine(Application.streamingAssetsPath, "Scat"),
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
                        VSFartMod.Logger.LogWarning($"Scat WAV fail {file}: {ex.Message}");
                    }
                }
            }

            if (_clips.Count == 0)
            {
                VSFartMod.Logger.LogWarning("No scat WAVs — generating procedural soft splat sounds. Drop Scat_*.wav into VSFartSounds/Scat/.");
                for (int i = 0; i < 4; i++)
                {
                    _clips.Add(ProceduralScat.Create($"ProcScat_{i}", 4200 + i * 77));
                }
            }
        }

        private void BuildSystems()
        {
            var sprayGo = new GameObject("ScatSpray");
            sprayGo.transform.SetParent(transform, false);
            _spray = sprayGo.AddComponent<ParticleSystem>();

            var main = _spray.main;
            main.loop = false;
            main.duration = 0.9f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
            main.startColor = new Color(0.42f, 0.22f, 0.08f, 0.92f);
            main.gravityModifier = 1.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;
            main.playOnAwake = false;

            var emission = _spray.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12, 24) });

            var shape = _spray.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.02f;

            var sizeOverLifetime = _spray.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.55f));

            var colorOverLifetime = _spray.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.48f, 0.26f, 0.1f), 0f),
                    new GradientColorKey(new Color(0.28f, 0.14f, 0.05f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.95f, 0f),
                    new GradientAlphaKey(0.75f, 0.6f),
                    new GradientAlphaKey(0.2f, 1f)
                });
            colorOverLifetime.color = grad;

            var sprayRenderer = sprayGo.GetComponent<ParticleSystemRenderer>();
            sprayRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            sprayRenderer.material = KinkFxShared.MakeParticleMaterial();

            var splatGo = new GameObject("ScatSplat");
            splatGo.transform.SetParent(transform, false);
            _splat = splatGo.AddComponent<ParticleSystem>();

            var sm = _splat.main;
            sm.loop = false;
            sm.duration = 0.7f;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.7f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.1f);
            sm.startColor = new Color(0.38f, 0.18f, 0.06f, 0.85f);
            sm.gravityModifier = 0.35f;
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            sm.maxParticles = 60;
            sm.playOnAwake = false;

            var se = _splat.emission;
            se.rateOverTime = 0f;
            se.SetBursts(new[] { new ParticleSystem.Burst(0.05f, 10, 22) });

            var ss = _splat.shape;
            ss.enabled = true;
            ss.shapeType = ParticleSystemShapeType.Hemisphere;
            ss.radius = 0.1f;

            var splatRenderer = splatGo.GetComponent<ParticleSystemRenderer>();
            splatRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            splatRenderer.material = KinkFxShared.MakeParticleMaterial();
        }
    }

    internal static class ProceduralScat
    {
        public static AudioClip Create(string name, int seed)
        {
            var rng = new System.Random(seed);
            int sampleRate = 22050;
            float seconds = 0.55f + (seed % 5) * 0.08f;
            int samples = Mathf.CeilToInt(sampleRate * seconds);
            float[] data = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)sampleRate;
                float n = ((float)rng.NextDouble() * 2f - 1f);
                // Soft low thuds + wet noise — short splat, not a loop.
                float thud = Mathf.Exp(-t * 9f) * Mathf.Sin(t * (90f + seed % 40) * Mathf.PI * 2f) * 0.45f;
                float wet = n * Mathf.Exp(-t * 4.5f) * 0.28f;
                float edge = 1f;
                if (t < 0.02f) edge = t / 0.02f;
                float end = seconds - t;
                if (end < 0.08f) edge = end / 0.08f;
                data[i] = (thud + wet) * edge * 0.55f;
            }

            var clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
