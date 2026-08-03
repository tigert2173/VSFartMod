using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace VSFartMod
{
    internal static class KinkFxShared
    {
        public static bool IsSessionActive()
        {
            // Only fire FX during a real session. ExtraLoadScene / menus must not count —
            // Content Select tooltips contain "fart" / "pee" / "Watersports" and caused spam.
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (s.isLoaded && s.name == "SessionScene")
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True if an instruction-like UI string contains any needle.
        /// Ignores short toggle labels / menu titles so Content Select text cannot trigger FX.
        /// </summary>
        public static bool AnyActiveUiTextContains(params string[] needles)
        {
            return !string.IsNullOrEmpty(FindActiveInstructionContaining(needles));
        }

        /// <summary>Returns the matching instruction text, or null.</summary>
        public static string FindActiveInstructionContaining(string[] needles)
        {
            if (needles == null || needles.Length == 0)
            {
                return null;
            }

            var uiTexts = UnityEngine.Object.FindObjectsOfType<Text>();
            for (int i = 0; i < uiTexts.Length; i++)
            {
                var t = uiTexts[i];
                if (!IsInstructionText(t != null ? t.gameObject : null, t != null ? t.text : null))
                {
                    continue;
                }

                if (ContainsAny(t.text, needles))
                {
                    return t.text;
                }
            }

            var tmp = UnityEngine.Object.FindObjectsOfType<TMP_Text>();
            for (int i = 0; i < tmp.Length; i++)
            {
                var t = tmp[i];
                if (!IsInstructionText(t != null ? t.gameObject : null, t != null ? t.text : null))
                {
                    continue;
                }

                if (ContainsAny(t.text, needles))
                {
                    return t.text;
                }
            }

            return null;
        }

        private static bool IsInstructionText(GameObject go, string text)
        {
            if (go == null || !go.activeInHierarchy || string.IsNullOrEmpty(text))
            {
                return false;
            }

            // Toggle labels / explanations are short; real task lines are longer.
            if (text.Length < 28)
            {
                return false;
            }

            // Content Select / settings blurbs must never drive session FX.
            if (IsMetaSettingsText(text))
            {
                return false;
            }

            string path = go.name;
            Transform p = go.transform.parent;
            if (p != null) path = p.name + "/" + path;
            if (path.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("Toggle", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("ContentSelect", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("Explanation", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("Tooltip", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("Glossary", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("Speech", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("Dialogue", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("Subtitle", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("History", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("LogText", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("Status", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("Kink", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return true;
        }

        /// <summary>True for Content Select / pace-setting copy, not live task instructions.</summary>
        public static bool IsMetaSettingsText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.IndexOf("Fart pace", StringComparison.OrdinalIgnoreCase) >= 0
                   || text.IndexOf("Gas pace", StringComparison.OrdinalIgnoreCase) >= 0
                   || text.IndexOf("Pick one Gas toggle", StringComparison.OrdinalIgnoreCase) >= 0
                   || text.IndexOf("Mod FX setting", StringComparison.OrdinalIgnoreCase) >= 0
                   || text.IndexOf("Keep Camera Clear", StringComparison.OrdinalIgnoreCase) >= 0
                   || text.IndexOf("No Blackout Smothering", StringComparison.OrdinalIgnoreCase) >= 0
                   || text.IndexOf("BepInEx", StringComparison.OrdinalIgnoreCase) >= 0
                   || text.IndexOf("Content Select", StringComparison.OrdinalIgnoreCase) >= 0
                   || text.IndexOf("MaxCloudOpacity", StringComparison.OrdinalIgnoreCase) >= 0
                   || text.IndexOf("FrequencyScale", StringComparison.OrdinalIgnoreCase) >= 0
                   || text.IndexOf("Pairs with Smothering", StringComparison.OrdinalIgnoreCase) >= 0
                   || text.IndexOf("Optional WAVs", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool ContainsAny(string text, string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
            {
                if (!string.IsNullOrEmpty(needles[i])
                    && text.IndexOf(needles[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsAnyNamedPoseActive(string[] exactNames, string[] nameContains, string[] nameExcludes = null)
        {
            if (exactNames != null)
            {
                for (int i = 0; i < exactNames.Length; i++)
                {
                    var hook = GameObject.Find(exactNames[i]);
                    if (hook != null && hook.activeInHierarchy)
                    {
                        return true;
                    }
                }
            }

            var special = GameObject.Find("SpecialAnimations");
            if (special == null || nameContains == null)
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
                if (nameExcludes != null)
                {
                    bool excluded = false;
                    for (int e = 0; e < nameExcludes.Length; e++)
                    {
                        if (n.IndexOf(nameExcludes[e], StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            excluded = true;
                            break;
                        }
                    }
                    if (excluded) continue;
                }

                for (int c = 0; c < nameContains.Length; c++)
                {
                    if (n.IndexOf(nameContains[c], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static Transform FindBone(string namePart)
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

        public static Transform FindBoneExact(string exactName)
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

        public static SkinnedMeshRenderer FindFaceRenderer()
        {
            GameObject faceGo = GameObject.Find("Face")
                ?? GameObject.Find("GirlCharacter/Face");
            if (faceGo != null)
            {
                var smr = faceGo.GetComponent<SkinnedMeshRenderer>()
                    ?? faceGo.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (smr != null) return smr;
            }

            var renderers = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || r.sharedMesh == null) continue;
                string n = r.name;
                if (n.IndexOf("Face", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return r;
                }
            }

            return null;
        }

        public static int FindBlendShape(SkinnedMeshRenderer smr, params string[] names)
        {
            return FindBlendShapePrefer(smr, names);
        }

        /// <summary>
        /// Prefer exact / suffix matches. Fuzzy needles shorter than 3 chars are ignored
        /// (bare "A" used to match Angry at index 0).
        /// </summary>
        public static int FindBlendShapePrefer(SkinnedMeshRenderer smr, params string[] names)
        {
            if (smr == null || smr.sharedMesh == null || names == null)
            {
                return -1;
            }

            var mesh = smr.sharedMesh;

            for (int i = 0; i < names.Length; i++)
            {
                if (string.IsNullOrEmpty(names[i])) continue;
                int idx = mesh.GetBlendShapeIndex(names[i]);
                if (idx >= 0) return idx;
            }

            // Prefer shapes that end with the needle (Fcl_MTH_A).
            for (int i = 0; i < names.Length; i++)
            {
                string needle = names[i];
                if (string.IsNullOrEmpty(needle) || needle.Length < 3) continue;
                for (int s = 0; s < mesh.blendShapeCount; s++)
                {
                    string shape = mesh.GetBlendShapeName(s);
                    if (shape.EndsWith(needle, StringComparison.OrdinalIgnoreCase)
                        || shape.EndsWith("." + needle, StringComparison.OrdinalIgnoreCase))
                    {
                        return s;
                    }
                }
            }

            // Fuzzy contains — skip tiny needles.
            for (int i = 0; i < names.Length; i++)
            {
                string needle = names[i];
                if (string.IsNullOrEmpty(needle) || needle.Length < 3) continue;
                for (int s = 0; s < mesh.blendShapeCount; s++)
                {
                    string shape = mesh.GetBlendShapeName(s);
                    if (shape.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return s;
                    }
                }
            }

            return -1;
        }

        public static Material MakeParticleMaterial()
        {
            var shader = Shader.Find("Particles/Standard Unlit")
                ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended")
                ?? Shader.Find("Sprites/Default");
            return new Material(shader);
        }

        // --- KeepCameraClear helpers (shared by fart / burp / smell / skunk / pee / scat) ---

        public static Color ClarityColor(Color c)
        {
            if (!FartEffects.KeepCameraClear) return c;
            if (c.a > FartEffects.MaxCloudOpacity) c.a = FartEffects.MaxCloudOpacity;
            return c;
        }

        /// <summary>Liquids stay a bit more opaque than gas, but still won't wall off the lens.</summary>
        public static Color ClarityLiquid(Color c)
        {
            if (!FartEffects.KeepCameraClear) return c;
            float maxA = Mathf.Clamp(FartEffects.MaxCloudOpacity * 2.2f, 0.25f, 0.55f);
            if (c.a > maxA) c.a = maxA;
            return c;
        }

        public static float ClaritySizeMul() => FartEffects.KeepCameraClear ? 0.7f : 1f;

        public static float ClarityCamDistance(float preferred)
        {
            return FartEffects.KeepCameraClear ? Mathf.Max(preferred, 0.48f) : preferred;
        }

        public static int ClarityCount(int n)
        {
            if (!FartEffects.KeepCameraClear) return n;
            return Mathf.Max(3, Mathf.RoundToInt(n * 0.6f));
        }
    }
}
