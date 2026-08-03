using System;
using UnityEngine;
using UnityEngine.UI;

namespace VSFartMod
{
    /// <summary>
    /// First-person fart-mask HUD + rubber tube from her hips to the lens
    /// while mask/tube task text is on screen.
    /// </summary>
    public class FartMaskVisuals : MonoBehaviour
    {
        public static FartMaskVisuals Instance { get; private set; }

        public float fadeSpeed = 4.5f;
        public float tubeWidth = 0.038f;
        public float sag = 0.18f;
        public int tubePoints = 14;

        private Canvas _canvas;
        private CanvasGroup _group;
        private RawImage _maskImage;
        private RawImage _fogImage;
        private Texture2D _maskTex;
        private Texture2D _fogTex;
        private LineRenderer _tube;
        private GameObject _hipNub;
        private GameObject _lensNub;
        private Material _tubeMat;
        private float _targetAlpha;
        private float _fogAlpha;
        private bool _built;
        private bool _f10WasDown;
        private float _forceUntil = -1f;

        private static readonly string[] MaskNeedles =
        {
            "fart mask", "through the tube", "down the tube"
        };

        public static void EnsureCreated(GameObject host)
        {
            if (Instance != null || host == null) return;
            host.AddComponent<FartMaskVisuals>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            Build();
            VSFartMod.Logger.LogInfo("FartMaskVisuals ready (mask HUD + tube). Debug: F10 force-show.");
        }

        private void Update()
        {
            if (WasF10Pressed())
            {
                _forceUntil = Time.unscaledTime + 12f;
                VSFartMod.Logger.LogInfo("Fart mask + tube forced on for 12s (F10).");
            }

            bool want = Time.unscaledTime < _forceUntil
                        || (KinkFxShared.IsSessionActive() && KinkFxShared.AnyActiveUiTextContains(MaskNeedles));

            _targetAlpha = want ? 1f : 0f;
            if (_group != null)
            {
                float a = _group.alpha;
                a = Mathf.MoveTowards(a, _targetAlpha, fadeSpeed * Time.unscaledDeltaTime);
                _group.alpha = a;
                _group.blocksRaycasts = false;
                _group.interactable = false;
                bool show = a > 0.02f;
                if (_canvas != null && _canvas.gameObject.activeSelf != show)
                {
                    _canvas.gameObject.SetActive(show);
                }
            }

            if (_fogImage != null)
            {
                _fogAlpha = Mathf.MoveTowards(_fogAlpha, 0f, 0.55f * Time.unscaledDeltaTime);
                if (FartEffects.KeepCameraClear)
                {
                    _fogAlpha = Mathf.Min(_fogAlpha, FartEffects.MaxCloudOpacity);
                }
                var c = _fogImage.color;
                c.a = _fogAlpha;
                _fogImage.color = c;
            }

            bool tubeOn = _targetAlpha > 0.5f || (_group != null && _group.alpha > 0.15f);
            UpdateTube(tubeOn);
        }

        /// <summary>Brief green fog inside the mask lenses after a tube blast.</summary>
        public void PulseGasFog(float strength = 0.55f)
        {
            float cap = FartEffects.KeepCameraClear ? FartEffects.MaxCloudOpacity : 1f;
            _fogAlpha = Mathf.Clamp(Mathf.Max(_fogAlpha, strength), 0f, cap);
            if (_fogImage != null)
            {
                var c = _fogImage.color;
                c.a = _fogAlpha;
                _fogImage.color = c;
            }
        }

        private bool WasF10Pressed()
        {
            bool f10 = NativeInput.IsKeyDown(0x79); // VK_F10
            bool edge = f10 && !_f10WasDown;
            _f10WasDown = f10;
            try
            {
                if (Input.GetKeyDown(KeyCode.F10)) return true;
            }
            catch { /* ignored */ }
            return edge;
        }

