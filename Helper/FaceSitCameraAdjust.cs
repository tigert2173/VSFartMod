using System;
using UnityEngine;

namespace VSFartMod
{
    /// <summary>
    /// Subtle FaceSit / ButtFaceSit camera-marker nudge.
    /// Default: slight local −Y (down) so the view sits on the anus, not just the top of the crack.
    /// Full pullback stays off — that framed her off-lens.
    /// </summary>
    public class FaceSitCameraAdjust : MonoBehaviour
    {
        public static FaceSitCameraAdjust Instance { get; private set; }

        /// <summary>When false, markers are left at vanilla positions.</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>How far to pull the camera back along local −Z (0 = none).</summary>
        public static float PullBack
        {
            get => _pullBack;
            set => _pullBack = Mathf.Clamp(value, 0f, 0.5f);
        }

        /// <summary>Extra local +Y (raise). Prefer Lower for aiming down the crack.</summary>
        public static float Raise
        {
            get => _raise;
            set => _raise = Mathf.Clamp(value, 0f, 0.25f);
        }

        /// <summary>Local −Y drop so the lens aims lower (anus vs top of crack).</summary>
        public static float Lower
        {
            get => _lower;
            set => _lower = Mathf.Clamp(value, 0f, 0.2f);
        }

        private static float _pullBack = 0f;
        private static float _raise = 0f;
        private static float _lower = 0.035f;

        private Transform _activeMarker;
        private Vector3 _markerBaseLocal;
        private bool _markerBaseCached;
        private float _nextScan;

        private static readonly string[] PoseRoots =
        {
            "NoIK_ButtFaceSit",
            "NoIK_FaceSit",
            "VSFart_ButtFaceSit",
            "VSFart_FaceSit",
        };

        public static void EnsureCreated(GameObject host)
        {
            if (Instance != null || host == null) return;
            host.AddComponent<FaceSitCameraAdjust>();
        }

        public static bool HasAnyOffset =>
            PullBack > 0.0001f || Raise > 0.0001f || Lower > 0.0001f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            VSFartMod.Logger.LogInfo(
                $"FaceSitCameraAdjust ready (Enabled={Enabled}, lower={Lower:0.###}, raise={Raise:0.##}, pull={PullBack:0.##}).");
        }

        private void LateUpdate()
        {
            if (!Enabled || !HasAnyOffset)
            {
                RestoreIfNeeded();
                return;
            }

            if (!KinkFxShared.IsSessionActive() && GameObject.Find("SpecialAnimations") == null)
            {
                RestoreIfNeeded();
                return;
            }

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.2f;
                RefreshActiveMarker();
            }

            if (_activeMarker == null)
            {
                RestoreIfNeeded();
                return;
            }

            if (!_markerBaseCached)
            {
                _markerBaseLocal = _activeMarker.localPosition;
                _markerBaseCached = true;
            }

            // −Y = down the crack; −Z = pull back (usually leave 0).
            _activeMarker.localPosition = _markerBaseLocal
                                          + new Vector3(0f, Raise - Lower, -PullBack);
        }

        private void RefreshActiveMarker()
        {
            Transform found = null;
            var special = GameObject.Find("SpecialAnimations");
            if (special != null)
            {
                for (int i = 0; i < PoseRoots.Length; i++)
                {
                    var root = FindChildRecursive(special.transform, PoseRoots[i]);
                    if (root == null || !root.gameObject.activeInHierarchy) continue;
                    var marker = root.Find("CameraPositions/1")
                                 ?? FindChildRecursive(root, "1");
                    if (marker != null)
                    {
                        found = marker;
                        break;
                    }
                }
            }

            if (found == null)
            {
                for (int i = 0; i < PoseRoots.Length; i++)
                {
                    var go = GameObject.Find(PoseRoots[i]);
                    if (go == null || !go.activeInHierarchy) continue;
                    var marker = go.transform.Find("CameraPositions/1");
                    if (marker != null)
                    {
                        found = marker;
                        break;
                    }
                }
            }

            if (found != _activeMarker)
            {
                if (_activeMarker != null && _markerBaseCached)
                {
                    _activeMarker.localPosition = _markerBaseLocal;
                }

                _activeMarker = found;
                _markerBaseCached = false;
            }
        }

        private void RestoreIfNeeded()
        {
            if (_activeMarker != null && _markerBaseCached)
            {
                _activeMarker.localPosition = _markerBaseLocal;
            }

            _activeMarker = null;
            _markerBaseCached = false;
        }

        private void OnDisable()
        {
            RestoreIfNeeded();
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent == null) return null;
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var hit = FindChildRecursive(parent.GetChild(i), name);
                if (hit != null) return hit;
            }

            return null;
        }
    }
}
