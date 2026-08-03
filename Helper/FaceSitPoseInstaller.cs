using System;
using System.Collections;
using UnityEngine;

namespace VSFartMod
{
    /// <summary>
    /// No Blackout Smothering: keep vanilla NoIK_FaceSit / NoIK_ButtFaceSit (PlayMaker-safe),
    /// kill the fullscreen SkinOverlay UI PlayMaker enables with smother poses, and lightly
    /// push SuccuPosition away from the lens. Does not clone poses or move CameraPositions.
    /// </summary>
    public class FaceSitPoseInstaller : MonoBehaviour
    {
        public static FaceSitPoseInstaller Instance { get; private set; }

        public static bool NoBlackoutSmothering { get; set; } = true;

        public static bool UseCustomSmotherPoses
        {
            get => NoBlackoutSmothering;
            set => NoBlackoutSmothering = value;
        }

        public static float SmotherBodyPull
        {
            get => _bodyPull;
            set => _bodyPull = Mathf.Clamp(value, 0f, 0.35f);
        }

        public static float EffectiveBodyPull => NoBlackoutSmothering ? SmotherBodyPull : 0f;

        private static float _bodyPull = 0f;
        private float _nextScan;
        private float _nextOverlayFind;
        private Coroutine _waitRoutine;
        private GameObject _skinOverlay;
        private bool _loggedSkinSuppress;

        private static readonly string[] PoseRoots =
        {
            "NoIK_FaceSit",
            "NoIK_ButtFaceSit",
        };

        public static void EnsureCreated(GameObject host)
        {
            if (Instance != null || host == null) return;
            host.AddComponent<FaceSitPoseInstaller>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            VSFartMod.Logger.LogInfo(
                $"FaceSitPoseInstaller ready (NoBlackoutSmothering={NoBlackoutSmothering}, bodyPull={SmotherBodyPull:0.##}) — in-place SuccuOffset, no clones.");
            DestroyLeftoverClones();
        }

        private void OnEnable()
        {
            TryStartWait();
        }

        public void OnSessionLikelyLoaded()
        {
            DestroyLeftoverClones();
            TryStartWait();
        }

        public void ReapplyBodyPull()
        {
            ApplyToAllKnownPoses();
            if (NoBlackoutSmothering)
            {
                ForceHideSkinOverlay();
            }

            VSFartMod.Logger.LogInfo(
                $"No Blackout Smothering {(NoBlackoutSmothering ? "ON" : "OFF")} (effective pull={EffectiveBodyPull:0.##}, SkinOverlay suppress).");
        }

        private void LateUpdate()
        {
            if (!KinkFxShared.IsSessionActive() && GameObject.Find("SpecialAnimations") == null)
            {
                return;
            }

            // PlayMaker re-enables SkinOverlay with FaceSit — kill it every frame when toggle is on.
            if (NoBlackoutSmothering)
            {
                ForceHideSkinOverlay();
            }

            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 0.1f;
            ApplyToActivePoses();
        }

        private void TryStartWait()
        {
            if (_waitRoutine != null) StopCoroutine(_waitRoutine);
            _waitRoutine = StartCoroutine(WaitAndPrep());
        }

