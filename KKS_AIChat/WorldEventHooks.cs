using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KKS_AIChat
{
    public static class WorldEventHooks
    {
        public static Action OnTalkSceneStarted;
        public static Action OnTalkSceneEnded;
        public static Action<string, string> OnNativeDialogueCaptured;
        public static Action<string, int> OnTouchOrLookCaptured;
        public static Action<string, string, string> OnHeroineKeyMemoryEvent;

        private static Harmony harmonyInstance;
        private static string lastDialogueFingerprint = "";
        private static float lastDialogueTime = -10f;
        private static string lastTouchFingerprint = "";
        private static float lastTouchTime = -10f;

        private static readonly BindingFlags AllFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        public static void Install(string harmonyId)
        {
            if (harmonyInstance != null) return;

            try
            {
                harmonyInstance = new Harmony(harmonyId);
                PatchTalkSceneLifecycle();
                PatchNativeDialogues();
                PatchTouchAndLook();
                PatchHSceneEnd();
                PatchProbabilityCalculator();
                Debug.Log("[KKS AI Chat] WorldEventHooks instalados correctamente.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[KKS AI Chat] Error instalando WorldEventHooks: " + ex.Message);
            }
        }

        public static void Uninstall()
        {
            if (harmonyInstance != null)
            {
                try
                {
                    harmonyInstance.UnpatchSelf();
                }
                catch
                {
                }
                harmonyInstance = null;
            }
        }

        private static void PatchTalkSceneLifecycle()
        {
            try
            {
                Type talkType = typeof(TalkScene);
                MethodInfo startMethod = talkType.GetMethod("Start", AllFlags);
                if (startMethod != null)
                {
                    harmonyInstance.Patch(startMethod, null, new HarmonyMethod(typeof(WorldEventHooks), "TalkScene_Start_Postfix"));
                }

                MethodInfo endMethod = talkType.GetMethod("EndProc", AllFlags)
                    ?? talkType.GetMethod("OnDestroy", AllFlags);
                if (endMethod != null)
                {
                    harmonyInstance.Patch(endMethod, new HarmonyMethod(typeof(WorldEventHooks), "TalkScene_End_Prefix"), null);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[KKS AI Chat] Error parcheando ciclo de TalkScene: " + ex.Message);
            }
        }

        private static void TalkScene_Start_Postfix()
        {
            if (OnTalkSceneStarted != null) OnTalkSceneStarted();
        }

        private static void TalkScene_End_Prefix()
        {
            if (OnTalkSceneEnded != null) OnTalkSceneEnded();
        }

        private static void PatchNativeDialogues()
        {
            try
            {
                Type scenarioType = CharacterDataReader.FindTypeAcrossAssemblies("ADV.TextScenario");
                if (scenarioType == null) return;

                MethodInfo[] methods = scenarioType.GetMethods(AllFlags);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo m = methods[i];
                    if (m.Name != "MessageWindow" && m.Name != "SetText" && m.Name != "ChangeText") continue;

                    ParameterInfo[] p = m.GetParameters();
                    if (p.Length >= 2 && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(string))
                    {
                        harmonyInstance.Patch(m, null, new HarmonyMethod(typeof(WorldEventHooks), "NativeDialogue_Postfix"));
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[KKS AI Chat] Error parcheando diálogos nativos: " + ex.Message);
            }
        }

        private static void NativeDialogue_Postfix(object[] __args)
        {
            if (__args == null || __args.Length < 2) return;

            string speaker = __args[0] as string;
            string text = __args[1] as string;
            if (string.IsNullOrEmpty(text)) return;

            string cleanText = text.Trim();
            if (string.IsNullOrEmpty(cleanText)) return;

            string cleanSpeaker = string.IsNullOrEmpty(speaker) ? "Personaje" : speaker.Trim();
            string fp = cleanSpeaker + "|" + cleanText;
            if (fp == lastDialogueFingerprint && (Time.unscaledTime - lastDialogueTime) < 2.0f) return;

            lastDialogueFingerprint = fp;
            lastDialogueTime = Time.unscaledTime;

            if (OnNativeDialogueCaptured != null)
            {
                OnNativeDialogueCaptured(cleanSpeaker, cleanText);
            }
        }

        private static void PatchTouchAndLook()
        {
            try
            {
                Type talkType = typeof(TalkScene);
                MethodInfo[] methods = talkType.GetMethods(AllFlags);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo m = methods[i];
                    if (m.Name != "TouchFunc") continue;

                    ParameterInfo[] p = m.GetParameters();
                    if (p.Length >= 1 && p[0].ParameterType == typeof(string))
                    {
                        harmonyInstance.Patch(m, null, new HarmonyMethod(typeof(WorldEventHooks), "TouchFunc_Postfix"));
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[KKS AI Chat] Error parcheando TouchFunc: " + ex.Message);
            }
        }

        private static void TouchFunc_Postfix(object __instance, object[] __args)
        {
            if (__args == null || __args.Length == 0) return;

            string rawKind = __args[0] as string;
            if (string.IsNullOrEmpty(rawKind)) return;

            int touchMode = 0;
            object modeObj = CharacterDataReader.TryGetMemberValue(__instance, "touchMode")
                ?? CharacterDataReader.TryGetMemberValue(__instance, "m_TouchMode");
            if (modeObj is int) touchMode = (int)modeObj;

            string translated = TranslateTouchKind(rawKind);
            string fp = translated + "|" + touchMode;
            if (fp == lastTouchFingerprint && (Time.unscaledTime - lastTouchTime) < 1.2f) return;

            lastTouchFingerprint = fp;
            lastTouchTime = Time.unscaledTime;

            if (OnTouchOrLookCaptured != null)
            {
                OnTouchOrLookCaptured(translated, touchMode);
            }
        }

        private static void PatchHSceneEnd()
        {
            try
            {
                Type hProcType = CharacterDataReader.FindTypeAcrossAssemblies("HSceneProc");
                if (hProcType == null) return;

                MethodInfo endMethod = hProcType.GetMethod("EndProc", AllFlags);
                if (endMethod != null)
                {
                    harmonyInstance.Patch(endMethod, new HarmonyMethod(typeof(WorldEventHooks), "HScene_EndProc_Prefix"), null);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[KKS AI Chat] Error parcheando HSceneProc: " + ex.Message);
            }
        }

        private static void HScene_EndProc_Prefix(object __instance)
        {
            try
            {
                object flags = CharacterDataReader.TryGetMemberValue(__instance, "flags");
                object lstHeroine = CharacterDataReader.TryGetMemberValue(flags, "lstHeroine");
                System.Collections.IList list = lstHeroine as System.Collections.IList;
                if (list == null) return;

                for (int i = 0; i < list.Count; i++)
                {
                    SaveData.Heroine h = list[i] as SaveData.Heroine;
                    if (h == null) continue;

                    string uid = CharacterDataReader.GetHeroineUniqueId(h);
                    string name = CharacterDataReader.GetHeroineCleanName(h);
                    if (OnHeroineKeyMemoryEvent != null)
                    {
                        OnHeroineKeyMemoryEvent(uid, name, "You shared an intimate moment (H scene) with the player.");
                    }
                }
            }
            catch
            {
            }
        }

        private static void PatchProbabilityCalculator()
        {
            try
            {
                Type probType = CharacterDataReader.FindTypeAcrossAssemblies("Illusion.Utils.ProbabilityCalclator")
                    ?? CharacterDataReader.FindTypeAcrossAssemblies("Illusion.Utils.ProbabilityCalculator");

                if (probType == null)
                {
                    Debug.LogWarning("[KKS AI Chat] No se encontró ProbabilityCalclator para el parche de invitaciones.");
                    return;
                }

                MethodInfo detectMethod = probType.GetMethod(
                    "DetectFromPercent",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(float) },
                    null
                );

                if (detectMethod != null && harmonyInstance != null)
                {
                    MethodInfo prefix = typeof(WorldEventHooks).GetMethod(
                        "DetectFromPercent_Prefix",
                        BindingFlags.NonPublic | BindingFlags.Static
                    );
                    harmonyInstance.Patch(detectMethod, new HarmonyMethod(prefix), null);
                    Debug.Log("[KKS AI Chat] Parche de aceptación de invitaciones instalado correctamente.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[KKS AI Chat] Error al parchear ProbabilityCalclator: " + ex.Message);
            }
        }

        private static bool DetectFromPercent_Prefix(ref bool __result)
        {
            if (TalkSceneController.IsForcingInvitationAccept())
            {
                __result = true;
                return false;
            }
            return true;
        }

        private static string TranslateTouchKind(string rawKind)
        {
            string lower = rawKind.Trim().ToLowerInvariant();
            bool en = TalkSceneController.UseEnglishUI;

            if (lower.Contains("head") || lower.Contains("atama")) return en ? "your head" : "la cabeza";
            if (lower.Contains("cheek") || lower.Contains("hoho")) return en ? "your cheek" : "la mejilla";
            if (lower.Contains("face") || lower.Contains("kao")) return en ? "your face" : "el rostro";
            if (lower.Contains("eye") || lower.Contains("me")) return en ? "your eyes" : "los ojos";
            if (lower.Contains("mouth") || lower.Contains("kuchi") || lower.Contains("lip")) return en ? "your lips" : "los labios";
            if (lower.Contains("hand") || lower.Contains("te")) return en ? "your hand" : "la mano";
            if (lower.Contains("arm") || lower.Contains("ude")) return en ? "your arm" : "el brazo";
            if (lower.Contains("shoulder") || lower.Contains("kata")) return en ? "your shoulder" : "el hombro";
            if (lower.Contains("mune") || lower.Contains("bust") || lower.Contains("chest") || lower.Contains("breast")) return en ? "your chest" : "el pecho";
            if (lower.Contains("waist") || lower.Contains("koshi") || lower.Contains("hara") || lower.Contains("belly")) return en ? "your waist" : "la cintura";
            if (lower.Contains("hip") || lower.Contains("siri") || lower.Contains("shiri") || lower.Contains("ass")) return en ? "your hips/butt" : "la cadera/trasero";
            if (lower.Contains("leg") || lower.Contains("ashi") || lower.Contains("thigh")) return en ? "your legs" : "las piernas";
            if (lower.Contains("crotch") || lower.Contains("kokan") || lower.Contains("groin")) return en ? "your intimate area" : "la zona íntima";

            return rawKind.Trim();
        }
    }
}