        private void Build()
        {
            if (_built) return;
            _built = true;

            var canvasGo = new GameObject("VSFartMaskCanvas");
            UnityEngine.Object.DontDestroyOnLoad(canvasGo);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 800; // above session chrome so the mask frame is visible
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.AddComponent<GraphicRaycaster>();
            _group = canvasGo.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            canvasGo.SetActive(false);

            _maskTex = BuildMaskTexture(1024, 1024);
            _fogTex = BuildFogTexture(512, 512);

            var maskGo = new GameObject("MaskOverlay");
            maskGo.transform.SetParent(canvasGo.transform, false);
            _maskImage = maskGo.AddComponent<RawImage>();
            _maskImage.texture = _maskTex;
            _maskImage.color = Color.white;
            var mrt = _maskImage.rectTransform;
            mrt.anchorMin = Vector2.zero;
            mrt.anchorMax = Vector2.one;
            mrt.offsetMin = Vector2.zero;
            mrt.offsetMax = Vector2.zero;

            var fogGo = new GameObject("MaskFog");
            fogGo.transform.SetParent(canvasGo.transform, false);
            _fogImage = fogGo.AddComponent<RawImage>();
            _fogImage.texture = _fogTex;
            _fogImage.color = new Color(0.45f, 0.75f, 0.28f, 0f);
            var frt = _fogImage.rectTransform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;

            _tubeMat = new Material(Shader.Find("Sprites/Default")
                                    ?? Shader.Find("Unlit/Color")
                                    ?? Shader.Find("Standard"));
            _tubeMat.color = new Color(0.12f, 0.12f, 0.14f, 1f);

            var tubeGo = new GameObject("FartMaskHose");
            UnityEngine.Object.DontDestroyOnLoad(tubeGo);
            _tube = tubeGo.AddComponent<LineRenderer>();
            _tube.positionCount = Mathf.Max(6, tubePoints);
            _tube.startWidth = tubeWidth;
            _tube.endWidth = tubeWidth * 0.92f;
            _tube.material = _tubeMat;
            _tube.textureMode = LineTextureMode.Stretch;
            _tube.numCapVertices = 4;
            _tube.numCornerVertices = 4;
            _tube.useWorldSpace = true;
            _tube.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _tube.receiveShadows = false;
            _tube.enabled = false;

            _hipNub = CreateNub("HoseHipNub", 0.028f);
            _lensNub = CreateNub("HoseLensNub", 0.032f);
        }

        private static GameObject CreateNub(string name, float radius)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.transform.localScale = Vector3.one * (radius * 2f);
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
            var rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                var mat = new Material(Shader.Find("Sprites/Default")
                                       ?? Shader.Find("Unlit/Color")
                                       ?? Shader.Find("Standard"));
                mat.color = new Color(0.1f, 0.1f, 0.12f, 1f);
                rend.material = mat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            go.SetActive(false);
            return go;
        }

        private void UpdateTube(bool on)
        {
            if (_tube == null) return;

            if (!on)
            {
                _tube.enabled = false;
                if (_hipNub != null) _hipNub.SetActive(false);
                if (_lensNub != null) _lensNub.SetActive(false);
                return;
            }

            var cam = Camera.main;
            if (cam == null)
            {
                _tube.enabled = false;
                return;
            }

            var hips = KinkFxShared.FindBoneExact("Hips")
                       ?? KinkFxShared.FindBone("Hips")
                       ?? KinkFxShared.FindBone("Pelvis")
                       ?? KinkFxShared.FindBone("hip");

            Vector3 from;
            if (hips != null)
            {
                // Slightly behind/below hips — reads as coming from her rear.
                from = hips.position + hips.TransformDirection(new Vector3(0f, -0.05f, -0.12f));
            }
            else
            {
                from = cam.transform.position + cam.transform.forward * 0.7f + Vector3.up * 0.15f;
            }

            // Tube plugs into the mask just in front of the lens / mouth area.
            Vector3 to = cam.transform.position
                         + cam.transform.forward * 0.16f
                         + cam.transform.up * -0.06f;

            int n = _tube.positionCount;
            Vector3 mid = (from + to) * 0.5f + Vector3.down * sag;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                // Quadratic Bezier sag so it looks like a hose, not a laser.
                Vector3 p = (1f - t) * (1f - t) * from
                            + 2f * (1f - t) * t * mid
                            + t * t * to;
                _tube.SetPosition(i, p);
            }

            _tube.startWidth = tubeWidth;
            _tube.endWidth = tubeWidth * 0.9f;
            _tube.enabled = true;

            if (_hipNub != null)
            {
                _hipNub.SetActive(true);
                _hipNub.transform.position = from;
            }

