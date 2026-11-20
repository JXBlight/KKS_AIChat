using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace KKS_AIChat
{
    public enum UILanguage
    {
        English,
        Spanish
    }

    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.miusuario.kks.aichat";
        public const string PluginName = "KKS AI Chat";
        public const string PluginVersion = "1.1.0";

        // Configuración persistente en BepInEx\config\com.miusuario.kks.aichat.cfg
        private ConfigEntry<UILanguage> configUiLanguage;
        private ConfigEntry<bool> configVisualNovelMode;
        private ConfigEntry<ApiProvider> configProvider;
        private ConfigEntry<string> configApiKey;
        private ConfigEntry<string> configModel;
        private ConfigEntry<string> configCustomEndpointUrl;
        private ConfigEntry<string> configPlayerName;
        private ConfigEntry<float> configTemperature;
        private ConfigEntry<int> configMaxContextMessages;
        private ConfigEntry<bool> configAutoSummary;
        private ConfigEntry<int> configAutoSummaryInterval;
        private ConfigEntry<bool> configUseNativeDialogBox;
        private ConfigEntry<float> configTextSpeed;
        private ConfigEntry<KeyCode> configToggleKey;
        private ConfigEntry<bool> configAutoReactGestures;
        private ConfigEntry<bool> configAutoReactDialogues;
        private ConfigEntry<float> configAutoReactCooldown;
        private ConfigEntry<int> configHighHProposalThreshold;

        // Estado y posición de la ventana
        private bool showWindow = false;
        private Rect windowRect = new Rect(40, 40, 520, 560);
        private Vector2 scrollPosition = Vector2.zero;
        private Vector2 inputScrollPosition = Vector2.zero;

        // Entrada y estado de petición
        private string inputText = "";
        private bool isWaitingForReply = false;
        private bool isSummarizing = false;
        private string statusBanner = "";
        private string errorBanner = "";

        // Cola de reacciones automáticas y evento de Barra H alta
        private readonly List<string> pendingAutoTriggers = new List<string>();
        private float autoReactionDueTime = -1f;
        private float lastAutoReactionSentTime = -999f;
        private bool highHProposalTriggered = false;
        private bool pendingHighHAutoProposal = false;
        private bool forceBypassAutoCooldown = false;
        private int lastObservedLewdness = -1;

        // Caché en RAM de las memorias cargadas desde disco por personaje
        private readonly Dictionary<string, CharacterMemory> loadedMemories = new Dictionary<string, CharacterMemory>();

        // Estilos visuales oscuros
        private Texture2D windowBgTex;
        private Texture2D boxBgTex;
        private Texture2D inputBgTex;
        private Texture2D errorBgTex;
        private GUIStyle windowStyle;
        private GUIStyle boxStyle;
        private GUIStyle errorBoxStyle;
        private GUIStyle inputAreaStyle;
        private GUIStyle messageStyle;
        private GUIStyle infoStyle;

        // Estado de TalkScene y bloqueo durante el fade-out de salida
        private TalkScene cachedTalkScene = null;
        private bool isTalkExiting = false;
        private CharacterSnapshot currentCharacter = null;
        private string lastCharacterId = "";
        private float nextRefreshTime = 0f;

        private string T(string en, string es)
        {
            return configUiLanguage.Value == UILanguage.English ? en : es;
        }

        private void Awake()
        {
            configUiLanguage = Config.Bind(
                "Interfaz",
                "IdiomaInterfaz",
                UILanguage.English,
                "UI language (English / Spanish). Note: AI replies automatically match ANY language you type in. | Idioma de la interfaz."
            );

            configVisualNovelMode = Config.Bind(
                "Interfaz",
                "ModoVisualNovel",
                false,
                "Minimalist Visual Novel input bar instead of the full chat log window (can also be toggled in-game). | Barra minimalista estilo Novela Visual."
            );

            configProvider = Config.Bind(
                "Gemini",
                "Proveedor",
                ApiProvider.OpenRouter,
                "AI Provider: OpenRouter (free tier), Groq, Gemini, or LocalOrCustom (KoboldCpp/Ollama/LM Studio). | Proveedor de IA."
            );

            configApiKey = Config.Bind(
                "Gemini",
                "ApiKey",
                "",
                "API Key for your selected provider (leave blank for LocalOrCustom). | Tu API Key del proveedor elegido."
            );

            configModel = Config.Bind(
                "Gemini",
                "Model",
                "openrouter/free",
                "Model ID. Examples: OpenRouter='openrouter/free' | Groq='llama-3.3-70b-versatile' | Gemini='gemini-3-flash-preview'."
            );

            configCustomEndpointUrl = Config.Bind(
                "Gemini",
                "UrlPersonalizada",
                "http://localhost:5001/v1/chat/completions",
                "Only for LocalOrCustom. KoboldCpp: :5001 | Ollama: :11434 | LM Studio: :1234 (/v1/chat/completions)"
            );

            configPlayerName = Config.Bind(
                "Jugador",
                "NombreJugador",
                "Player",
                "Leave as 'Player'/'Jugador' to auto-read your in-game avatar name, or type a custom name. | Déjalo por defecto para leer tu nombre del juego."
            );

            configTemperature = Config.Bind(
                "Gemini",
                "Temperatura",
                0.75f,
                "Response creativity (recommended: 0.65 - 0.85). | Creatividad de las respuestas."
            );

            configMaxContextMessages = Config.Bind(
                "Gemini",
                "MensajesDeContexto",
                12,
                "Recent messages sent per turn (4 to 40). | Mensajes recientes enviados por turno."
            );

            configAutoSummary = Config.Bind(
                "Memoria",
                "AutoResumenMemoria",
                true,
                "Automatically summarize conversations per character into long-term memories. | Resumir charlas automáticamente en recuerdos a largo plazo."
            );

            configAutoSummaryInterval = Config.Bind(
                "Memoria",
                "IntervaloAutoResumen",
                10,
                "New messages per character before generating an automatic memory summary (6 - 30). | Mensajes nuevos antes de auto-resumir."
            );

            configUseNativeDialogBox = Config.Bind(
                "Interfaz",
                "UsarCajaNativa",
                true,
                "Display replies in a cloned native game dialog box at the bottom. | Mostrar respuestas en un clon de la caja nativa del juego."
            );

            configTextSpeed = Config.Bind(
                "Interfaz",
                "VelocidadTexto",
                45f,
                "Typewriter speed (characters per second). | Velocidad del texto (caracteres por segundo)."
            );

            configToggleKey = Config.Bind(
                "Interfaz",
                "TeclaChat",
                KeyCode.F8,
                "Hotkey to open/close chat inside TalkScene (auto-closes on exit). | Tecla para abrir/cerrar el chat."
            );

            configAutoReactGestures = Config.Bind(
                "Reacciones",
                "ReaccionAutoGestos",
                true,
                "Characters spontaneously react in chat when touched or looked at. | Reacción automática al tocar o mirar al personaje."
            );

            configAutoReactDialogues = Config.Bind(
                "Reacciones",
                "ReaccionAutoDialogos",
                true,
                "Characters comment in chat after native game dialogues. | Reacción automática tras diálogos nativos del juego."
            );

            configAutoReactCooldown = Config.Bind(
                "Reacciones",
                "CooldownReaccionAuto",
                12f,
                "Minimum seconds between automatic reactions to save API quota. | Segundos mínimos entre reacciones automáticas."
            );

            configHighHProposalThreshold = Config.Bind(
                "Reacciones",
                "UmbralPropuestaH",
                80,
                "H Gauge level (50-100) at which characters proactively ask to start an H scene or go to the room (set 101 to disable). | Nivel de Barra H en el que las chicas te propondrán iniciar una escena H (pon 101 para desactivar)."
            );

            TalkSceneController.UseEnglishUI = configUiLanguage.Value == UILanguage.English;

            WorldEventHooks.OnTalkSceneStarted = HandleTalkSceneStarted;
            WorldEventHooks.OnTalkSceneEnded = HandleTalkSceneEnded;
            WorldEventHooks.OnNativeDialogueCaptured = HandleNativeDialogueCaptured;
            WorldEventHooks.OnTouchOrLookCaptured = HandleTouchOrLookCaptured;
            WorldEventHooks.OnHeroineKeyMemoryEvent = HandleHeroineKeyMemoryEvent;
            WorldEventHooks.Install(PluginGUID + ".harmony");

            Logger.LogInfo("KKS AI Chat v1.1.0 loaded. Provider: " + configProvider.Value + " | Memories: " + MemoryManager.MemoriesFolder);
        }

        private void OnDestroy()
        {
            WorldEventHooks.OnTalkSceneStarted = null;
            WorldEventHooks.OnTalkSceneEnded = null;
            WorldEventHooks.OnNativeDialogueCaptured = null;
            WorldEventHooks.OnTouchOrLookCaptured = null;
            WorldEventHooks.OnHeroineKeyMemoryEvent = null;
            WorldEventHooks.Uninstall();

            TalkSceneController.Cleanup();
            if (windowBgTex != null) Destroy(windowBgTex);
            if (boxBgTex != null) Destroy(boxBgTex);
            if (inputBgTex != null) Destroy(inputBgTex);
            if (errorBgTex != null) Destroy(errorBgTex);
        }

        private void HandleTalkSceneStarted()
        {
            isTalkExiting = false;
            pendingAutoTriggers.Clear();
            autoReactionDueTime = -1f;
            highHProposalTriggered = false;
            pendingHighHAutoProposal = false;
            forceBypassAutoCooldown = false;
            lastObservedLewdness = -1;
        }

        private void HandleTalkSceneEnded()
        {
            isTalkExiting = true;
            CloseChatOnExitTalkScene();
        }

        private void HandleNativeDialogueCaptured(string speaker, string text)
        {
            if (isTalkExiting) return;
            if (currentCharacter == null) RefreshCharacterData();
            if (currentCharacter == null) return;

            CharacterMemory mem = GetMemoryById(currentCharacter.UniqueId, currentCharacter.FullName);
            string summary = "In-game dialogue (" + speaker + "): \"" + text + "\"";
            MemoryManager.AddRecentGameEvent(mem, summary);

            if (configAutoReactDialogues.Value)
            {
                EnqueueAutoTrigger(summary, 1.8f);
            }
        }

        private void HandleTouchOrLookCaptured(string partName, int touchMode)
        {
            if (isTalkExiting) return;
            if (currentCharacter == null) RefreshCharacterData();
            if (currentCharacter == null) return;

            CharacterMemory mem = GetMemoryById(currentCharacter.UniqueId, currentCharacter.FullName);
            string actionVerb = touchMode == 1
                ? T("stared at", "miró fijamente")
                : T("touched or petted", "tocó o acarició");
            string note = T("The player " + actionVerb + " " + partName + ".", "El jugador te " + actionVerb + " " + partName + ".");

            MemoryManager.AddRecentGameEvent(mem, note);
            mem.Messages.Add(new ChatMessage("system", note, T("Gesture", "Gesto")));
            scrollPosition.y = 100000f;

            if (configAutoReactGestures.Value)
            {
                EnqueueAutoTrigger(note, 1.5f);
            }
        }

        private void EnqueueAutoTrigger(string triggerText, float debounceSeconds)
        {
            if (string.IsNullOrEmpty(triggerText)) return;

            pendingAutoTriggers.Add(triggerText);
            if (pendingAutoTriggers.Count > 5)
            {
                pendingAutoTriggers.RemoveAt(0);
            }

            autoReactionDueTime = Time.unscaledTime + debounceSeconds;
        }

        private string GetAvailableHInvitationKey(CharacterSnapshot c)
        {
            if (c == null || c.AvailableInvitations == null) return "";
            if (c.AvailableInvitations.Contains("h_scene")) return "h_scene";
            if (c.AvailableInvitations.Contains("room")) return "room";
            return "";
        }

        private bool ShouldUrgeHighHProposal(CharacterSnapshot c)
        {
            int threshold = configHighHProposalThreshold.Value;
            if (threshold > 100 || highHProposalTriggered || c == null) return false;
            return c.Lewdness >= threshold && !string.IsNullOrEmpty(GetAvailableHInvitationKey(c));
        }

        private void TryQueueHighHProposal(float delaySeconds)
        {
            if (isTalkExiting || currentCharacter == null || highHProposalTriggered || pendingHighHAutoProposal || TalkSceneController.HasPendingInvitation())
            {
                return;
            }

            if (!ShouldUrgeHighHProposal(currentCharacter))
            {
                return;
            }

            pendingHighHAutoProposal = true;
            forceBypassAutoCooldown = true;
            autoReactionDueTime = Time.unscaledTime + delaySeconds;
        }

        private void HandleHeroineKeyMemoryEvent(string uniqueId, string charName, string memoryText)
        {
            if (string.IsNullOrEmpty(uniqueId) || string.IsNullOrEmpty(memoryText)) return;

            CharacterMemory mem = GetMemoryById(uniqueId, charName);
            MemoryManager.AddKeyMemory(mem, memoryText);
            Logger.LogInfo("[Memory saved for " + charName + "]: " + memoryText);
        }

        private void Update()
        {
            TalkSceneController.UseEnglishUI = configUiLanguage.Value == UILanguage.English;

            bool talkActiveNow = !isTalkExiting && CharacterDataReader.IsTalkSceneActuallyActive(cachedTalkScene);

            if (isTalkExiting)
            {
                if (!CharacterDataReader.IsTalkSceneActuallyActive(UnityEngine.Object.FindObjectOfType<TalkScene>()))
                {
                    isTalkExiting = false;
                }
            }
            else if ((showWindow || currentCharacter != null) && !talkActiveNow)
            {
                CloseChatOnExitTalkScene();
            }

            if (Input.GetKeyDown(configToggleKey.Value))
            {
                if (showWindow)
                {
                    showWindow = false;
                }
                else if (!isTalkExiting)
                {
                    RefreshCharacterData();
                    if (currentCharacter != null)
                    {
                        showWindow = true;
                    }
                }
            }

            if (Time.unscaledTime >= nextRefreshTime)
            {
                RefreshCharacterData();
                nextRefreshTime = Time.unscaledTime + 0.5f;
            }

            ProcessPendingAutoReaction();

            TalkSceneController.Update(currentCharacter != null && !isTalkExiting, configUseNativeDialogBox.Value);
        }

        private void ProcessPendingAutoReaction()
        {
            if (isTalkExiting || currentCharacter == null || isWaitingForReply)
            {
                return;
            }

            if (pendingAutoTriggers.Count == 0 && !pendingHighHAutoProposal)
            {
                return;
            }

            if (configProvider.Value != ApiProvider.LocalOrCustom
                && (string.IsNullOrEmpty(configApiKey.Value) || configApiKey.Value.Trim().Length < 10))
            {
                pendingAutoTriggers.Clear();
                pendingHighHAutoProposal = false;
                forceBypassAutoCooldown = false;
                return;
            }

            if (CharacterDataReader.IsNativeDialogBoxOpen())
            {
                autoReactionDueTime = Time.unscaledTime + 1.0f;
                return;
            }

            if (Time.unscaledTime < autoReactionDueTime)
            {
                return;
            }

            float cooldown = Mathf.Max(2f, configAutoReactCooldown.Value);
            if (!forceBypassAutoCooldown && (Time.unscaledTime - lastAutoReactionSentTime) < cooldown)
            {
                return;
            }

            string fallbackLang = configUiLanguage.Value == UILanguage.English ? "English" : "Spanish";

            if (pendingHighHAutoProposal)
            {
                pendingHighHAutoProposal = false;
                forceBypassAutoCooldown = false;
                pendingAutoTriggers.Clear();
                autoReactionDueTime = -1f;

                string hKey = GetAvailableHInvitationKey(currentCharacter);
                if (string.IsNullOrEmpty(hKey) || TalkSceneController.HasPendingInvitation())
                {
                    return;
                }

                highHProposalTriggered = true;
                string hPrompt = hKey == "h_scene"
                    ? "Your H Gauge just reached " + currentCharacter.Lewdness + "/100! You can no longer hold back your desire. Proactively ask the player to get intimate / start an H scene right now in " + fallbackLang + " (or the player's language), staying true to your personality, and set invitation to \"h_scene\"."
                    : "Your H Gauge just reached " + currentCharacter.Lewdness + "/100! You can no longer hold back your desire. Proactively ask the player to go to the room / somewhere private right now in " + fallbackLang + " (or the player's language), staying true to your personality, and set invitation to \"room\".";

                SendAutoReaction(hPrompt, true);
                return;
            }

            string combinedEvents = string.Join(" | ", pendingAutoTriggers.ToArray());
            pendingAutoTriggers.Clear();
            autoReactionDueTime = -1f;

            bool urgeH = ShouldUrgeHighHProposal(currentCharacter);
            if (urgeH)
            {
                highHProposalTriggered = true;
            }

            string autoPrompt = "Action just happened: " + combinedEvents
                + " Reply in character in " + fallbackLang
                + " (or the player's language) and fill all JSON fields including emotion, dialogue, favorDelta, lewdnessDelta, and invitation.";

            SendAutoReaction(autoPrompt, urgeH);
        }

        private void CloseChatOnExitTalkScene()
        {
            showWindow = false;
            cachedTalkScene = null;
            currentCharacter = null;
            lastCharacterId = "";
            errorBanner = "";
            statusBanner = "";
            pendingAutoTriggers.Clear();
            autoReactionDueTime = -1f;
            highHProposalTriggered = false;
            pendingHighHAutoProposal = false;
            forceBypassAutoCooldown = false;
            lastObservedLewdness = -1;
            TalkSceneController.HideDialogue();
            TalkSceneController.ClearPendingInvitation();
        }

        private void RefreshCharacterData()
        {
            if (isTalkExiting) return;

            TalkScene activeTalk = CharacterDataReader.GetActiveTalkScene();
            if (activeTalk == null)
            {
                if (showWindow || currentCharacter != null || cachedTalkScene != null)
                {
                    CloseChatOnExitTalkScene();
                }
                return;
            }

            cachedTalkScene = activeTalk;
            currentCharacter = CharacterDataReader.GetCurrentTalkHeroine();

            if (currentCharacter != null && currentCharacter.UniqueId != lastCharacterId)
            {
                lastCharacterId = currentCharacter.UniqueId;
                errorBanner = "";
                pendingAutoTriggers.Clear();
                autoReactionDueTime = -1f;
                highHProposalTriggered = false;
                pendingHighHAutoProposal = false;
                forceBypassAutoCooldown = false;
                lastObservedLewdness = currentCharacter.Lewdness;
                TalkSceneController.HideDialogue();
                TalkSceneController.ClearPendingInvitation();

                CharacterMemory mem = GetMemoryById(currentCharacter.UniqueId, currentCharacter.FullName);

                if (currentCharacter.IsFemaleOnlyArea && !currentCharacter.IsGirlfriend)
                {
                    MemoryManager.AddRecentGameEvent(mem, "The player walked in to talk to you in a female-only area (locker room/shower) without being your lover.");
                }
                else if (currentCharacter.IsExposure)
                {
                    MemoryManager.AddRecentGameEvent(mem, "The player caught you in a private moment wearing very little clothing.");
                }

                scrollPosition.y = 100000f;
            }
            else if (currentCharacter != null)
            {
                int prevLewd = lastObservedLewdness;
                lastObservedLewdness = currentCharacter.Lewdness;

                if (prevLewd >= 0 && currentCharacter.Lewdness > prevLewd && ShouldUrgeHighHProposal(currentCharacter))
                {
                    TryQueueHighHProposal(isWaitingForReply ? 3.5f : 1.8f);
                }
            }
            else
            {
                CloseChatOnExitTalkScene();
            }
        }

        private CharacterMemory GetMemoryById(string uniqueId, string characterName)
        {
            CharacterMemory memory;
            if (!loadedMemories.TryGetValue(uniqueId, out memory))
            {
                memory = MemoryManager.LoadOrCreate(uniqueId, characterName);
                loadedMemories[uniqueId] = memory;
            }

            return memory;
        }

        private CharacterMemory GetMemoryForCharacter(CharacterSnapshot character)
        {
            return GetMemoryById(character.UniqueId, character.FullName);
        }

        private void InitStyles()
        {
            if (windowStyle != null) return;

            windowBgTex = MakeSolidTexture(new Color(0.08f, 0.08f, 0.12f, 0.94f));
            boxBgTex = MakeSolidTexture(new Color(0.14f, 0.14f, 0.20f, 0.96f));
            inputBgTex = MakeSolidTexture(new Color(0.06f, 0.06f, 0.09f, 1.0f));
            errorBgTex = MakeSolidTexture(new Color(0.32f, 0.10f, 0.12f, 0.96f));

            windowStyle = new GUIStyle(GUI.skin.window);
            windowStyle.normal.background = windowBgTex;
            windowStyle.onNormal.background = windowBgTex;
            windowStyle.normal.textColor = Color.white;
            windowStyle.onNormal.textColor = Color.white;
            windowStyle.fontStyle = FontStyle.Bold;

            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.normal.background = boxBgTex;
            boxStyle.normal.textColor = Color.white;
            boxStyle.padding = new RectOffset(8, 8, 8, 8);

            errorBoxStyle = new GUIStyle(GUI.skin.box);
            errorBoxStyle.normal.background = errorBgTex;
            errorBoxStyle.normal.textColor = new Color(1f, 0.85f, 0.85f, 1f);
            errorBoxStyle.padding = new RectOffset(8, 8, 6, 6);

            inputAreaStyle = new GUIStyle(GUI.skin.textArea);
            inputAreaStyle.wordWrap = true;
            inputAreaStyle.normal.background = inputBgTex;
            inputAreaStyle.focused.background = inputBgTex;
            inputAreaStyle.hover.background = inputBgTex;
            inputAreaStyle.active.background = inputBgTex;
            inputAreaStyle.normal.textColor = Color.white;
            inputAreaStyle.focused.textColor = Color.white;
            inputAreaStyle.hover.textColor = Color.white;
            inputAreaStyle.active.textColor = Color.white;
            inputAreaStyle.padding = new RectOffset(6, 6, 6, 6);

            messageStyle = new GUIStyle(GUI.skin.label);
            messageStyle.wordWrap = true;
            messageStyle.richText = true;
            messageStyle.normal.textColor = Color.white;

            infoStyle = new GUIStyle(GUI.skin.label);
            infoStyle.wordWrap = true;
            infoStyle.richText = true;
            infoStyle.fontSize = 12;
            infoStyle.normal.textColor = new Color(0.92f, 0.94f, 1.0f, 1.0f);
        }

        private Texture2D MakeSolidTexture(Color color)
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        private void OnGUI()
        {
            if (isTalkExiting || currentCharacter == null)
            {
                return;
            }

            TalkSceneController.OnGUI(configUseNativeDialogBox.Value);

            if (!showWindow)
            {
                return;
            }

            InitStyles();

            if (configVisualNovelMode.Value)
            {
                DrawVisualNovelBar();
            }
            else
            {
                string title = "KKS AI Chat v1.1.0 - " + configProvider.Value + " (" + configToggleKey.Value + T(" to close)", " para cerrar)");
                windowRect = GUI.Window(98432, windowRect, DrawChatWindow, title, windowStyle);
            }
        }

        private void DrawVisualNovelBar()
        {
            bool shouldSend = false;
            Event e = Event.current;
            if (e.type == EventType.KeyDown
                && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                && !e.shift
                && GUI.GetNameOfFocusedControl() == "VNChatInputField")
            {
                e.Use();
                shouldSend = true;
            }

            float barWidth = Mathf.Min(Screen.width * 0.68f, 760f);
            float barHeight = 52f;
            float x = (Screen.width - barWidth) * 0.5f;
            float y = TalkSceneController.HasPendingInvitation() ? (Screen.height - 344f) : (Screen.height - 294f);
            if (y < 20f) y = 20f;

            // Aviso discreto de estado o error justo encima de la barra VN
            if (!string.IsNullOrEmpty(errorBanner))
            {
                GUILayout.BeginArea(new Rect(x, y - 34f, barWidth, 30f), errorBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#FFD6D6>" + errorBanner + "</color>", infoStyle);
                if (GUILayout.Button("X", GUILayout.Width(22), GUILayout.Height(18))) errorBanner = "";
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
            }
            else if (!string.IsNullOrEmpty(statusBanner))
            {
                GUILayout.BeginArea(new Rect(x, y - 30f, barWidth, 26f), boxStyle);
                GUILayout.Label("<color=#F1FA8C><i>" + statusBanner + "</i></color>", infoStyle);
                GUILayout.EndArea();
            }

            // Barra minimalista de escritura estilo Novela Visual
            GUILayout.BeginArea(new Rect(x, y, barWidth, barHeight), boxStyle);
            GUILayout.BeginHorizontal();

            GUI.enabled = !isWaitingForReply;
            GUI.SetNextControlName("VNChatInputField");
            inputText = GUILayout.TextArea(inputText, inputAreaStyle, GUILayout.ExpandWidth(true), GUILayout.Height(34f));

            string sendLabel = isWaitingForReply ? "..." : T("Send", "Enviar");
            if (GUILayout.Button(sendLabel, GUILayout.Width(72f), GUILayout.Height(34f)))
            {
                shouldSend = true;
            }
            GUI.enabled = true;

            if (GUILayout.Button(T("Log", "Historial"), GUILayout.Width(68f), GUILayout.Height(34f)))
            {
                configVisualNovelMode.Value = false;
            }

            if (GUILayout.Button("X", GUILayout.Width(28f), GUILayout.Height(34f)))
            {
                showWindow = false;
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            if (shouldSend && !isWaitingForReply)
            {
                SendChatMessage();
            }
        }

        private void DrawChatWindow(int windowID)
        {
            bool shouldSend = false;

            Event e = Event.current;
            if (e.type == EventType.KeyDown
                && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                && !e.shift
                && GUI.GetNameOfFocusedControl() == "ChatInputField")
            {
                e.Use();
                shouldSend = true;
            }

            GUILayout.BeginVertical();

            // Panel superior con ficha del personaje y entorno
            GUILayout.BeginVertical(boxStyle);
            CharacterMemory currentMem = GetMemoryForCharacter(currentCharacter);
            string playerRealName = GeminiClient.ResolvePlayerDisplayName(currentCharacter, configPlayerName.Value);

            string header = "<b>" + T("Character:", "Personaje:") + "</b> " + currentCharacter.FullName;
            if (!string.IsNullOrEmpty(currentCharacter.Nickname))
            {
                header += " (" + currentCharacter.Nickname + ")";
            }

            if (currentCharacter.CanUsePetName && !string.IsNullOrEmpty(currentCharacter.CallName))
            {
                header += " | <b>" + T("Calls you:", "Te llama:") + "</b> " + currentCharacter.CallName + T(" (lover)", " (pareja)");
            }
            else
            {
                header += " | <b>" + T("Calls you:", "Te llama:") + "</b> " + playerRealName;
            }

            GUILayout.Label(header, infoStyle);
            GUILayout.Label("<b>" + T("Personality:", "Personalidad:") + "</b> " + currentCharacter.PersonalityName, infoStyle);
            GUILayout.Label("<b>" + T("Relation:", "Relación:") + "</b> " + currentCharacter.RelationName
                + " (" + currentCharacter.Favor + "/100) | <b>" + T("H Gauge:", "Barra H:") + "</b> " + currentCharacter.Lewdness + "/100", infoStyle);

            string envLine = "<b>" + T("Scene:", "Entorno:") + "</b> " + currentCharacter.TimeOfDay;
            if (!string.IsNullOrEmpty(currentCharacter.DayOfWeek)) envLine += " (" + currentCharacter.DayOfWeek + ")";
            envLine += " | " + currentCharacter.CurrentPlace;
            envLine += currentCharacter.IsOtherPeopleNearby ? T(" | People nearby", " | Con gente cerca") : T(" | Alone", " | A solas");
            GUILayout.Label(envLine, infoStyle);

            string profileBadge = string.IsNullOrEmpty(currentCharacter.ProfileText)
                ? "<color=#FFD166>" + T("No profile", "Sin ficha") + "</color>"
                : "<color=#7CFC00>KK_Profile OK</color>";

            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>" + T("Profile:", "Perfil:") + "</b> " + profileBadge
                + " | <b>" + T("Memory:", "Memoria:") + "</b> " + currentMem.CountSavedDialogMessages() + " msgs"
                + " | <b>" + T("Key:", "Recuerdos:") + "</b> " + currentMem.KeyMemories.Count, infoStyle);
            if (GUILayout.Button(T("VN Mode", "Modo VN"), GUILayout.Width(72), GUILayout.Height(20)))
            {
                configVisualNovelMode.Value = true;
            }
            if (GUILayout.Button(T("Clear", "Limpiar"), GUILayout.Width(62), GUILayout.Height(20)))
            {
                currentMem.Messages.Clear();
                currentMem.RecentGameEvents.Clear();
                currentMem.LastSummarizedMsgCount = 0;
                pendingAutoTriggers.Clear();
                MemoryManager.Save(currentMem);
                TalkSceneController.HideDialogue();
                TalkSceneController.ClearPendingInvitation();
                statusBanner = "";
                errorBanner = "";
            }
            GUILayout.EndHorizontal();

            if (configProvider.Value != ApiProvider.LocalOrCustom && string.IsNullOrEmpty(configApiKey.Value))
            {
                GUILayout.Label("<color=#FF6B6B><b>" + T("Warning:", "Aviso:") + "</b> "
                    + T("Missing API Key for " + configProvider.Value + " (press F1/F11).",
                        "Falta configurar tu API Key para " + configProvider.Value + " (presiona F1/F11).") + "</color>", infoStyle);
            }
            GUILayout.EndVertical();

            // Barra de invitación dentro de la ventana de chat
            if (TalkSceneController.HasPendingInvitation())
            {
                string invKey = TalkSceneController.GetPendingInvitationKey();
                GUILayout.Space(4);
                GUILayout.BeginHorizontal(boxStyle);
                GUILayout.Label("<color=#FFD166><b>" + T("Proposal:", "Invitación:") + "</b></color> " + TalkSceneController.GetInvitationDisplayLabel(invKey), infoStyle);
                if (GUILayout.Button(T("Accept", "Aceptar"), GUILayout.Width(72), GUILayout.Height(22)))
                {
                    HandleAcceptInvitation(currentCharacter, invKey);
                }
                if (GUILayout.Button(T("Reject", "Rechazar"), GUILayout.Width(72), GUILayout.Height(22)))
                {
                    HandleRejectInvitation(currentCharacter, invKey);
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);

            // Historial de mensajes del personaje actual
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, boxStyle, GUILayout.ExpandHeight(true));
            if (currentMem.Messages.Count == 0)
            {
                GUILayout.Label("<color=#B0BEC5><i>"
                    + T("Type below to start chatting with " + currentCharacter.FullName + "...",
                        "Escribe algo abajo para iniciar una conversación con " + currentCharacter.FullName + "...")
                    + "</i></color>", messageStyle);
            }
            else
            {
                for (int i = 0; i < currentMem.Messages.Count; i++)
                {
                    ChatMessage msg = currentMem.Messages[i];
                    string colorHex = msg.Role == "user" ? "#8BE9FD" : (msg.Role == "model" ? "#FFB86C" : "#A0AEC0");
                    if (msg.DisplaySender == "Evento" || msg.DisplaySender == "Event") colorHex = "#7CFC00";
                    else if (msg.DisplaySender == "Gesto" || msg.DisplaySender == "Gesture") colorHex = "#FF79C6";

                    GUILayout.Label("<color=" + colorHex + "><b>" + msg.DisplaySender + ":</b></color> " + msg.Text, messageStyle);
                    GUILayout.Space(4);
                }
            }

            if (!string.IsNullOrEmpty(statusBanner))
            {
                GUILayout.Label("<color=#F1FA8C><i>" + statusBanner + "</i></color>", messageStyle);
            }
            GUILayout.EndScrollView();

            // Aviso temporal no intrusivo cuando falla la API
            if (!string.IsNullOrEmpty(errorBanner))
            {
                GUILayout.Space(4);
                GUILayout.BeginHorizontal(errorBoxStyle);
                GUILayout.Label("<color=#FFD6D6>" + errorBanner + "</color>", infoStyle);
                if (GUILayout.Button("X", GUILayout.Width(24), GUILayout.Height(22)))
                {
                    errorBanner = "";
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);

            // Campo de texto multilínea de ancho fijo + botón Enviar
            GUILayout.BeginHorizontal();
            GUI.enabled = !isWaitingForReply;

            float inputBoxWidth = Mathf.Max(150f, windowRect.width - 108f);
            inputScrollPosition = GUILayout.BeginScrollView(
                inputScrollPosition,
                false,
                false,
                GUILayout.Width(inputBoxWidth),
                GUILayout.Height(58)
            );
            GUI.SetNextControlName("ChatInputField");
            inputText = GUILayout.TextArea(inputText, inputAreaStyle, GUILayout.Width(inputBoxWidth - 22f), GUILayout.ExpandHeight(true));
            GUILayout.EndScrollView();

            GUILayout.Space(4);

            string btnText = isWaitingForReply ? "..." : T("Send", "Enviar");
            if (GUILayout.Button(btnText, GUILayout.Width(75), GUILayout.Height(58)))
            {
                shouldSend = true;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            if (shouldSend && !isWaitingForReply)
            {
                SendChatMessage();
            }

            GUI.DragWindow(new Rect(0, 0, windowRect.width, 25));
        }

        private void HandleAcceptInvitation(CharacterSnapshot targetChar, string invitationKey)
        {
            if (targetChar == null || string.IsNullOrEmpty(invitationKey)) return;

            TalkSceneController.ClearPendingInvitation();
            CharacterMemory mem = GetMemoryForCharacter(targetChar);
            string label = TalkSceneController.GetInvitationDisplayLabel(invitationKey);

            showWindow = false;

            StartCoroutine(TalkSceneController.TriggerNativeInvitationCoroutine(
                invitationKey,
                delegate (bool ok, string detail)
                {
                    if (ok)
                    {
                        MemoryManager.AddKeyMemory(mem, "You proposed \"" + label + "\" and the player accepted.");
                        mem.Messages.Add(new ChatMessage("system", T("Accepted proposal: ", "Aceptaste la invitación: ") + label, T("Event", "Evento")));
                        Logger.LogInfo("Native invitation triggered: " + invitationKey);
                    }
                    else
                    {
                        errorBanner = T("Could not start invitation: ", "No se pudo iniciar la invitación: ") + detail;
                        Logger.LogWarning("Failed to trigger invitation '" + invitationKey + "': " + detail);
                        if (currentCharacter != null && !isTalkExiting) showWindow = true;
                    }
                    scrollPosition.y = 100000f;
                }
            ));
        }

        private void HandleRejectInvitation(CharacterSnapshot targetChar, string invitationKey)
        {
            if (targetChar == null || string.IsNullOrEmpty(invitationKey)) return;

            TalkSceneController.ClearPendingInvitation();
            CharacterMemory mem = GetMemoryForCharacter(targetChar);
            string label = TalkSceneController.GetInvitationDisplayLabel(invitationKey);

            MemoryManager.AddKeyMemory(mem, "You proposed \"" + label + "\" but the player declined for now.");
            mem.Messages.Add(new ChatMessage("system", T("Declined proposal: ", "Rechazaste la invitación: ") + label, T("Event", "Evento")));
            scrollPosition.y = 100000f;
        }

        private string FormatFriendlyApiError(string rawError, bool restoredText)
        {
            string lower = (rawError ?? "").ToLowerInvariant();
            string providerName = configProvider.Value.ToString();
            string suffix = restoredText ? T(" Your message was restored to the input box.", " Tu mensaje volvió a la caja de texto.") : "";

            if (lower.Contains("(503)") || lower.Contains("high demand") || lower.Contains("overloaded"))
            {
                return providerName + T(" is experiencing high demand (503).", " tiene alta demanda (503).") + suffix;
            }
            if (lower.Contains("(429)") || lower.Contains("quota") || lower.Contains("rate limit"))
            {
                return T("Temporary rate limit on " + providerName + " (429).", "Límite temporal de peticiones en " + providerName + " (429).") + suffix;
            }
            if (lower.Contains("(401)") || lower.Contains("invalid api key") || lower.Contains("unauthorized"))
            {
                return T("Invalid API Key for " + providerName + " (401). Check F1/F11.", "API Key inválida para " + providerName + " (401). Revisa F1/F11.") + suffix;
            }
            if (lower.Contains("timed out") || lower.Contains("timeout"))
            {
                return T("Connection to " + providerName + " timed out.", "La conexión con " + providerName + " tardó demasiado.") + suffix;
            }

            return T("API Error (" + rawError + ").", "Error al contactar con " + providerName + " (" + rawError + ").") + suffix;
        }

        private void ApplyReplyToScene(CharacterSnapshot targetChar, CharacterMemory targetMemory, GeminiReply reply)
        {
            targetMemory.Messages.Add(new ChatMessage("model", reply.Dialogue, targetChar.FullName));
            MemoryManager.Save(targetMemory);

            int appliedFavor = 0;
            int appliedLewdness = 0;
            if (!isTalkExiting && currentCharacter != null && currentCharacter.UniqueId == targetChar.UniqueId)
            {
                TalkSceneController.ApplyReaction(
                    reply.Emotion,
                    reply.FavorDelta,
                    reply.LewdnessDelta,
                    out appliedFavor,
                    out appliedLewdness
                );
                TalkSceneController.ShowDialogue(
                    targetChar.FullName,
                    reply.Dialogue,
                    reply.Emotion,
                    appliedFavor,
                    appliedLewdness,
                    configUseNativeDialogBox.Value,
                    configTextSpeed.Value
                );

                if (!string.IsNullOrEmpty(reply.Invitation) && reply.Invitation != "none")
                {
                    string invKey = reply.Invitation;
                    if (invKey == "h_scene" || invKey == "room")
                    {
                        highHProposalTriggered = true;
                        pendingHighHAutoProposal = false;
                    }

                    TalkSceneController.SetPendingInvitation(
                        invKey,
                        delegate (string acceptedKey) { HandleAcceptInvitation(targetChar, acceptedKey); },
                        delegate (string rejectedKey) { HandleRejectInvitation(targetChar, rejectedKey); }
                    );
                }

                RefreshCharacterData();

                if (!highHProposalTriggered && appliedLewdness > 0 && ShouldUrgeHighHProposal(currentCharacter))
                {
                    TryQueueHighHProposal(3.5f);
                }
            }

            string favorWord = T(" Favor", " Afinidad");
            string reactionTag = T("Emotion: ", "Emoción: ") + TalkSceneController.TranslateEmotionLabel(reply.Emotion);
            if (appliedFavor > 0) reactionTag += " | +" + appliedFavor + favorWord;
            else if (appliedFavor < 0) reactionTag += " | " + appliedFavor + favorWord;
            if (appliedLewdness > 0) reactionTag += " | +" + appliedLewdness + " H";
            else if (appliedLewdness < 0) reactionTag += " | " + appliedLewdness + " H";
            if (!string.IsNullOrEmpty(reply.Invitation) && reply.Invitation != "none")
            {
                reactionTag += T(" | Proposal: ", " | Propuesta: ") + TalkSceneController.GetInvitationDisplayLabel(reply.Invitation);
            }

            targetMemory.Messages.Add(new ChatMessage("system", "[" + reactionTag + "]", T("Status", "Estado")));
            scrollPosition.y = 100000f;

            CheckAndTriggerAutoSummary(targetChar, targetMemory);
        }

        private void CheckAndTriggerAutoSummary(CharacterSnapshot targetChar, CharacterMemory targetMemory)
        {
            if (!configAutoSummary.Value || isSummarizing || targetChar == null || targetMemory == null)
            {
                return;
            }

            int interval = Mathf.Clamp(configAutoSummaryInterval.Value, 6, 30);
            List<ChatMessage> dialogMsgs = targetMemory.GetDialogMessagesOnly();

            if (dialogMsgs.Count < targetMemory.LastSummarizedMsgCount)
            {
                targetMemory.LastSummarizedMsgCount = dialogMsgs.Count;
            }

            int unsummarizedCount = dialogMsgs.Count - targetMemory.LastSummarizedMsgCount;
            if (unsummarizedCount < interval)
            {
                return;
            }

            int startIndex = Math.Max(0, targetMemory.LastSummarizedMsgCount);
            List<ChatMessage> chunk = dialogMsgs.GetRange(startIndex, dialogMsgs.Count - startIndex);
            int targetCountSnapshot = dialogMsgs.Count;

            isSummarizing = true;
            Logger.LogInfo("[Memory] Generating auto-summary for " + targetChar.FullName + " (" + chunk.Count + " new messages)...");

            StartCoroutine(GeminiClient.SummarizeConversationCoroutine(
                configProvider.Value,
                configApiKey.Value,
                configModel.Value,
                configCustomEndpointUrl.Value,
                targetChar.FullName,
                chunk,
                delegate (string summaryText)
                {
                    targetMemory.LastSummarizedMsgCount = targetCountSnapshot;
                    MemoryManager.AddKeyMemory(targetMemory, summaryText);
                    Logger.LogInfo("[Memory] Auto-summary saved for " + targetChar.FullName + ": " + summaryText);
                    isSummarizing = false;
                },
                delegate (string err)
                {
                    Logger.LogWarning("[Memory] Auto-summary failed for " + targetChar.FullName + ": " + err);
                    isSummarizing = false;
                }
            ));
        }

        private void SendAutoReaction(string autoPrompt, bool urgeHighHProposal)
        {
            if (currentCharacter == null || isWaitingForReply) return;

            CharacterSnapshot targetChar = currentCharacter;
            CharacterMemory targetMemory = GetMemoryForCharacter(targetChar);

            isWaitingForReply = true;
            lastAutoReactionSentTime = Time.unscaledTime;
            statusBanner = targetChar.FullName + T(" is reacting...", " está reaccionando...");
            scrollPosition.y = 100000f;

            StartCoroutine(GeminiClient.SendMessageCoroutine(
                configProvider.Value,
                configApiKey.Value,
                configModel.Value,
                configCustomEndpointUrl.Value,
                configTemperature.Value,
                configMaxContextMessages.Value,
                configPlayerName.Value,
                targetChar,
                targetMemory,
                autoPrompt,
                urgeHighHProposal,
                delegate (GeminiReply reply)
                {
                    ApplyReplyToScene(targetChar, targetMemory, reply);
                    isWaitingForReply = false;
                    statusBanner = "";
                    errorBanner = "";
                },
                delegate (string error)
                {
                    Logger.LogWarning("Auto-reaction error: " + error);
                    if (currentCharacter != null && currentCharacter.UniqueId == targetChar.UniqueId)
                    {
                        errorBanner = FormatFriendlyApiError(error, false);
                    }
                    isWaitingForReply = false;
                    statusBanner = "";
                }
            ));
        }

        private void SendChatMessage()
        {
            string trimmed = inputText.Trim();
            if (string.IsNullOrEmpty(trimmed) || currentCharacter == null)
            {
                return;
            }

            pendingAutoTriggers.Clear();
            pendingHighHAutoProposal = false;
            forceBypassAutoCooldown = false;
            autoReactionDueTime = -1f;
            TalkSceneController.ClearPendingInvitation();
            errorBanner = "";

            CharacterSnapshot targetChar = currentCharacter;
            CharacterMemory targetMemory = GetMemoryForCharacter(targetChar);
            bool urgeH = ShouldUrgeHighHProposal(targetChar);

            ChatMessage sentMessage = new ChatMessage("user", trimmed, T("You", "Tú"));
            targetMemory.Messages.Add(sentMessage);

            inputText = "";
            inputScrollPosition = Vector2.zero;
            isWaitingForReply = true;
            statusBanner = targetChar.FullName + T(" is thinking...", " está pensando...");
            scrollPosition.y = 100000f;

            StartCoroutine(GeminiClient.SendMessageCoroutine(
                configProvider.Value,
                configApiKey.Value,
                configModel.Value,
                configCustomEndpointUrl.Value,
                configTemperature.Value,
                configMaxContextMessages.Value,
                configPlayerName.Value,
                targetChar,
                targetMemory,
                null,
                urgeH,
                delegate (GeminiReply reply)
                {
                    if (urgeH)
                    {
                        highHProposalTriggered = true;
                    }
                    ApplyReplyToScene(targetChar, targetMemory, reply);
                    isWaitingForReply = false;
                    statusBanner = "";
                    errorBanner = "";
                },
                delegate (string error)
                {
                    targetMemory.Messages.Remove(sentMessage);

                    if (currentCharacter != null && currentCharacter.UniqueId == targetChar.UniqueId)
                    {
                        if (string.IsNullOrEmpty(inputText.Trim()))
                        {
                            inputText = trimmed;
                        }
                        else
                        {
                            inputText = trimmed + "\n" + inputText;
                        }
                        errorBanner = FormatFriendlyApiError(error, true);
                    }

                    Logger.LogWarning("API Error (message restored to input): " + error);
                    isWaitingForReply = false;
                    statusBanner = "";
                    scrollPosition.y = 100000f;
                }
            ));
        }
    }
}