        private IEnumerator WaitAndPrep()
        {
            float deadline = Time.unscaledTime + 45f;
            while (Time.unscaledTime < deadline)
            {
                if (GameObject.Find("SpecialAnimations") != null)
                {
                    DestroyLeftoverClones();
                    CacheBases();
                    CacheSkinOverlay();
                    ApplyToAllKnownPoses();
                    VSFartMod.Logger.LogInfo("No Blackout Smothering: hooked FaceSit SuccuPosition + SkinOverlay suppress.");
                    yield break;
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        private void CacheSkinOverlay()
        {
            if (_skinOverlay != null) return;
            _skinOverlay = FindSceneObjectByName("SkinOverlay");
            if (_skinOverlay != null)
            {
                VSFartMod.Logger.LogInfo("Cached SkinOverlay (fullscreen flesh plate used during FaceSit).");
            }
        }

        private void ForceHideSkinOverlay()
        {
            if (_skinOverlay == null)
            {
                // Find() only hits active objects — that's fine once PlayMaker turns it on.
                _skinOverlay = GameObject.Find("SkinOverlay");
                if (_skinOverlay == null && Time.unscaledTime >= _nextOverlayFind)
                {
                    _nextOverlayFind = Time.unscaledTime + 1f;
                    CacheSkinOverlay();
                }
            }

            if (_skinOverlay == null) return;

            if (_skinOverlay.activeSelf)
            {
                _skinOverlay.SetActive(false);
                if (!_loggedSkinSuppress)
                {
                    _loggedSkinSuppress = true;
                    VSFartMod.Logger.LogInfo("No Blackout Smothering: disabled SkinOverlay fullscreen plate.");
                }
            }

            // Belt-and-suspenders if something re-enables children without the root.
            var image = _skinOverlay.transform.Find("SkinOverlayImage");
            if (image != null && image.gameObject.activeSelf)
            {
                image.gameObject.SetActive(false);
            }

            var cg = _skinOverlay.GetComponent<CanvasGroup>();
            if (cg != null && cg.alpha > 0.001f)
            {
                cg.alpha = 0f;
                cg.blocksRaycasts = false;
            }
        }

        private static GameObject FindSceneObjectByName(string name)
        {
            var transforms = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform t = transforms[i];
                if (t == null || t.name != name) continue;
                if (!t.gameObject.scene.IsValid() || !t.gameObject.scene.isLoaded) continue;
                return t.gameObject;
            }

            return null;
        }

        private void ApplyToActivePoses()
        {
            var special = GameObject.Find("SpecialAnimations");
            if (special == null) return;

            for (int i = 0; i < PoseRoots.Length; i++)
            {
                Transform root = FindChildNamed(special.transform, PoseRoots[i]);
                if (root == null || !root.gameObject.activeInHierarchy) continue;
                ApplyBodyPull(root, forceRestore: !NoBlackoutSmothering);
            }
        }

        private void ApplyToAllKnownPoses()
        {
            var special = GameObject.Find("SpecialAnimations");
            if (special == null) return;
            for (int i = 0; i < PoseRoots.Length; i++)
            {
                Transform root = FindChildNamed(special.transform, PoseRoots[i]);
                if (root != null) ApplyBodyPull(root, forceRestore: !NoBlackoutSmothering);
            }
        }

        private void CacheBases()
        {
            var special = GameObject.Find("SpecialAnimations");
            if (special == null) return;
            for (int i = 0; i < PoseRoots.Length; i++)
            {
                Transform root = FindChildNamed(special.transform, PoseRoots[i]);
                if (root == null) continue;
                Transform succu = FindChildNamed(root, "SuccuPosition");
                if (succu == null) continue;
                var tag = succu.GetComponent<SuccuPullTag>();
                if (tag == null)
                {
                    tag = succu.gameObject.AddComponent<SuccuPullTag>();
                    tag.BaseLocal = succu.localPosition;
                }
            }
        }

        private static void ApplyBodyPull(Transform poseRoot, bool forceRestore)
        {
            if (poseRoot == null) return;
            Transform succu = FindChildNamed(poseRoot, "SuccuPosition");
            Transform camMarker = poseRoot.Find("CameraPositions/1")
                                  ?? FindChildNamed(poseRoot, "1");
            if (succu == null) return;

            var tag = succu.GetComponent<SuccuPullTag>();
            if (tag == null)
            {
                tag = succu.gameObject.AddComponent<SuccuPullTag>();
                tag.BaseLocal = succu.localPosition;
            }

            float pull = forceRestore ? 0f : EffectiveBodyPull;
            if (pull <= 0.001f)
            {
                if ((succu.localPosition - tag.BaseLocal).sqrMagnitude > 1e-8f)
                {
                    succu.localPosition = tag.BaseLocal;
                }

                return;
            }

            Vector3 awayWorld;
            if (camMarker != null)
            {
                awayWorld = succu.position - camMarker.position;
                if (awayWorld.sqrMagnitude < 1e-6f)
                {
                    awayWorld = -camMarker.forward;
                }
                else
                {
                    awayWorld.Normalize();
                }
            }
            else
            {
                awayWorld = poseRoot.up;
            }

            Vector3 localDelta = poseRoot.InverseTransformDirection(awayWorld) * pull;
            Vector3 want = tag.BaseLocal + localDelta;
            if ((succu.localPosition - want).sqrMagnitude > 1e-8f)
            {
                succu.localPosition = want;
            }
        }

        private static void DestroyLeftoverClones()
        {
            var special = GameObject.Find("SpecialAnimations");
            if (special == null) return;
            string[] leftovers = { "VSFart_FaceSit", "VSFart_ButtFaceSit" };
            for (int i = 0; i < leftovers.Length; i++)
            {
                Transform t = FindChildNamed(special.transform, leftovers[i]);
                if (t == null) continue;
                VSFartMod.Logger.LogInfo($"Removing broken clone pose '{leftovers[i]}' (using vanilla + SuccuOffset instead).");
                UnityEngine.Object.Destroy(t.gameObject);
            }
        }

        private static Transform FindChildNamed(Transform parent, string name)
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name == name) return c;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                var hit = FindChildNamed(parent.GetChild(i), name);
                if (hit != null) return hit;
            }

            return null;
        }

        private sealed class SuccuPullTag : MonoBehaviour
        {
            public Vector3 BaseLocal;
        }
    }
}