            if (_lensNub != null)
            {
                _lensNub.SetActive(true);
                _lensNub.transform.position = to;
            }
        }

        private static Texture2D BuildMaskTexture(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            float cx = w * 0.5f;
            float cy = h * 0.52f;

            // Big open viewing window — keep her body/ass visible through the middle.
            float viewRx = w * 0.42f;
            float viewRy = h * 0.38f;

            // Eye lens frames (rings only — interiors stay clear).
            float eyeY = h * 0.58f;
            float eyeRx = w * 0.17f;
            float eyeRy = h * 0.14f;
            float eyeSep = w * 0.19f;
            float eyeRim = 0.12f; // ring thickness in ellipse units

            // Mouth / tube port near bottom of the open window.
            float portCx = cx;
            float portCy = h * 0.30f;
            float portR = w * 0.055f;

            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float nx = (x - cx) / (w * 0.5f);
                    float ny = (y - cy) / (h * 0.5f);
                    float edgeDist = Mathf.Sqrt(nx * nx + ny * ny); // 0 center → ~1.4 corners

                    float view = Ellipse((x - cx) / viewRx, (y - cy) / viewRy);

                    float leftEye = Ellipse((x - (cx - eyeSep)) / eyeRx, (y - eyeY) / eyeRy);
                    float rightEye = Ellipse((x - (cx + eyeSep)) / eyeRx, (y - eyeY) / eyeRy);
                    float portD = Dist(x, y, portCx, portCy) / portR;

                    Color32 c = new Color32(0, 0, 0, 0);

                    // Soft corner vignette only — never full-screen black.
                    if (edgeDist > 0.88f)
                    {
                        float t = Mathf.InverseLerp(0.88f, 1.35f, edgeDist);
                        byte a = (byte)(Mathf.Clamp01(t) * 160f);
                        c = new Color32(18, 16, 14, a);
                    }

                    // Rubber frame outside the open viewing oval.
                    if (view > 1f)
                    {
                        float frame = Mathf.InverseLerp(1f, 1.35f, view);
                        byte a = (byte)Mathf.Lerp(200f, 90f, Mathf.Clamp01(frame));
                        // Prefer frame over lighter vignette.
                        if (a > c.a)
                        {
                            c = new Color32(28, 26, 24, a);
                        }
                    }
                    else
                    {
                        // Inside open window: keep almost fully clear.
                        c = new Color32(0, 0, 0, 0);
                    }

                    // Lens rings (visible mask cue) — hollow so you still see through.
                    void Ring(float e)
                    {
                        if (e >= 1f - eyeRim && e <= 1f + eyeRim * 0.35f)
                        {
                            float rimT = 1f - Mathf.Abs(e - 1f) / eyeRim;
                            byte a = (byte)(rimT * 210f);
                            c = new Color32(42, 40, 38, a);
                        }
                    }
                    Ring(leftEye);
                    Ring(rightEye);

                    // Tube socket — clear read that a hose plugs into the mask.
                    if (portD <= 1.15f)
                    {
                        if (portD > 0.55f)
                            c = new Color32(55, 52, 48, 230);
                        else if (portD > 0.28f)
                            c = new Color32(22, 22, 24, 200);
                        else
                            c = new Color32(8, 8, 10, 160);
                    }

                    // Thin strap line across the brow.
                    float browY = h * 0.72f;
                    if (Mathf.Abs(y - browY) < h * 0.012f && Mathf.Abs(x - cx) < w * 0.38f && view <= 1.05f)
                    {
                        c = new Color32(36, 34, 32, 190);
                    }

                    pixels[y * w + x] = c;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D BuildFogTexture(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            float cx = w * 0.5f;
            float eyeY = h * 0.58f;
            float eyeRx = w * 0.17f;
            float eyeRy = h * 0.14f;
            float eyeSep = w * 0.19f;

            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float left = Ellipse((x - (cx - eyeSep)) / eyeRx, (y - eyeY) / eyeRy);
                    float right = Ellipse((x - (cx + eyeSep)) / eyeRx, (y - eyeY) / eyeRy);
                    float e = Mathf.Min(left, right);
                    if (e > 1f)
                    {
                        pixels[y * w + x] = new Color32(0, 0, 0, 0);
                    }
                    else
                    {
                        // Soft green tint in lenses only — never opaque.
                        byte a = (byte)((1f - e) * 70f);
                        pixels[y * w + x] = new Color32(90, 160, 50, a);
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static float Ellipse(float nx, float ny) => nx * nx + ny * ny;

        private static float Dist(float x, float y, float cx, float cy)
        {
            float dx = x - cx;
            float dy = y - cy;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }
    }
}
