using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace KKS_AIChat
{
    public static class TalkSceneController
    {
        public static bool UseEnglishUI = true;

        private static readonly BindingFlags AllFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        // Referencias del cuadro de texto nativo clonado y del original para auto-ocultado
        private static RectTransform cachedNativeSourceWindow = null;
        private static GameObject nativeCanvasRoot = null;
        private static Component nativeNameTextComp = null;
        private static Component nativeMsgTextComp = null;
        private static bool nativeBoxAttempted = false;
        private static bool nativeBoxReady = false;

        // Estado actual del diálogo mostrado en pantalla
        private static bool isDialogueVisible = false;
        private static string currentSpeaker = "";
        private static string fullDialogueText = "";
        private static string currentEmotionKey = "neutral";
        private static int currentFavorDelta = 0;
        private static int currentLewdnessDelta = 0;
        private static float typewriterElapsed = 0f;
        private static float charsPerSecond = 45f;
        private static int lastShownCharCount = -1;

        // Invitación pendiente propuesta por la IA
        private static string pendingInvitationKey = "";
        private static Action<string> onAcceptInvitationCallback = null;
        private static Action<string> onRejectInvitationCallback = null;

        // Ventana temporal para forzar aceptación en el RNG del juego
        private static float forceAcceptUntilTime = -1f;

        // Estilos visuales de respaldo / barra de estado / invitación
        private static Texture2D fallbackBgTex;
        private static Texture2D badgeBgTex;
        private static Texture2D inviteBgTex;
        private static GUIStyle fallbackBoxStyle;
        private static GUIStyle fallbackNameStyle;
        private static GUIStyle fallbackTextStyle;
        private static GUIStyle badgeStyle;
        private static GUIStyle inviteBarStyle;
        private static GUIStyle closeBtnStyle;

        public static bool IsForcingInvitationAccept()
        {
            return Time.unscaledTime <= forceAcceptUntilTime;
        }

        public static List<string> GetAvailableInvitations(TalkScene talkScene, CharacterSnapshot snapshot)
        {
            List<string> list = new List<string>();
            if (talkScene == null) return list;

            Array buttonInfos = CharacterDataReader.TryGetMemberValue(talkScene, "buttonInfos") as Array;
            Array eventButtons = CharacterDataReader.TryGetMemberValue(talkScene, "buttonEventContents") as Array;
            Array r18Buttons = CharacterDataReader.TryGetMemberValue(talkScene, "buttonR18Contents") as Array;

            bool mainEventAvailable = IsMainButtonAvailable(buttonInfos, 3);
            bool mainR18Available = IsMainButtonAvailable(buttonInfos, 4);

            if (mainEventAvailable && eventButtons != null)
            {
                if (IsSubButtonAvailable(eventButtons, 0)) list.Add("sightseeing");
                if (IsSubButtonAvailable(eventButtons, 1)) list.Add("play");
                if (IsSubButtonAvailable(eventButtons, 2)) list.Add("experience");
                if (IsSubButtonAvailable(eventButtons, 3)) list.Add("eat");
                if (IsSubButtonAvailable(eventButtons, 4) && snapshot != null && !snapshot.IsFollowingPlayer) list.Add("follow");
                if (IsSubButtonAvailable(eventButtons, 5)) list.Add("date");
            }

            if (mainR18Available && r18Buttons != null)
            {
                if (IsSubButtonAvailable(r18Buttons, 0) && (snapshot == null || snapshot.IsHPossible)) list.Add("h_scene");
                if (IsSubButtonAvailable(r18Buttons, 1)) list.Add("room");
            }

            return list;
        }

        public static string GetInvitationPromptDescription(string key)
        {
            switch (key)
            {
                case "sightseeing": return "Go sightseeing or walking around the island together (trail, viewpoint, tour).";
                case "play": return "Go play or have fun together (beach, pool, entertainment).";
                case "experience": return "Try an activity or experience together (kayaking, water sports, excursion).";
                case "eat": return "Go eat or grab a drink together at a restaurant/cafe.";
                case "follow": return "Ask to walk together / follow the player around the island.";
                case "date": return "Formally invite the player on a date.";
                case "h_scene": return "Propose getting intimate (H scene) right now.";
                case "room": return "Invite the player to go alone to the room for privacy.";
                default: return "Activity together.";
            }
        }

        public static string GetInvitationDisplayLabel(string key)
        {
            if (UseEnglishUI)
            {
                switch (key)
                {
                    case "sightseeing": return "Go sightseeing together";
                    case "play": return "Go play together";
                    case "experience": return "Do an activity (kayaking / experience)";
                    case "eat": return "Go eat together";
                    case "follow": return "Walk together around the island";
                    case "date": return "Go on a date";
                    case "h_scene": return "Start H Scene now";
                    case "room": return "Go to the room";
                    default: return key;
                }
            }

            switch (key)
            {
                case "sightseeing": return "Ir de paseo / turismo juntos";
                case "play": return "Ir a jugar juntos";
                case "experience": return "Hacer una actividad (kayak / experiencia)";
                case "eat": return "Ir a comer juntos";
                case "follow": return "Que te acompañe por la isla";
                case "date": return "Ir a una cita";
                case "h_scene": return "Escena H ahora";
                case "room": return "Ir a la habitación";
                default: return key;
            }
        }

        public static void SetPendingInvitation(string invitationKey, Action<string> onAccept, Action<string> onReject)
        {
            if (string.IsNullOrEmpty(invitationKey) || invitationKey == "none")
            {
                ClearPendingInvitation();
                return;
            }

            pendingInvitationKey = invitationKey;
            onAcceptInvitationCallback = onAccept;
            onRejectInvitationCallback = onReject;
        }

        public static void ClearPendingInvitation()
        {
            pendingInvitationKey = "";
            onAcceptInvitationCallback = null;
            onRejectInvitationCallback = null;
        }

        public static bool HasPendingInvitation()
        {
            return !string.IsNullOrEmpty(pendingInvitationKey);
        }

        public static string GetPendingInvitationKey()
        {
            return pendingInvitationKey;
        }

        public static IEnumerator TriggerNativeInvitationCoroutine(string invitationKey, Action<bool, string> onCompleted)
        {
            TalkScene talkScene = CharacterDataReader.GetActiveTalkScene();
            if (talkScene == null)
            {
                if (onCompleted != null) onCompleted(false, UseEnglishUI ? "Not in an active conversation." : "No estás dentro de una conversación activa.");
                yield break;
            }

            int mainIndex;
            string arrayMemberName;
            int subIndex;
            if (!TryMapInvitationToButtons(invitationKey, out mainIndex, out arrayMemberName, out subIndex))
            {
                if (onCompleted != null) onCompleted(false, "Unknown invitation: " + invitationKey);
                yield break;
            }

            Array buttonInfos = CharacterDataReader.TryGetMemberValue(talkScene, "buttonInfos") as Array;
            Array subButtons = CharacterDataReader.TryGetMemberValue(talkScene, arrayMemberName) as Array;

            if (!IsMainButtonAvailable(buttonInfos, mainIndex) || !IsSubButtonAvailable(subButtons, subIndex))
            {
                if (onCompleted != null) onCompleted(false, UseEnglishUI ? "That option is no longer available right now." : "Esa opción ya no está disponible en este momento.");
                yield break;
            }

            HideDialogue();
            ClearPendingInvitation();

            object mainInfo = buttonInfos.GetValue(mainIndex);
            GameObject mainRoot = CharacterDataReader.TryGetMemberValue(mainInfo, "root") as GameObject;
            Component mainBtn = CharacterDataReader.TryGetMemberValue(mainInfo, "button") as Component;

            if (mainRoot != null && !mainRoot.activeSelf && mainBtn != null)
            {
                InvokeUnityButtonClick(mainBtn);
                yield return null;
                yield return null;
            }

            Component targetSubBtn = subButtons.GetValue(subIndex) as Component;
            bool invoked = false;
            if (targetSubBtn != null && targetSubBtn.gameObject.activeSelf && IsButtonInteractable(targetSubBtn))
            {
                forceAcceptUntilTime = Time.unscaledTime + 4.0f;
                invoked = InvokeUnityButtonClick(targetSubBtn);
            }

            if (onCompleted != null)
            {
                onCompleted(invoked, invoked ? "OK" : (UseEnglishUI ? "The game blocked this activity button." : "El juego bloqueó el botón de esta actividad."));
            }
        }

        private static bool TryMapInvitationToButtons(string key, out int mainIndex, out string arrayMemberName, out int subIndex)
        {
            mainIndex = -1;
            arrayMemberName = "";
            subIndex = -1;

            switch (key)
            {
                case "sightseeing":
                    mainIndex = 3; arrayMemberName = "buttonEventContents"; subIndex = 0; return true;
                case "play":
                    mainIndex = 3; arrayMemberName = "buttonEventContents"; subIndex = 1; return true;
                case "experience":
                    mainIndex = 3; arrayMemberName = "buttonEventContents"; subIndex = 2; return true;
                case "eat":
                    mainIndex = 3; arrayMemberName = "buttonEventContents"; subIndex = 3; return true;
                case "follow":
                    mainIndex = 3; arrayMemberName = "buttonEventContents"; subIndex = 4; return true;
                case "date":
                    mainIndex = 3; arrayMemberName = "buttonEventContents"; subIndex = 5; return true;
                case "h_scene":
                    mainIndex = 4; arrayMemberName = "buttonR18Contents"; subIndex = 0; return true;
                case "room":
                    mainIndex = 4; arrayMemberName = "buttonR18Contents"; subIndex = 1; return true;
                default:
                    return false;
            }
        }

        private static bool IsMainButtonAvailable(Array buttonInfos, int index)
        {
            if (buttonInfos == null || index < 0 || index >= buttonInfos.Length) return false;
            object info = buttonInfos.GetValue(index);
            if (info == null) return false;

            Component btn = CharacterDataReader.TryGetMemberValue(info, "button") as Component;
            if (btn == null || !btn.gameObject.activeSelf) return false;

            return IsButtonInteractable(btn);
        }

        private static bool IsSubButtonAvailable(Array subButtons, int index)
        {
            if (subButtons == null || index < 0 || index >= subButtons.Length) return false;
            Component btn = subButtons.GetValue(index) as Component;
            if (btn == null || !btn.gameObject.activeSelf) return false;

            return IsButtonInteractable(btn);
        }

        private static bool IsButtonInteractable(Component btn)
        {
            object interactable = CharacterDataReader.TryGetMemberValue(btn, "interactable");
            return !(interactable is bool) || (bool)interactable;
        }

        private static bool InvokeUnityButtonClick(Component btnComp)
        {
            if (btnComp == null) return false;

            try
            {
                object onClickObj = CharacterDataReader.TryGetMemberValue(btnComp, "onClick");
                if (onClickObj == null) return false;

                MethodInfo invokeMethod = onClickObj.GetType().GetMethod(
                    "Invoke",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null
                );

                if (invokeMethod == null) return false;

                invokeMethod.Invoke(onClickObj, null);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[KKS AI Chat] Error clicking native button: " + ex.Message);
                return false;
            }
        }

        public static void ApplyReaction(
            string emotion,
            int favorDelta,
            int lewdnessDelta,
            out int actualFavorApplied,
            out int actualLewdnessApplied)
        {
            actualFavorApplied = 0;
            actualLewdnessApplied = 0;

            TalkScene talkScene = CharacterDataReader.GetActiveTalkScene();
            SaveData.Heroine heroine = CharacterDataReader.GetActiveHeroine();

            if (talkScene == null || heroine == null) return;

            string normalizedEmotion = string.IsNullOrEmpty(emotion) ? "neutral" : emotion.Trim().ToLowerInvariant();

            if (heroine.chaCtrl != null)
            {
                ApplyFacialExpression(heroine.chaCtrl, normalizedEmotion);
            }

            string poseClip = PickPoseForEmotion(normalizedEmotion);
            PlayNativePose(talkScene, heroine.chaCtrl, poseClip);

            if (favorDelta != 0)
            {
                actualFavorApplied = ApplyIntStatChange(heroine, "favor", favorDelta, 0, 100);
            }

            if (lewdnessDelta != 0)
            {
                actualLewdnessApplied = ApplyIntStatChange(heroine, "lewdness", lewdnessDelta, 0, 100);
            }

            if (actualFavorApplied != 0 || actualLewdnessApplied != 0)
            {
                InvokeSafe(talkScene, "UpdateUI", true);
            }
        }

        public static void ShowDialogue(
            string speakerName,
            string dialogueText,
            string emotion,
            int favorDelta,
            int lewdnessDelta,
            bool useNativeBox,
            float textSpeed)
        {
            currentSpeaker = string.IsNullOrEmpty(speakerName) ? "" : speakerName;
            fullDialogueText = string.IsNullOrEmpty(dialogueText) ? "" : dialogueText;
            currentEmotionKey = string.IsNullOrEmpty(emotion) ? "neutral" : emotion;
            currentFavorDelta = favorDelta;
            currentLewdnessDelta = lewdnessDelta;
            charsPerSecond = Mathf.Clamp(textSpeed, 15f, 150f);
            typewriterElapsed = 0f;
            lastShownCharCount = -1;
            isDialogueVisible = true;

            if (cachedNativeSourceWindow == null)
            {
                cachedNativeSourceWindow = FindNativeMessageWindow();
            }

            if (useNativeBox)
            {
                if (!nativeBoxAttempted || nativeCanvasRoot == null)
                {
                    TryBuildClonedNativeBox();
                }

                if (nativeBoxReady && nativeCanvasRoot != null)
                {
                    SetTextComponentString(nativeNameTextComp, currentSpeaker);
                    SetTextComponentString(nativeMsgTextComp, "");
                    nativeCanvasRoot.SetActive(true);
                }
            }
            else if (nativeCanvasRoot != null)
            {
                nativeCanvasRoot.SetActive(false);
            }
        }

        public static void HideDialogue()
        {
            isDialogueVisible = false;
            if (nativeCanvasRoot != null)
            {
                nativeCanvasRoot.SetActive(false);
            }
        }

        public static void Update(bool isInTalkScene, bool useNativeBox)
        {
            if (!isInTalkScene)
            {
                if (isDialogueVisible) HideDialogue();
                if (HasPendingInvitation()) ClearPendingInvitation();
                return;
            }

            if (cachedNativeSourceWindow != null && cachedNativeSourceWindow.gameObject.activeInHierarchy)
            {
                if (isDialogueVisible) HideDialogue();
                return;
            }

            if (!isDialogueVisible) return;

            typewriterElapsed += Time.unscaledDeltaTime;
            int targetChars = Mathf.Clamp(Mathf.FloorToInt(typewriterElapsed * charsPerSecond), 0, fullDialogueText.Length);

            if (useNativeBox && nativeBoxReady && nativeCanvasRoot != null)
            {
                if (!nativeCanvasRoot.activeSelf)
                {
                    nativeCanvasRoot.SetActive(true);
                }

                if (targetChars != lastShownCharCount)
                {
                    lastShownCharCount = targetChars;
                    SetTextComponentString(nativeMsgTextComp, fullDialogueText.Substring(0, targetChars));
                }
            }
            else if (nativeCanvasRoot != null && nativeCanvasRoot.activeSelf)
            {
                nativeCanvasRoot.SetActive(false);
            }
        }

        public static void OnGUI(bool useNativeBox)
        {
            if (!isDialogueVisible && !HasPendingInvitation())
            {
                return;
            }

            InitFallbackStyles();

            bool usingNativeVisual = useNativeBox && nativeBoxReady && nativeCanvasRoot != null && nativeCanvasRoot.activeSelf;

            if (isDialogueVisible)
            {
                int visibleChars = Mathf.Clamp(Mathf.FloorToInt(typewriterElapsed * charsPerSecond), 0, fullDialogueText.Length);
                string visibleText = fullDialogueText.Substring(0, visibleChars);

                if (usingNativeVisual)
                {
                    DrawCompactControlStrip();
                }
                else
                {
                    DrawFallbackDialogBox(visibleText);
                }
            }

            if (HasPendingInvitation())
            {
                DrawInvitationBar(usingNativeVisual);
            }
        }

        public static void Cleanup()
        {
            ClearPendingInvitation();
            forceAcceptUntilTime = -1f;
            if (nativeCanvasRoot != null)
            {
                UnityEngine.Object.Destroy(nativeCanvasRoot);
                nativeCanvasRoot = null;
            }
            cachedNativeSourceWindow = null;
            nativeNameTextComp = null;
            nativeMsgTextComp = null;
            nativeBoxAttempted = false;
            nativeBoxReady = false;

            if (fallbackBgTex != null) UnityEngine.Object.Destroy(fallbackBgTex);
            if (badgeBgTex != null) UnityEngine.Object.Destroy(badgeBgTex);
            if (inviteBgTex != null) UnityEngine.Object.Destroy(inviteBgTex);
        }

        public static string TranslateEmotionLabel(string emotion)
        {
            string e = string.IsNullOrEmpty(emotion) ? "neutral" : emotion.Trim().ToLowerInvariant();

            if (UseEnglishUI)
            {
                switch (e)
                {
                    case "happy": return "Happy";
                    case "excited": return "Excited";
                    case "shy": return "Blushing / Shy";
                    case "flirty": return "Flirty";
                    case "surprised": return "Surprised";
                    case "angry": return "Angry";
                    case "sad": return "Sad";
                    case "serious": return "Serious";
                    default: return "Neutral";
                }
            }

            switch (e)
            {
                case "happy": return "Feliz";
                case "excited": return "Emocionada";
                case "shy": return "Sonrojada";
                case "flirty": return "Coqueta";
                case "surprised": return "Sorprendida";
                case "angry": return "Molesta";
                case "sad": return "Triste";
                case "serious": return "Seria";
                default: return "Neutral";
            }
        }

        private static void ApplyFacialExpression(ChaControl chaCtrl, string emotion)
        {
            int eyebrow = 0;
            int eyes = 0;
            int mouth = 0;
            float blush = 0f;
            float tears = 0f;

            switch (emotion)
            {
                case "happy":
                    eyebrow = 3; eyes = 2; mouth = 2; blush = 0.20f; break;
                case "excited":
                    eyebrow = 3; eyes = 4; mouth = 3; blush = 0.30f; break;
                case "shy":
                    eyebrow = 2; eyes = 8; mouth = 8; blush = 0.75f; break;
                case "flirty":
                    eyebrow = 0; eyes = 12; mouth = 7; blush = 0.45f; break;
                case "surprised":
                    eyebrow = 3; eyes = 11; mouth = 4; blush = 0.15f; break;
                case "angry":
                    eyebrow = 1; eyes = 9; mouth = 5; blush = 0.15f; break;
                case "sad":
                    eyebrow = 2; eyes = 7; mouth = 6; tears = 0.35f; break;
                case "serious":
                    eyebrow = 4; eyes = 10; mouth = 0; break;
                default:
                    eyebrow = 0; eyes = 0; mouth = 1; break;
            }

            InvokeSafe(chaCtrl, "ChangeEyebrowPtn", eyebrow, true);
            InvokeSafe(chaCtrl, "ChangeEyesPtn", eyes, true);
            InvokeSafe(chaCtrl, "ChangeMouthPtn", mouth, true);
            InvokeSafe(chaCtrl, "ChangeHohoAkaRate", blush);
            InvokeSafe(chaCtrl, "ChangeTearsRate", tears);
        }

        private static string PickPoseForEmotion(string emotion)
        {
            string[] candidates;

            switch (emotion)
            {
                case "happy":
                    candidates = new string[] { "Stand_Joy_00", "Stand_Joy_01", "Stand_Joy_02", "Stand_Joy_03", "Stand_Joy_04" };
                    break;
                case "excited":
                    candidates = new string[] { "Stand_Joy_05", "Stand_Joy_06", "Stand_Joy_07", "Stand_Joy_08", "Stand_Joy_09" };
                    break;
                case "angry":
                    candidates = new string[] { "Stand_Ang_00", "Stand_Ang_01", "Stand_Ang_02", "Stand_Ang_03", "Stand_Ang_04" };
                    break;
                case "shy":
                    candidates = new string[] { "Stand_Oth_00", "Stand_Oth_01", "Stand_04_00", "Stand_05_00" };
                    break;
                case "flirty":
                    candidates = new string[] { "Stand_Joy_02", "Stand_06_00", "Stand_07_00", "Stand_08_00" };
                    break;
                case "surprised":
                    candidates = new string[] { "f_reaction_00_00_M", "f_reaction_00_01_M", "Stand_Oth_02" };
                    break;
                case "sad":
                    candidates = new string[] { "Stand_Oth_03", "Stand_Oth_04", "Stand_Oth_05" };
                    break;
                case "serious":
                    candidates = new string[] { "Stand_01_00", "Stand_02_00", "Stand_03_00" };
                    break;
                default:
                    candidates = new string[] { "Idle_00_00", "Stand_00_00", "Stand_01_00" };
                    break;
            }

            int idx = UnityEngine.Random.Range(0, candidates.Length);
            return candidates[idx];
        }

        private static void PlayNativePose(TalkScene talkScene, ChaControl chaCtrl, string poseName)
        {
            if (string.IsNullOrEmpty(poseName)) return;

            bool played = false;
            try
            {
                MethodInfo animePlay = talkScene.GetType().GetMethod(
                    "AnimePlay",
                    AllFlags,
                    null,
                    new Type[] { typeof(string), typeof(bool) },
                    null
                );

                if (animePlay != null)
                {
                    animePlay.Invoke(talkScene, new object[] { poseName, false });
                    played = true;
                }
            }
            catch
            {
            }

            if (!played && chaCtrl != null)
            {
                object animBody = CharacterDataReader.TryGetMemberValue(chaCtrl, "animBody");
                if (animBody != null)
                {
                    InvokeSafe(animBody, "CrossFadeInFixedTime", poseName, 0.25f);
                }
            }
        }

        private static int ApplyIntStatChange(SaveData.Heroine heroine, string memberName, int delta, int minVal, int maxVal)
        {
            if (heroine == null || delta == 0) return 0;

            object currentObj = CharacterDataReader.TryGetMemberValue(heroine, memberName);
            if (!(currentObj is int)) return 0;

            int oldVal = (int)currentObj;
            int clampedDelta = Mathf.Clamp(delta, -50, 50);
            int newVal = Mathf.Clamp(oldVal + clampedDelta, minVal, maxVal);
            int realDiff = newVal - oldVal;

            if (realDiff == 0) return 0;

            bool updated = false;
            try
            {
                Type t = heroine.GetType();
                PropertyInfo prop = t.GetProperty(memberName, AllFlags);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(heroine, newVal, null);
                    updated = true;
                }
                else
                {
                    FieldInfo field = t.GetField(memberName, AllFlags) ?? t.GetField("<" + memberName + ">k__BackingField", AllFlags);
                    if (field != null)
                    {
                        field.SetValue(heroine, newVal);
                        updated = true;
                    }
                }
            }
            catch
            {
            }

            return updated ? realDiff : 0;
        }

        private static void TryBuildClonedNativeBox()
        {
            nativeBoxAttempted = true;
            nativeBoxReady = false;

            try
            {
                RectTransform sourceWindow = cachedNativeSourceWindow != null ? cachedNativeSourceWindow : FindNativeMessageWindow();
                cachedNativeSourceWindow = sourceWindow;
                if (sourceWindow == null) return;

                Type canvasType = CharacterDataReader.FindTypeAcrossAssemblies("UnityEngine.Canvas");
                Type scalerType = CharacterDataReader.FindTypeAcrossAssemblies("UnityEngine.UI.CanvasScaler");
                Type canvasGroupType = CharacterDataReader.FindTypeAcrossAssemblies("UnityEngine.CanvasGroup");

                if (canvasType == null) return;

                if (nativeCanvasRoot != null) UnityEngine.Object.Destroy(nativeCanvasRoot);

                nativeCanvasRoot = new GameObject("KKS_AIChat_NativeDialogCanvas");
                UnityEngine.Object.DontDestroyOnLoad(nativeCanvasRoot);

                Component canvas = nativeCanvasRoot.AddComponent(canvasType);
                SetMemberEnumOrValue(canvas, "renderMode", 0);
                SetMemberEnumOrValue(canvas, "sortingOrder", 25);

                if (scalerType != null)
                {
                    Component scaler = nativeCanvasRoot.AddComponent(scalerType);
                    SetMemberEnumOrValue(scaler, "uiScaleMode", 1);
                    SetMemberEnumOrValue(scaler, "referenceResolution", new Vector2(1920f, 1080f));
                    SetMemberEnumOrValue(scaler, "screenMatchMode", 0);
                    SetMemberEnumOrValue(scaler, "matchWidthOrHeight", 0.5f);
                }

                GameObject clone = (GameObject)UnityEngine.Object.Instantiate(sourceWindow.gameObject);
                clone.name = "KKS_AIChat_ClonedMessageWindow";
                clone.transform.SetParent(nativeCanvasRoot.transform, false);
                clone.SetActive(true);

                MonoBehaviour[] behaviors = clone.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < behaviors.Length; i++)
                {
                    MonoBehaviour mb = behaviors[i];
                    if (mb == null) continue;

                    Type t = mb.GetType();
                    string ns = t.Namespace ?? "";

                    if (t.FullName == "UnityEngine.UI.Button")
                    {
                        if (mb.gameObject != clone) mb.gameObject.SetActive(false);
                        else mb.enabled = false;
                    }
                    else if (ns.StartsWith("ADV"))
                    {
                        mb.enabled = false;
                    }
                }

                if (canvasGroupType != null)
                {
                    Component cg = clone.GetComponent(canvasGroupType);
                    if (cg == null) cg = clone.AddComponent(canvasGroupType);
                    SetMemberEnumOrValue(cg, "alpha", 1f);
                    SetMemberEnumOrValue(cg, "blocksRaycasts", false);
                    SetMemberEnumOrValue(cg, "interactable", false);
                }

                List<Component> textComps = new List<Component>();
                Component[] allComps = clone.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < allComps.Length; i++)
                {
                    Component c = allComps[i];
                    if (c == null || !c.gameObject.activeInHierarchy) continue;

                    string fullName = c.GetType().FullName;
                    if (fullName == "UnityEngine.UI.Text" || fullName == "TMPro.TextMeshProUGUI")
                    {
                        textComps.Add(c);
                    }
                }

                nativeNameTextComp = null;
                nativeMsgTextComp = null;

                for (int i = 0; i < textComps.Count; i++)
                {
                    Component c = textComps[i];
                    string objName = c.gameObject.name.ToLowerInvariant();

                    if (nativeNameTextComp == null && objName.Contains("name"))
                    {
                        nativeNameTextComp = c;
                    }
                    else if (nativeMsgTextComp == null && (objName.Contains("msg") || objName.Contains("message") || objName.Contains("text") || objName.Contains("serif")))
                    {
                        nativeMsgTextComp = c;
                    }
                }

                if ((nativeNameTextComp == null || nativeMsgTextComp == null) && textComps.Count >= 2)
                {
                    Component tallest = textComps[0];
                    Component shortest = textComps[0];
                    float maxH = GetRectHeight(tallest);
                    float minH = maxH;

                    for (int i = 1; i < textComps.Count; i++)
                    {
                        float h = GetRectHeight(textComps[i]);
                        if (h > maxH) { maxH = h; tallest = textComps[i]; }
                        if (h < minH) { minH = h; shortest = textComps[i]; }
                    }

                    if (nativeMsgTextComp == null) nativeMsgTextComp = tallest;
                    if (nativeNameTextComp == null && shortest != nativeMsgTextComp) nativeNameTextComp = shortest;
                }
                else if (nativeMsgTextComp == null && textComps.Count == 1)
                {
                    nativeMsgTextComp = textComps[0];
                }

                if (nativeMsgTextComp != null)
                {
                    nativeBoxReady = true;
                    nativeCanvasRoot.SetActive(false);
                }
                else
                {
                    UnityEngine.Object.Destroy(nativeCanvasRoot);
                    nativeCanvasRoot = null;
                }
            }
            catch
            {
                if (nativeCanvasRoot != null)
                {
                    UnityEngine.Object.Destroy(nativeCanvasRoot);
                    nativeCanvasRoot = null;
                }
                nativeBoxReady = false;
            }
        }

        private static RectTransform FindNativeMessageWindow()
        {
            MonoBehaviour[] allMonos = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
            RectTransform fallbackCandidate = null;

            for (int i = 0; i < allMonos.Length; i++)
            {
                MonoBehaviour mb = allMonos[i];
                if (mb == null) continue;

                string fullName = mb.GetType().FullName;
                if (fullName == "ADV.MainScenario")
                {
                    RectTransform rt = ExtractMessageWindowRect(mb);
                    if (rt != null) return rt;
                }
                else if (fullName == "ADV.TextScenario" && fallbackCandidate == null)
                {
                    RectTransform rt = ExtractMessageWindowRect(mb);
                    if (rt != null) fallbackCandidate = rt;
                }
            }

            return fallbackCandidate;
        }

        private static RectTransform ExtractMessageWindowRect(object scenarioObj)
        {
            object val = CharacterDataReader.TryGetMemberValue(scenarioObj, "MessageWindow");
            if (val == null)
            {
                val = CharacterDataReader.TryGetMemberValue(scenarioObj, "messageWindow");
            }

            return val as RectTransform;
        }

        private static float GetRectHeight(Component comp)
        {
            RectTransform rt = comp.GetComponent<RectTransform>();
            return rt != null ? rt.rect.height : 0f;
        }

        private static void SetTextComponentString(Component textComp, string value)
        {
            if (textComp == null) return;

            try
            {
                PropertyInfo prop = textComp.GetType().GetProperty("text", AllFlags);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(textComp, value ?? "", null);
                }
            }
            catch
            {
            }
        }

        private static void SetMemberEnumOrValue(object target, string memberName, object value)
        {
            if (target == null) return;

            Type t = target.GetType();
            PropertyInfo prop = t.GetProperty(memberName, AllFlags);
            if (prop != null && prop.CanWrite)
            {
                try
                {
                    object converted = prop.PropertyType.IsEnum ? Enum.ToObject(prop.PropertyType, value) : value;
                    prop.SetValue(target, converted, null);
                }
                catch
                {
                }
            }
        }

        private static void InvokeSafe(object target, string methodName, params object[] args)
        {
            if (target == null) return;

            Type t = target.GetType();
            MethodInfo[] methods = t.GetMethods(AllFlags);

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];
                if (m.Name != methodName) continue;

                ParameterInfo[] parameters = m.GetParameters();
                if (parameters.Length == args.Length)
                {
                    bool compatible = true;
                    for (int p = 0; p < parameters.Length; p++)
                    {
                        if (args[p] != null && !parameters[p].ParameterType.IsAssignableFrom(args[p].GetType()))
                        {
                            compatible = false;
                            break;
                        }
                    }

                    if (compatible)
                    {
                        try { m.Invoke(target, args); }
                        catch { }
                        return;
                    }
                }
            }
        }

        private static void InitFallbackStyles()
        {
            if (fallbackBoxStyle != null) return;

            fallbackBgTex = MakeSolidTexture(new Color(0.11f, 0.36f, 0.56f, 0.88f));
            badgeBgTex = MakeSolidTexture(new Color(0.08f, 0.14f, 0.24f, 0.90f));
            inviteBgTex = MakeSolidTexture(new Color(0.12f, 0.22f, 0.36f, 0.95f));

            fallbackBoxStyle = new GUIStyle(GUI.skin.box);
            fallbackBoxStyle.normal.background = fallbackBgTex;
            fallbackBoxStyle.padding = new RectOffset(20, 20, 14, 14);

            fallbackNameStyle = new GUIStyle(GUI.skin.label);
            fallbackNameStyle.fontSize = 18;
            fallbackNameStyle.fontStyle = FontStyle.Bold;
            fallbackNameStyle.normal.textColor = new Color(1f, 0.96f, 0.78f, 1f);

            fallbackTextStyle = new GUIStyle(GUI.skin.label);
            fallbackTextStyle.fontSize = 17;
            fallbackTextStyle.wordWrap = true;
            fallbackTextStyle.normal.textColor = Color.white;

            badgeStyle = new GUIStyle(GUI.skin.box);
            badgeStyle.normal.background = badgeBgTex;
            badgeStyle.richText = true;
            badgeStyle.fontSize = 12;
            badgeStyle.alignment = TextAnchor.MiddleCenter;
            badgeStyle.normal.textColor = Color.white;

            inviteBarStyle = new GUIStyle(GUI.skin.box);
            inviteBarStyle.normal.background = inviteBgTex;
            inviteBarStyle.richText = true;
            inviteBarStyle.fontSize = 13;
            inviteBarStyle.alignment = TextAnchor.MiddleLeft;
            inviteBarStyle.padding = new RectOffset(12, 12, 6, 6);
            inviteBarStyle.normal.textColor = Color.white;

            closeBtnStyle = new GUIStyle(GUI.skin.button);
            closeBtnStyle.fontStyle = FontStyle.Bold;
        }

        private static Texture2D MakeSolidTexture(Color color)
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        private static string BuildStatusBadgeText()
        {
            string emoPrefix = UseEnglishUI ? "<b>Emotion:</b> " : "<b>Emoción:</b> ";
            string favorWord = UseEnglishUI ? " Favor" : " Afinidad";

            string badge = emoPrefix + TranslateEmotionLabel(currentEmotionKey);
            if (currentFavorDelta > 0)
            {
                badge += "  |  <color=#7CFC00><b>+" + currentFavorDelta + favorWord + "</b></color>";
            }
            else if (currentFavorDelta < 0)
            {
                badge += "  |  <color=#FF6B6B><b>" + currentFavorDelta + favorWord + "</b></color>";
            }

            if (currentLewdnessDelta > 0)
            {
                badge += "  |  <color=#FF79C6><b>+" + currentLewdnessDelta + " H</b></color>";
            }
            else if (currentLewdnessDelta < 0)
            {
                badge += "  |  <color=#FF6B6B><b>" + currentLewdnessDelta + " H</b></color>";
            }

            return badge;
        }

        private static void DrawCompactControlStrip()
        {
            float stripWidth = 390f;
            float stripHeight = 26f;
            float x = (Screen.width - stripWidth) * 0.5f;
            float y = Screen.height - 235f;
            if (y < 10f) y = 10f;

            GUI.Box(new Rect(x, y, stripWidth - 34f, stripHeight), BuildStatusBadgeText(), badgeStyle);

            if (GUI.Button(new Rect(x + stripWidth - 30f, y, 30f, stripHeight), "X", closeBtnStyle))
            {
                HideDialogue();
            }
        }

        private static void DrawFallbackDialogBox(string visibleText)
        {
            float boxWidth = Mathf.Min(Screen.width * 0.72f, 920f);
            float boxHeight = 155f;
            float x = (Screen.width - boxWidth) * 0.5f;
            float y = Screen.height - boxHeight - 28f;

            GUI.Box(new Rect(x, y, boxWidth, boxHeight), "", fallbackBoxStyle);

            GUI.Label(new Rect(x + 22f, y + 10f, boxWidth - 340f, 28f), currentSpeaker, fallbackNameStyle);
            GUI.Box(new Rect(x + boxWidth - 335f, y + 10f, 290f, 24f), BuildStatusBadgeText(), badgeStyle);

            if (GUI.Button(new Rect(x + boxWidth - 38f, y + 10f, 26f, 24f), "X", closeBtnStyle))
            {
                HideDialogue();
            }

            GUI.Label(new Rect(x + 22f, y + 46f, boxWidth - 44f, boxHeight - 56f), visibleText, fallbackTextStyle);
        }

        private static void DrawInvitationBar(bool usingNativeVisual)
        {
            string key = pendingInvitationKey;
            if (string.IsNullOrEmpty(key)) return;

            float barWidth = 560f;
            float barHeight = 40f;
            float x = (Screen.width - barWidth) * 0.5f;
            float y = usingNativeVisual ? (Screen.height - 282f) : (Screen.height - 232f);
            if (y < 10f) y = 10f;

            string prefix = UseEnglishUI ? "<color=#FFD166><b>Proposal:</b></color> " : "<color=#FFD166><b>Propuesta:</b></color> ";
            string acceptLabel = UseEnglishUI ? "Accept" : "Aceptar";
            string rejectLabel = UseEnglishUI ? "Reject" : "Rechazar";

            GUI.Box(new Rect(x, y, barWidth, barHeight), prefix + GetInvitationDisplayLabel(key), inviteBarStyle);

            if (GUI.Button(new Rect(x + barWidth - 190f, y + 6f, 90f, 28f), acceptLabel))
            {
                Action<string> cb = onAcceptInvitationCallback;
                ClearPendingInvitation();
                if (cb != null) cb(key);
            }

            if (GUI.Button(new Rect(x + barWidth - 95f, y + 6f, 88f, 28f), rejectLabel))
            {
                Action<string> cb = onRejectInvitationCallback;
                ClearPendingInvitation();
                if (cb != null) cb(key);
            }
        }
    }
}