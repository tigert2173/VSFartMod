using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VSFartMod
{
    /// <summary>
    /// Skunk Girl: procedural black/white striped tail on her hips + musk spray bursts.
    /// Tail shows in-session when the kink is on; spray fires on skunk/spray task text (or F12).
    /// </summary>
    public class SkunkGirlEffects : MonoBehaviour
    {
        public static SkunkGirlEffects Instance { get; private set; }
        public static bool SkunkGirlEnabled { get; set; }

        public float scanInterval = 0.6f;
        public float minSprayGap = 5f;
        public float maxSprayGap = 12f;
        public float volume = 0.6f;
        public int tailSegments = 9;

        private readonly List<AudioClip> _clips = new List<AudioClip>();
        private readonly List<Transform> _segments = new List<Transform>();
        private GameObject _tailRoot;
        private Transform _boundHips;
        private ParticleSystem _spray;
        private ParticleSystem _mist;
        private AudioSource _audio;
        private Material _blackMat;
        private Material _whiteMat;
        private float _nextScan;
        private float _nextSpray;
        private float _nextRebind;
        private float _loggedContextAt = -999f;
        private bool _f12WasDown;
        private bool _tailWanted;

        private static readonly string[] SprayNeedles =
        {
            "skunk", "skunk spray", "raise her tail", "lifts her tail",
            "sprays you", "spray you", "sprays musk", "musk spray",
            "skunk girl", "through her tail"
        };

        public static void EnsureCreated(GameObject host)
        {
            if (Instance != null || host == null) return;
            host.AddComponent<SkunkGirlEffects>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            BuildMaterials();
            BuildTail();
            BuildSpraySystems();
            LoadClips();
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.loop = false;
            _audio.spatialBlend = 0f;
            _audio.ignoreListenerPause = true;
            _audio.bypassListenerEffects = true;
            _audio.volume = volume;
            _nextSpray = Time.unscaledTime + 12f;
            SetTailVisible(false);
            VSFartMod.Logger.LogInfo($"SkunkGirlEffects ready ({_clips.Count} clips). Tail when Skunk Girl on. Debug spray: F12.");
        }

        private void LateUpdate()
        {
            bool wantTail = SkunkGirlEnabled && KinkFxShared.IsSessionActive();
            if (wantTail != _tailWanted)
            {
                _tailWanted = wantTail;
                SetTailVisible(wantTail);
            }

            if (!_tailWanted) return;

            if (Time.unscaledTime >= _nextRebind || _boundHips == null)
            {
                _nextRebind = Time.unscaledTime + 2.5f;
                RebindTail();
            }

            AnimateTailSway();
        }

        private void Update()
        {
            if (WasF12Pressed())
            {
                ForceSpray("debug F12");
            }

            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + scanInterval;

            if (!IsSprayContextActive()) return;

            if (Time.unscaledTime - _loggedContextAt > 8f)
            {
                _loggedContextAt = Time.unscaledTime;
                VSFartMod.Logger.LogInfo("Skunk spray task active.");
            }

            if (Time.unscaledTime >= _nextSpray)
            {
                PlaySpray();
                _nextSpray = Time.unscaledTime + UnityEngine.Random.Range(minSprayGap, maxSprayGap);
            }
        }

        public void ForceSpray(string reason = "manual")
        {
            if (!SkunkGirlEnabled)
            {
                SkunkGirlEnabled = true; // preview also shows the tail
            }
            PlaySpray(strong: true);
            VSFartMod.Logger.LogInfo($"Skunk ForceSpray ({reason}).");
            _nextSpray = Time.unscaledTime + UnityEngine.Random.Range(minSprayGap, maxSprayGap);
        }

        private bool WasF12Pressed()
        {
            bool f12 = NativeInput.IsKeyDown(0x7B); // VK_F12
            bool edge = f12 && !_f12WasDown;
            _f12WasDown = f12;
            try
            {
                if (Input.GetKeyDown(KeyCode.F12)) return true;
            }
            catch { /* ignored */ }
            return edge;
        }

        private bool IsSprayContextActive()
        {
            if (!SkunkGirlEnabled || !KinkFxShared.IsSessionActive()) return false;
            return KinkFxShared.AnyActiveUiTextContains(SprayNeedles);
        }

        private void PlaySpray(bool strong = false)
        {
            var cam = Camera.main;
            var hips = _boundHips
                       ?? KinkFxShared.FindBoneExact("Hips")
                       ?? KinkFxShared.FindBone("Hips")
                       ?? KinkFxShared.FindBone("Pelvis");

            Vector3 origin;
            if (hips != null)
            {
                origin = hips.position + hips.TransformDirection(new Vector3(0f, 0.02f, -0.16f));
            }
            else if (cam != null)
            {
                origin = cam.transform.position + cam.transform.forward * 0.6f;
            }
            else
            {
                origin = transform.position;
            }

            Vector3 aim = cam != null
                ? (cam.transform.position + cam.transform.forward * KinkFxShared.ClarityCamDistance(0.35f) - origin).normalized
                : Vector3.forward;
            if (aim.sqrMagnitude < 0.01f) aim = Vector3.forward;

            float intensity = strong ? 1f : UnityEngine.Random.Range(0.55f, 1f);
            Color musk = KinkFxShared.ClarityColor(new Color(0.35f, 0.55f, 0.22f, 0.55f + intensity * 0.25f));
            float sizeMul = KinkFxShared.ClaritySizeMul();

            if (_spray != null)
            {
                _spray.transform.position = origin;
                _spray.transform.rotation = Quaternion.LookRotation(aim);
                var main = _spray.main;
                main.startColor = musk;
                main.startSize = new ParticleSystem.MinMaxCurve(0.06f * sizeMul, 0.2f * sizeMul);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f + intensity, 3.8f + intensity);
                var emission = _spray.emission;
                short lo = (short)KinkFxShared.ClarityCount(18 + (int)(intensity * 20));
                short hi = (short)KinkFxShared.ClarityCount(32 + (int)(intensity * 28));
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, lo, hi) });
                _spray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _spray.Play();
            }

            if (_mist != null && cam != null)
            {
                _mist.transform.position = cam.transform.position
                    + cam.transform.forward * KinkFxShared.ClarityCamDistance(0.42f);
                _mist.transform.rotation = Quaternion.LookRotation(-cam.transform.forward);
                var mm = _mist.main;
                mm.startColor = KinkFxShared.ClarityColor(new Color(musk.r, musk.g, musk.b, 0.45f));
                mm.startSize = new ParticleSystem.MinMaxCurve(0.14f * sizeMul, 0.35f * sizeMul);
                var me = _mist.emission;
                me.SetBursts(new[]
                {
                    new ParticleSystem.Burst(0f,
                        (short)KinkFxShared.ClarityCount(12),
                        (short)KinkFxShared.ClarityCount(22))
                });
                _mist.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _mist.Play();
            }

            // Lift tip of tail briefly when spraying.
            if (_segments.Count > 2)
            {
                var tip = _segments[_segments.Count - 1];
                if (tip != null)
                {
                    tip.localRotation = Quaternion.Euler(-35f, 0f, 0f);
                }
            }

            if (_clips.Count > 0 && _audio != null)
            {
                var clip = _clips[UnityEngine.Random.Range(0, _clips.Count)];
                if (clip != null)
                {
                    if (AudioListener.pause) AudioListener.pause = false;
                    _audio.pitch = UnityEngine.Random.Range(0.92f, 1.08f);
                    _audio.PlayOneShot(clip, volume * (0.8f + intensity * 0.25f));
                }
            }
        }

        private void RebindTail()
        {
            var hips = KinkFxShared.FindBoneExact("Hips")
                       ?? KinkFxShared.FindBone("Hips")
                       ?? KinkFxShared.FindBone("Pelvis")
                       ?? KinkFxShared.FindBone("hip");
            if (hips == null || _tailRoot == null) return;

            if (_boundHips == hips && _tailRoot.transform.parent == hips) return;

            _boundHips = hips;
            _tailRoot.transform.SetParent(hips, false);
            _tailRoot.transform.localPosition = new Vector3(0f, -0.02f, -0.1f);
            _tailRoot.transform.localRotation = Quaternion.Euler(25f, 0f, 0f);
            _tailRoot.transform.localScale = Vector3.one;
        }

        private void AnimateTailSway()
        {
            if (_segments.Count == 0) return;
            float t = Time.unscaledTime;
            for (int i = 0; i < _segments.Count; i++)
            {
                var s = _segments[i];
                if (s == null) continue;
                float w = (i + 1) / (float)_segments.Count;
                float yaw = Mathf.Sin(t * 1.7f + i * 0.45f) * (8f + w * 14f);
                float pitch = Mathf.Sin(t * 1.1f + i * 0.3f) * (4f + w * 8f) - w * 6f;
                s.localRotation = Quaternion.Euler(pitch, yaw, 0f);
            }
        }

        private void SetTailVisible(bool on)
        {
            if (_tailRoot != null) _tailRoot.SetActive(on);
        }

        private void BuildMaterials()
        {
            var shader = Shader.Find("Sprites/Default")
                         ?? Shader.Find("Unlit/Color")
                         ?? Shader.Find("Standard");
            _blackMat = new Material(shader) { color = new Color(0.06f, 0.06f, 0.07f, 1f) };
            _whiteMat = new Material(shader) { color = new Color(0.92f, 0.92f, 0.9f, 1f) };
        }

        private void BuildTail()
        {
            _tailRoot = new GameObject("SkunkTail");
            UnityEngine.Object.DontDestroyOnLoad(_tailRoot);
            _segments.Clear();

            Transform parent = _tailRoot.transform;
            float segLen = 0.07f;
            for (int i = 0; i < tailSegments; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"SkunkSeg_{i}";
                UnityEngine.Object.Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(parent, false);
                float taper = Mathf.Lerp(0.055f, 0.12f, i / (float)(tailSegments - 1));
                // Fluffy mid/tip.
                if (i > tailSegments / 2) taper *= 1.25f;
                go.transform.localScale = new Vector3(taper, taper, segLen * 1.6f);
                go.transform.localPosition = new Vector3(0f, 0f, -segLen * 0.85f);
                go.transform.localRotation = Quaternion.identity;

                var rend = go.GetComponent<Renderer>();
                if (rend != null)
                {
                    // Classic skunk: black body, white stripe segments.
                    bool white = (i % 3 == 1) || i >= tailSegments - 2;
                    rend.material = white ? _whiteMat : _blackMat;
                    rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                _segments.Add(go.transform);
                parent = go.transform;
            }

            _tailRoot.SetActive(false);
        }

        private void BuildSpraySystems()
        {
            var sprayGo = new GameObject("SkunkSpray");
            sprayGo.transform.SetParent(transform, false);
            _spray = sprayGo.AddComponent<ParticleSystem>();

            var main = _spray.main;
            main.loop = false;
            main.duration = 0.85f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.4f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.28f);
            main.startColor = new Color(0.35f, 0.55f, 0.22f, 0.6f);
            main.gravityModifier = 0.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 180;
            main.playOnAwake = false;

            var emission = _spray.emission;
            emission.rateOverTime = 0f;

            var shape = _spray.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.02f;

            var col = _spray.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.4f, 0.65f, 0.25f), 0f),
                    new GradientColorKey(new Color(0.25f, 0.4f, 0.15f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.7f, 0f),
                    new GradientAlphaKey(0.35f, 0.6f),
                    new GradientAlphaKey(0.05f, 1f)
                });
            col.color = g;

            var r = sprayGo.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.material = KinkFxShared.MakeParticleMaterial();

            var mistGo = new GameObject("SkunkMist");
            mistGo.transform.SetParent(transform, false);
            _mist = mistGo.AddComponent<ParticleSystem>();
            var mm = _mist.main;
            mm.loop = false;
            mm.duration = 1.1f;
            mm.startLifetime = 1.2f;
            mm.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            mm.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.55f);
            mm.startColor = new Color(0.3f, 0.5f, 0.2f, 0.45f);
            mm.simulationSpace = ParticleSystemSimulationSpace.World;
            mm.maxParticles = 40;
            mm.playOnAwake = false;
            var me = _mist.emission;
            me.rateOverTime = 0f;
            me.SetBursts(new[] { new ParticleSystem.Burst(0f, 12, 22) });
            var ms = _mist.shape;
            ms.enabled = true;
            ms.shapeType = ParticleSystemShapeType.Sphere;
            ms.radius = 0.12f;
            mistGo.GetComponent<ParticleSystemRenderer>().material = KinkFxShared.MakeParticleMaterial();
        }

        private void LoadClips()
        {
            _clips.Clear();
            string[] folders =
            {
                Path.Combine(Application.streamingAssetsPath, "VSFartSounds", "Skunk"),
                Path.Combine(Application.streamingAssetsPath, "Skunk"),
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
                        VSFartMod.Logger.LogWarning($"Skunk WAV fail {file}: {ex.Message}");
                    }
                }
            }

            if (_clips.Count == 0)
            {
                for (int i = 0; i < 3; i++)
                {
                    _clips.Add(ProceduralSkunkSpray.Create($"ProcSkunk_{i}", 3300 + i * 53));
                }
            }
        }
    }

    internal static class ProceduralSkunkSpray
    {
        public static AudioClip Create(string name, int seed)
        {
            var rng = new System.Random(seed);
            int sampleRate = 22050;
            float seconds = 0.7f;
            int samples = Mathf.CeilToInt(sampleRate * seconds);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)sampleRate;
                float n = ((float)rng.NextDouble() * 2f - 1f);
                float hiss = n * Mathf.Exp(-t * 2.2f) * 0.4f;
                float whoosh = Mathf.Sin(t * 28f) * Mathf.Exp(-t * 3.5f) * 0.25f;
                float edge = t < 0.03f ? t / 0.03f : (seconds - t < 0.1f ? (seconds - t) / 0.1f : 1f);
                data[i] = (hiss + whoosh) * edge;
            }
            var clip = AudioClip.Create(name, samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
