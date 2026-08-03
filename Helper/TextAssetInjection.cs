using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;

namespace VSFartMod
{
    /// <summary>
    /// Swaps vanilla CSV TextAssets for modded ones when PlayMaker reads them,
    /// so Farting tasks/toggles are actually in the game's runtime tables.
    /// </summary>
    public static class TextAssetInjection
    {
        private static Harmony _harmony;
        private static bool _patched;
        private static FieldInfo _textAssetField;
        private static FieldInfo _contentField;
        private static MethodInfo _finishMethod;

        public static void Apply()
        {
            if (_patched)
            {
                return;
            }

            try
            {
                _harmony = new Harmony("VSFartMod.TextAssetInjection");
                var type = AccessTools.TypeByName("HutongGames.PlayMaker.Actions.ReadTextAsset");
                if (type == null)
                {
                    VSFartMod.Logger.LogError("ReadTextAsset type not found — cannot inject Farting CSV data.");
                    return;
                }

                _textAssetField = AccessTools.Field(type, "textAsset");
                _contentField = AccessTools.Field(type, "content");
                _finishMethod = AccessTools.Method(type, "Finish")
                    ?? AccessTools.Method(typeof(FsmStateAction), "Finish");

                var onEnter = AccessTools.Method(type, "OnEnter");
                if (onEnter == null || _textAssetField == null || _contentField == null)
                {
                    VSFartMod.Logger.LogError("ReadTextAsset.OnEnter / fields missing — injection aborted.");
                    return;
                }

                _harmony.Patch(onEnter, prefix: new HarmonyMethod(typeof(TextAssetInjection), nameof(ReadTextAssetPrefix)));
                _patched = true;
                VSFartMod.Logger.LogInfo("Patched ReadTextAsset — Farting CSV injection active.");
            }
            catch (Exception ex)
            {
                VSFartMod.Logger.LogError($"TextAssetInjection failed: {ex}");
            }
        }

        // Harmony prefix: instance is the ReadTextAsset action.
        private static bool ReadTextAssetPrefix(object __instance)
        {
            try
            {
                var textAsset = _textAssetField.GetValue(__instance) as TextAsset;
                if (textAsset == null)
                {
                    return true;
                }

                // Hard block: never inject Timed New over Primary New (different CSV schema → ArgumentOutOfRangeException).
                if (textAsset.name.IndexOf("Primary", StringComparison.OrdinalIgnoreCase) >= 0
                    && textAsset.name.IndexOf("Tasklist", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                var replacement = Assets.GetReplacementTextAsset(textAsset.name);
                if (replacement == null || string.IsNullOrEmpty(replacement.text))
                {
                    return true;
                }

                // Safety: never inject a differently-named Tasklist CSV into another Tasklist slot.
                if (textAsset.name.IndexOf("Tasklist", StringComparison.OrdinalIgnoreCase) >= 0
                    && !string.Equals(replacement.name, textAsset.name, StringComparison.OrdinalIgnoreCase))
                {
                    VSFartMod.Logger.LogError(
                        $"BLOCKED wrong Tasklist inject: '{replacement.name}' must not overwrite '{textAsset.name}'.");
                    return true;
                }

                var contentObj = _contentField.GetValue(__instance);
                // Always strip CR — PlayMaker CSV logs "OUPPS found a \r" and can mis-parse rows.
                string clean = replacement.text.Replace("\r\n", "\n").Replace("\r", "\n");
                if (contentObj is FsmString fsmString)
                {
                    fsmString.Value = clean;
                }
                else if (contentObj != null)
                {
                    var valueProp = contentObj.GetType().GetProperty("Value");
                    valueProp?.SetValue(contentObj, clean, null);
                }

                VSFartMod.Logger.LogInfo(
                    $"Injected modded TextAsset '{replacement.name}' over '{textAsset.name}' ({clean.Length} chars).");

                _finishMethod?.Invoke(__instance, null);
                return false; // skip original
            }
            catch (Exception ex)
            {
                VSFartMod.Logger.LogError($"ReadTextAssetPrefix error: {ex}");
                return true;
            }
        }
    }
}
