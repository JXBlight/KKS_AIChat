using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace KKS_AIChat
{
    public enum ApiProvider
    {
        Gemini,
        Groq,
        OpenRouter,
        LocalOrCustom
    }

    public class GeminiReply
    {
        public string Dialogue;
        public string Emotion;
        public int FavorDelta;
        public int LewdnessDelta;
        public string Invitation;
    }

    public static class GeminiClient
    {
        public static IEnumerator SendMessageCoroutine(
            ApiProvider provider,
            string apiKey,
            string modelName,
            string customEndpointUrl,
            float temperature,
            int maxContextMessages,
            string playerName,
            CharacterSnapshot character,
            CharacterMemory memory,
            string transientUserPrompt,
            bool urgeHighHProposal,
            Action<GeminiReply> onSuccess,
            Action<string> onError)
        {
            string trimmedKey = string.IsNullOrEmpty(apiKey) ? "" : apiKey.Trim();
            bool requiresKey = provider != ApiProvider.LocalOrCustom;

            if (requiresKey && trimmedKey.Length < 10)
            {
                if (onError != null)
                {
                    onError("Missing API Key for " + provider + " (F1/F11 -> KKS AI Chat).");
                }
                yield break;
            }

            string url = ResolveEndpointUrl(provider, modelName, customEndpointUrl);
            if (string.IsNullOrEmpty(url))
            {
                if (onError != null)
                {
                    onError("Missing custom server URL in 'UrlPersonalizada'.");
                }
                yield break;
            }

            string systemPrompt = BuildSystemPrompt(character, memory, playerName, urgeHighHProposal);
            string jsonBody = provider == ApiProvider.Gemini
                ? BuildGeminiRequestJson(systemPrompt, memory.Messages, maxContextMessages, temperature, transientUserPrompt)
                : BuildOpenAICompatibleRequestJson(provider, modelName, systemPrompt, memory.Messages, maxContextMessages, temperature, transientUserPrompt, character.AvailableInvitations);

            using (UnityWebRequest request = CreateConfiguredRequest(provider, url, trimmedKey, jsonBody))
            {
                yield return request.SendWebRequest();

                string responseText = request.downloadHandler != null ? request.downloadHandler.text : "";

                if (request.isNetworkError || request.isHttpError)
                {
                    string apiError = ExtractErrorMessage(responseText);
                    if (!string.IsNullOrEmpty(apiError))
                    {
                        if (onError != null) onError(provider + " (" + request.responseCode + "): " + apiError);
                    }
                    else
                    {
                        if (onError != null) onError(provider + " (" + request.responseCode + "): " + request.error);
                    }
                    yield break;
                }

                string rawModelText;
                string parseError;
                bool extracted = provider == ApiProvider.Gemini
                    ? TryExtractGeminiReplyText(responseText, out rawModelText, out parseError)
                    : TryExtractOpenAIReplyText(responseText, out rawModelText, out parseError);

                if (extracted)
                {
                    string availList = (character != null && character.AvailableInvitations != null && character.AvailableInvitations.Count > 0)
                        ? string.Join(", ", character.AvailableInvitations.ToArray())
                        : "none";
                    Debug.Log("[KKS AI Chat] Invitaciones disponibles: [" + availList + "] | Raw: " + rawModelText);

                    GeminiReply structured = ParseStructuredReply(rawModelText, character.AvailableInvitations);
                    if (string.IsNullOrEmpty(structured.Dialogue) || structured.Dialogue.Trim() == "{}")
                    {
                        if (onError != null) onError("El modelo devolvió una respuesta sin diálogo válido.");
                        yield break;
                    }
                    if (onSuccess != null) onSuccess(structured);
                }
                else
                {
                    if (onError != null) onError(parseError);
                }
            }
        }

        public static IEnumerator SummarizeConversationCoroutine(
            ApiProvider provider,
            string apiKey,
            string modelName,
            string customEndpointUrl,
            string characterName,
            List<ChatMessage> chunkToSummarize,
            Action<string> onSuccess,
            Action<string> onError)
        {
            string trimmedKey = string.IsNullOrEmpty(apiKey) ? "" : apiKey.Trim();
            if (provider != ApiProvider.LocalOrCustom && trimmedKey.Length < 10)
            {
                if (onError != null) onError("Missing API Key for summary.");
                yield break;
            }

            string url = ResolveEndpointUrl(provider, modelName, customEndpointUrl);
            if (string.IsNullOrEmpty(url) || chunkToSummarize == null || chunkToSummarize.Count == 0)
            {
                if (onError != null) onError("Insufficient data to summarize.");
                yield break;
            }

            StringBuilder transcript = new StringBuilder();
            for (int i = 0; i < chunkToSummarize.Count; i++)
            {
                ChatMessage m = chunkToSummarize[i];
                string who = m.Role == "user" ? "Player" : characterName;
                transcript.AppendLine(who + ": " + m.Text);
            }

            string sysPrompt = "You are a memory assistant for a character named " + characterName
                + ". Summarize in 1 or 2 very concise sentences (in second person addressed to " + characterName
                + ") the important facts, promises, preferences, or relationship changes from this conversation fragment. "
                + "Write the summary in the EXACT SAME LANGUAGE used in the conversation. Reply ONLY with the clean summary, no quotes or headers.";

            string userPrompt = "Conversation fragment to remember:\n" + transcript.ToString();
            string jsonBody = BuildPlainTextSummaryJson(provider, modelName, sysPrompt, userPrompt);

            using (UnityWebRequest request = CreateConfiguredRequest(provider, url, trimmedKey, jsonBody))
            {
                yield return request.SendWebRequest();

                string responseText = request.downloadHandler != null ? request.downloadHandler.text : "";
                if (request.isNetworkError || request.isHttpError)
                {
                    if (onError != null) onError("Summary error (" + request.responseCode + ")");
                    yield break;
                }

                string rawText;
                string parseError;
                bool ok = provider == ApiProvider.Gemini
                    ? TryExtractGeminiReplyText(responseText, out rawText, out parseError)
                    : TryExtractOpenAIReplyText(responseText, out rawText, out parseError);

                if (ok && !string.IsNullOrEmpty(rawText))
                {
                    string cleanSummary = rawText.Replace("\r", " ").Replace("\n", " ").Trim();
                    if (onSuccess != null) onSuccess(cleanSummary);
                }
                else
                {
                    if (onError != null) onError(parseError);
                }
            }
        }

        private static UnityWebRequest CreateConfiguredRequest(ApiProvider provider, string url, string trimmedKey, string jsonBody)
        {
            UnityWebRequest request = new UnityWebRequest(url, "POST");
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            request.SetRequestHeader("User-Agent", "KKS_AIChat/1.0");

            if (provider == ApiProvider.Gemini)
            {
                request.SetRequestHeader("x-goog-api-key", trimmedKey);
            }
            else
            {
                if (!string.IsNullOrEmpty(trimmedKey))
                {
                    request.SetRequestHeader("Authorization", "Bearer " + trimmedKey);
                }
                if (provider == ApiProvider.OpenRouter)
                {
                    request.SetRequestHeader("HTTP-Referer", "https://localhost/kks-aichat");
                    request.SetRequestHeader("X-Title", "KKS AI Chat");
                }
            }

            request.timeout = provider == ApiProvider.LocalOrCustom ? 90 : 45;
            return request;
        }

        private static string BuildPlainTextSummaryJson(ApiProvider provider, string modelName, string sysPrompt, string userPrompt)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{");

            if (provider == ApiProvider.Gemini)
            {
                sb.Append("\"system_instruction\":{\"parts\":[{\"text\":");
                SimpleJson.AppendEscaped(sb, sysPrompt);
                sb.Append("}]},");
                sb.Append("\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":");
                SimpleJson.AppendEscaped(sb, userPrompt);
                sb.Append("}]}],");
                sb.Append("\"generationConfig\":{\"temperature\":0.30,\"maxOutputTokens\":1024}");
            }
            else
            {
                sb.Append("\"model\":");
                SimpleJson.AppendEscaped(sb, ResolveModelName(provider, modelName));
                sb.Append(",\"messages\":[{\"role\":\"system\",\"content\":");
                SimpleJson.AppendEscaped(sb, sysPrompt);
                sb.Append("},{\"role\":\"user\",\"content\":");
                SimpleJson.AppendEscaped(sb, userPrompt);
                sb.Append("}],\"temperature\":0.30,\"max_tokens\":1024");
            }

            sb.Append("}");
            return sb.ToString();
        }

        private static string ResolveEndpointUrl(ApiProvider provider, string modelName, string customEndpointUrl)
        {
            switch (provider)
            {
                case ApiProvider.Gemini:
                    string cleanModel = string.IsNullOrEmpty(modelName) ? "gemini-3-flash-preview" : modelName.Trim();
                    return "https://generativelanguage.googleapis.com/v1beta/models/" + cleanModel + ":generateContent";
                case ApiProvider.Groq:
                    return "https://api.groq.com/openai/v1/chat/completions";
                case ApiProvider.OpenRouter:
                    return "https://openrouter.ai/api/v1/chat/completions";
                case ApiProvider.LocalOrCustom:
                    return string.IsNullOrEmpty(customEndpointUrl) ? "" : customEndpointUrl.Trim();
                default:
                    return "";
            }
        }

        private static string ResolveModelName(ApiProvider provider, string configuredModel)
        {
            if (!string.IsNullOrEmpty(configuredModel) && !string.IsNullOrEmpty(configuredModel.Trim()))
            {
                return configuredModel.Trim();
            }

            switch (provider)
            {
                case ApiProvider.Groq: return "llama-3.3-70b-versatile";
                case ApiProvider.OpenRouter: return "openrouter/free";
                case ApiProvider.LocalOrCustom: return "llama3.1";
                default: return "gemini-3-flash-preview";
            }
        }

        public static string ResolvePlayerDisplayName(CharacterSnapshot c, string configPlayerName)
        {
            if (!string.IsNullOrEmpty(configPlayerName)
                && !configPlayerName.Trim().Equals("Jugador", StringComparison.OrdinalIgnoreCase)
                && !configPlayerName.Trim().Equals("Player", StringComparison.OrdinalIgnoreCase))
            {
                return configPlayerName.Trim();
            }

            if (c != null && !string.IsNullOrEmpty(c.PlayerInGameName))
            {
                return c.PlayerInGameName;
            }

            return "Player";
        }

        private static string BuildSystemPrompt(CharacterSnapshot c, CharacterMemory memory, string playerName, bool urgeHighHProposal)
        {
            StringBuilder sb = new StringBuilder();
            string resolvedPlayerName = ResolvePlayerDisplayName(c, playerName);

            sb.AppendLine("You are roleplaying in first person as a character inside the tropical resort vacation setting of Koikatsu Sunshine.");
            sb.AppendLine("Never break character or mention that you are an AI, a language model, or a video game.");
            sb.AppendLine("CRITICAL LANGUAGE RULE: ALWAYS write your 'dialogue' in the EXACT SAME LANGUAGE the player writes in (English, Spanish, Russian, Japanese, Hindi, Italian, Portuguese, French, German, Korean, Chinese, etc.). Even if the metadata, memories, or game events below are in Spanish or Japanese, you MUST strictly match the player's language naturally.");
            sb.AppendLine();

            sb.AppendLine("=== MANDATORY RESPONSE FORMAT (JSON) ===");
            sb.AppendLine("Always reply with a SINGLE valid JSON object (no extra text outside the JSON) with these exact five keys in this order:");
            sb.AppendLine("{");
            sb.AppendLine("  \"emotion\": \"Choose FIRST the exact emotion that matches your reaction: neutral, happy, excited, shy, flirty, surprised, angry, sad, serious\",");
            sb.AppendLine("  \"dialogue\": \"Your spoken reply in the player's language matching the chosen emotion. Do NOT use asterisks or narrate physical gestures because the game engine animates your face and pose automatically.\",");
            sb.AppendLine("  \"favorDelta\": 0,");
            sb.AppendLine("  \"lewdnessDelta\": 0,");
            sb.AppendLine("  \"invitation\": \"none\"");
            sb.AppendLine("}");
            sb.AppendLine("Rules for emotion:");
            sb.AppendLine("- Your 'emotion' MUST strictly match the tone and meaning of your 'dialogue' (e.g., use 'happy' or 'excited' for joy, praise, or enthusiasm; use 'sad' ONLY when genuinely hurt, disappointed, or down; never contradict your own words).");
            sb.AppendLine("Rules for dialogue length (adaptive & natural):");
            sb.AppendLine("- Adjust response length organically to fit the conversation topic, situation, and your character profile:");
            sb.AppendLine("  * Keep it SHORT (1 to 2 sentences) for casual banter, greetings, quick reactions, or if your character is quiet/reserved.");
            sb.AppendLine("  * Write LONGER, more expressive replies (3 to 6 sentences) when discussing deeper/emotional topics, explaining something, telling a story, or when your character profile is talkative, energetic, or passionate about the subject.");
            sb.AppendLine("Rules for favorDelta (general affinity change on a 0-100 scale, integer from -50 to 50):");
            sb.AppendLine("- 0: neutral small talk with no emotional impact.");
            sb.AppendLine("- +10 to +20: pleasant, friendly, kind, fun, or matching your tastes.");
            sb.AppendLine("- +25 to +35: very sweet, heartfelt compliment, strong bonding, or impressive gesture.");
            sb.AppendLine("- +40 to +50: deeply romantic, unforgettable, or emotionally moving moment.");
            sb.AppendLine("- -10 to -50: rude comment, unwanted touch without trust, or something that upsets your personality (scale magnitude to severity).");
            sb.AppendLine("Rules for lewdnessDelta (H gauge / flirty-intimate mood change on a 0-100 scale, integer from -50 to 50):");
            sb.AppendLine("- 0: normal chat with no romantic/flirty charge.");
            sb.AppendLine("- +10 to +20: flirting, compliments on attractiveness/outfit, or affectionate touches that make you blush.");
            sb.AppendLine("- +25 to +35: bold flirting, sensual teasing, or intimate touches well received by you (especially when receptive or alone).");
            sb.AppendLine("- +40 to +50: intense seduction, passionate flirting, or provocative physical contact that excites you deeply.");
            sb.AppendLine("- -10 to -50: something that kills the romantic mood or makes you uncomfortable.");
            sb.AppendLine();

            sb.AppendLine("=== INVITATION RULES (invitation) ===");
            if (c.AvailableInvitations != null && c.AvailableInvitations.Count > 0)
            {
                sb.AppendLine("Valid options RIGHT NOW in this scene:");
                sb.AppendLine("- \"none\": Do not propose any activity change (use this in most normal turns).");
                for (int i = 0; i < c.AvailableInvitations.Count; i++)
                {
                    string key = c.AvailableInvitations[i];
                    sb.AppendLine("- \"" + key + "\": " + TalkSceneController.GetInvitationPromptDescription(key));
                }
                sb.AppendLine("CRITICAL INVITATION RULE:");
                sb.AppendLine("1. If the player invites you or suggests eating, sightseeing, playing, kayaking/activities, walking together, dating, or intimacy AND you agree in your dialogue, you MUST set \"invitation\" to the matching key (DO NOT leave it as \"none\").");
                sb.AppendLine("2. You may also spontaneously propose an activity occasionally if it fits the conversation, time of day, and your personality.");
                sb.AppendLine("3. When your H Gauge is high (80/100 or more, or your lewdnessDelta brings it to 80+), feel free to proactively ask the player to start an H scene (\"h_scene\") or go somewhere private (\"room\") if available.");
                sb.AppendLine("4. NEVER use a key that is not in the list above.");
            }
            else
            {
                sb.AppendLine("No invitations are available right now; ALWAYS use \"none\".");
            }
            sb.AppendLine();

            sb.AppendLine("=== CURRENT ENVIRONMENT ===");
            sb.AppendLine("Time of day: " + c.TimeOfDay + (string.IsNullOrEmpty(c.DayOfWeek) ? "" : " (" + c.DayOfWeek + ")"));
            sb.AppendLine("Location: " + c.CurrentPlace);
            sb.AppendLine("Other people nearby?: " + (c.IsOtherPeopleNearby ? "Yes" : "No, you are alone"));
            if (c.IsFemaleOnlyArea)
            {
                sb.AppendLine("Location alert: You are in a private/female-only area (locker room, bath, or shower). React according to your trust level with the player.");
            }
            if (c.IsExposure)
            {
                sb.AppendLine("Situation alert: You are in an exposed or lightly dressed situation in front of the player.");
            }
            if (c.IsFollowingPlayer)
            {
                sb.AppendLine("Note: You are already walking/following the player around the island.");
            }
            sb.AppendLine();

            sb.AppendLine("=== CHARACTER DATA ===");
            sb.AppendLine("Name: " + c.FullName);
            if (!string.IsNullOrEmpty(c.Nickname))
            {
                sb.AppendLine("Nickname: " + c.Nickname);
            }
            sb.AppendLine("Base game personality: " + c.PersonalityName);
            sb.AppendLine("Player's name: " + resolvedPlayerName);

            if (!string.IsNullOrEmpty(c.HeightComparisonText))
            {
                sb.AppendLine("Height relative to player: " + c.HeightComparisonText);
            }

            if (c.CanUsePetName && !string.IsNullOrEmpty(c.CallName))
            {
                sb.AppendLine("Unlocked lover pet name: " + c.CallName + " (since you are lovers, you can affectionately call the player this or use their real name depending on the moment).");
            }
            else
            {
                sb.AppendLine("How you address the player: Call them by their name (" + resolvedPlayerName + ") or naturally according to your personality. You are NOT lovers yet, so do NOT use romantic pet names.");
            }
            sb.AppendLine();

            sb.AppendLine("=== RELATIONSHIP WITH PLAYER ===");
            sb.AppendLine("Relationship level: " + c.RelationName + " (Favor: " + c.Favor + "/100)");
            sb.AppendLine("Current H Gauge / Intimate mood: " + c.Lewdness + "/100 (the higher it is, the more flustered, receptive, or bold you feel toward flirting).");
            sb.AppendLine("Are you lovers/dating?: " + (c.IsGirlfriend ? "Yes" : "No"));
            if (c.IsFirstGirlfriend)
            {
                sb.AppendLine("Special note: You are the player's first girlfriend.");
            }
            sb.AppendLine("Have you kissed?: " + (c.IsKiss ? "Yes" : "No"));
            sb.AppendLine("Intimate encounters count: " + c.HCount + " (Experience: " + c.HExperience + ")");
            if (c.IsDayH)
            {
                sb.AppendLine("Recent event: You already shared an intimate moment today.");
            }
            sb.AppendLine();

            sb.AppendLine("=== CURRENT OUTFIT ===");
            sb.AppendLine("Outfit category: " + c.OutfitCategory);
            sb.AppendLine("Clothing details: " + c.ClothingSummary);
            sb.AppendLine();

            if (!string.IsNullOrEmpty(c.ProfileText))
            {
                sb.AppendLine("=== DETAILED CHARACTER PROFILE & LORE (HIGH PRIORITY) ===");
                sb.AppendLine(c.ProfileText);
                sb.AppendLine();
            }

            if (memory != null && memory.KeyMemories != null && memory.KeyMemories.Count > 0)
            {
                sb.AppendLine("=== LONG-TERM MEMORIES & KEY EVENTS WITH PLAYER ===");
                for (int i = 0; i < memory.KeyMemories.Count; i++)
                {
                    sb.AppendLine("- " + memory.KeyMemories[i]);
                }
                sb.AppendLine();
            }

            if (memory != null && memory.RecentGameEvents != null && memory.RecentGameEvents.Count > 0)
            {
                sb.AppendLine("=== RECENT IN-GAME DIALOGUES & GESTURES ===");
                sb.AppendLine("(Keep these recent in-game actions in mind, and always reply in the player's language):");
                for (int i = 0; i < memory.RecentGameEvents.Count; i++)
                {
                    sb.AppendLine("- " + memory.RecentGameEvents[i]);
                }
                sb.AppendLine();
            }

            if (urgeHighHProposal && c.AvailableInvitations != null)
            {
                string preferredHKey = c.AvailableInvitations.Contains("h_scene")
                    ? "h_scene"
                    : (c.AvailableInvitations.Contains("room") ? "room" : "");

                if (!string.IsNullOrEmpty(preferredHKey))
                {
                    sb.AppendLine("=== HIGH H-GAUGE EVENT (TAKE THE INITIATIVE RIGHT NOW) ===");
                    sb.AppendLine("Your H Gauge is very high (" + c.Lewdness + "/100)! You are intensely flustered, aroused, and strongly desire to be intimate with the player right now.");
                    if (preferredHKey == "h_scene")
                    {
                        sb.AppendLine("In your 'dialogue', proactively ask or invite the player to get intimate / start an H scene right now (matching your personality: shy, bold, teasing, or passionate), and you MUST set \"invitation\" to \"h_scene\".");
                    }
                    else
                    {
                        sb.AppendLine("In your 'dialogue', proactively ask or invite the player to go somewhere private / to the room together right now to be intimate (matching your personality), and you MUST set \"invitation\" to \"room\".");
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("Remember: Pick 'emotion' first so it matches your 'dialogue' perfectly, match the player's language strictly, and adapt your message length to the topic and your profile.");
            if (c.AvailableInvitations != null && c.AvailableInvitations.Count > 0)
            {
                sb.AppendLine("If you agree to or propose an activity right now, you MUST set \"invitation\" to one of: " + string.Join(", ", c.AvailableInvitations.ToArray()) + " (otherwise \"none\").");
            }

            return sb.ToString();
        }

        private static List<KeyValuePair<string, string>> BuildMergedTurns(
            List<ChatMessage> history,
            int maxContextMessages,
            string transientUserPrompt,
            string assistantRoleName)
        {
            List<ChatMessage> validMessages = new List<ChatMessage>();
            for (int i = 0; i < history.Count; i++)
            {
                if (history[i].Role == "user" || history[i].Role == "model")
                {
                    validMessages.Add(history[i]);
                }
            }

            int clampedMax = Mathf.Clamp(maxContextMessages, 4, 40);
            int startIndex = Math.Max(0, validMessages.Count - clampedMax);

            List<KeyValuePair<string, string>> mergedTurns = new List<KeyValuePair<string, string>>();
            for (int i = startIndex; i < validMessages.Count; i++)
            {
                string role = validMessages[i].Role == "model" ? assistantRoleName : "user";
                string text = validMessages[i].Text;

                if (mergedTurns.Count > 0 && mergedTurns[mergedTurns.Count - 1].Key == role)
                {
                    string combined = mergedTurns[mergedTurns.Count - 1].Value + "\n" + text;
                    mergedTurns[mergedTurns.Count - 1] = new KeyValuePair<string, string>(role, combined);
                }
                else
                {
                    mergedTurns.Add(new KeyValuePair<string, string>(role, text));
                }
            }

            if (!string.IsNullOrEmpty(transientUserPrompt))
            {
                if (mergedTurns.Count > 0 && mergedTurns[mergedTurns.Count - 1].Key == "user")
                {
                    string combined = mergedTurns[mergedTurns.Count - 1].Value + "\n" + transientUserPrompt;
                    mergedTurns[mergedTurns.Count - 1] = new KeyValuePair<string, string>("user", combined);
                }
                else
                {
                    mergedTurns.Add(new KeyValuePair<string, string>("user", transientUserPrompt));
                }
            }

            while (mergedTurns.Count > 1 && mergedTurns[0].Key != "user")
            {
                mergedTurns.RemoveAt(0);
            }

            return mergedTurns;
        }

        private static string BuildGeminiRequestJson(
            string systemPrompt,
            List<ChatMessage> history,
            int maxContextMessages,
            float temperature,
            string transientUserPrompt)
        {
            List<KeyValuePair<string, string>> mergedTurns = BuildMergedTurns(history, maxContextMessages, transientUserPrompt, "model");

            StringBuilder sb = new StringBuilder();
            sb.Append("{");

            sb.Append("\"system_instruction\":{\"parts\":[{\"text\":");
            SimpleJson.AppendEscaped(sb, systemPrompt);
            sb.Append("}]},");

            sb.Append("\"contents\":[");
            for (int i = 0; i < mergedTurns.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("{\"role\":");
                SimpleJson.AppendEscaped(sb, mergedTurns[i].Key);
                sb.Append(",\"parts\":[{\"text\":");
                SimpleJson.AppendEscaped(sb, mergedTurns[i].Value);
                sb.Append("}]}");
            }
            sb.Append("],");

            float clampedTemp = Mathf.Clamp(temperature, 0.1f, 1.5f);
            sb.Append("\"generationConfig\":{");
            sb.Append("\"temperature\":");
            sb.Append(clampedTemp.ToString("0.00", CultureInfo.InvariantCulture));
            sb.Append(",\"maxOutputTokens\":2048");
            sb.Append(",\"responseMimeType\":\"application/json\"");
            sb.Append("},");

            sb.Append("\"safetySettings\":[");
            sb.Append("{\"category\":\"HARM_CATEGORY_HARASSMENT\",\"threshold\":\"BLOCK_NONE\"},");
            sb.Append("{\"category\":\"HARM_CATEGORY_HATE_SPEECH\",\"threshold\":\"BLOCK_NONE\"},");
            sb.Append("{\"category\":\"HARM_CATEGORY_SEXUALLY_EXPLICIT\",\"threshold\":\"BLOCK_NONE\"},");
            sb.Append("{\"category\":\"HARM_CATEGORY_DANGEROUS_CONTENT\",\"threshold\":\"BLOCK_NONE\"}");
            sb.Append("]");

            sb.Append("}");
            return sb.ToString();
        }

        private static void AppendInvitationEnumSchema(StringBuilder sb, List<string> allowedInvitations)
        {
            sb.Append("[\"none\"");
            if (allowedInvitations != null)
            {
                for (int i = 0; i < allowedInvitations.Count; i++)
                {
                    string key = allowedInvitations[i];
                    if (!string.IsNullOrEmpty(key) && key != "none")
                    {
                        sb.Append(",");
                        SimpleJson.AppendEscaped(sb, key);
                    }
                }
            }
            sb.Append("]");
        }

        private static string BuildOpenAICompatibleRequestJson(
            ApiProvider provider,
            string modelName,
            string systemPrompt,
            List<ChatMessage> history,
            int maxContextMessages,
            float temperature,
            string transientUserPrompt,
            List<string> allowedInvitations)
        {
            List<KeyValuePair<string, string>> mergedTurns = BuildMergedTurns(history, maxContextMessages, transientUserPrompt, "assistant");
            string resolvedModel = ResolveModelName(provider, modelName);

            StringBuilder sb = new StringBuilder();
            sb.Append("{");

            sb.Append("\"model\":");
            SimpleJson.AppendEscaped(sb, resolvedModel);
            sb.Append(",");

            sb.Append("\"messages\":[");
            sb.Append("{\"role\":\"system\",\"content\":");
            SimpleJson.AppendEscaped(sb, systemPrompt);
            sb.Append("}");

            for (int i = 0; i < mergedTurns.Count; i++)
            {
                sb.Append(",{\"role\":");
                SimpleJson.AppendEscaped(sb, mergedTurns[i].Key);
                sb.Append(",\"content\":");
                SimpleJson.AppendEscaped(sb, mergedTurns[i].Value);
                sb.Append("}");
            }
            sb.Append("],");

            float clampedTemp = Mathf.Clamp(temperature, 0.1f, 1.5f);
            sb.Append("\"temperature\":");
            sb.Append(clampedTemp.ToString("0.00", CultureInfo.InvariantCulture));
            sb.Append(",\"max_tokens\":2048");

            if (provider == ApiProvider.Groq)
            {
                sb.Append(",\"response_format\":{\"type\":\"json_object\"}");
            }
            else if (provider == ApiProvider.LocalOrCustom)
            {
                sb.Append(",\"response_format\":{\"type\":\"json_schema\",\"json_schema\":{\"name\":\"kks_reply\",\"strict\":true,\"schema\":{\"type\":\"object\",\"properties\":{\"emotion\":{\"type\":\"string\",\"enum\":[\"neutral\",\"happy\",\"excited\",\"shy\",\"flirty\",\"surprised\",\"angry\",\"sad\",\"serious\"]},\"dialogue\":{\"type\":\"string\"},\"favorDelta\":{\"type\":\"integer\"},\"lewdnessDelta\":{\"type\":\"integer\"},\"invitation\":{\"type\":\"string\",\"enum\":");
                AppendInvitationEnumSchema(sb, allowedInvitations);
                sb.Append("}},\"required\":[\"emotion\",\"dialogue\",\"favorDelta\",\"lewdnessDelta\",\"invitation\"],\"additionalProperties\":false}}}");
            }

            sb.Append("}");
            return sb.ToString();
        }

        private static GeminiReply ParseStructuredReply(string rawText, List<string> allowedInvitations)
        {
            GeminiReply result = new GeminiReply();
            result.Dialogue = rawText.Trim();
            result.Emotion = "neutral";
            result.FavorDelta = 0;
            result.LewdnessDelta = 0;
            result.Invitation = "none";

            string cleaned = StripMarkdownFences(rawText);

            try
            {
                Dictionary<string, object> obj = SimpleJson.Parse(cleaned) as Dictionary<string, object>;
                if (obj != null)
                {
                    result.Dialogue = "";

                    string[] dialogueKeys = new string[] { "dialogue", "response", "message", "text" };
                    for (int i = 0; i < dialogueKeys.Length; i++)
                    {
                        string k = dialogueKeys[i];
                        if (obj.ContainsKey(k) && obj[k] is string)
                        {
                            string dlg = ((string)obj[k]).Trim();
                            if (!string.IsNullOrEmpty(dlg))
                            {
                                result.Dialogue = dlg;
                                break;
                            }
                        }
                    }

                    if (obj.ContainsKey("emotion") && obj["emotion"] is string)
                    {
                        string emo = ((string)obj["emotion"]).Trim().ToLowerInvariant();
                        if (!string.IsNullOrEmpty(emo))
                        {
                            result.Emotion = emo;
                        }
                    }

                    result.FavorDelta = ParseClampedDelta(obj, "favorDelta", -50, 50);
                    if (result.FavorDelta == 0)
                    {
                        result.FavorDelta = ParseClampedDelta(obj, "favourDelta", -50, 50);
                    }

                    result.LewdnessDelta = ParseClampedDelta(obj, "lewdnessDelta", -50, 50);

                    if (obj.ContainsKey("invitation") && obj["invitation"] is string)
                    {
                        result.Invitation = NormalizeInvitationKey((string)obj["invitation"], allowedInvitations);
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private static string NormalizeInvitationKey(string rawInv, List<string> allowedInvitations)
        {
            if (string.IsNullOrEmpty(rawInv) || allowedInvitations == null || allowedInvitations.Count == 0)
            {
                return "none";
            }

            string inv = rawInv.Trim().ToLowerInvariant();
            if (inv == "none" || inv == "null" || inv == "no" || inv == "ninguna")
            {
                return "none";
            }

            if (allowedInvitations.Contains(inv))
            {
                return inv;
            }

            string mapped = "";
            if (inv.Contains("eat") || inv.Contains("comer") || inv.Contains("food") || inv.Contains("lunch") || inv.Contains("dinner") || inv.Contains("almuerz") || inv.Contains("cena") || inv.Contains("desayun"))
                mapped = "eat";
            else if (inv.Contains("experience") || inv.Contains("kayak") || inv.Contains("activ") || inv.Contains("experienc"))
                mapped = "experience";
            else if (inv.Contains("play") || inv.Contains("jugar") || inv.Contains("beach") || inv.Contains("playa") || inv.Contains("swim") || inv.Contains("nadar"))
                mapped = "play";
            else if (inv.Contains("sightseeing") || inv.Contains("paseo") || inv.Contains("turism") || inv.Contains("tour") || inv.Contains("walk"))
                mapped = "sightseeing";
            else if (inv.Contains("follow") || inv.Contains("acompañ") || inv.Contains("juntos"))
                mapped = "follow";
            else if (inv.Contains("date") || inv.Contains("cita"))
                mapped = "date";
            else if (inv.Contains("room") || inv.Contains("habitaci") || inv.Contains("cuarto") || inv.Contains("hotel"))
                mapped = "room";
            else if (inv.Contains("h_scene") || inv.Contains("hscene") || inv == "h" || inv.Contains("intim") || inv.Contains("sex"))
                mapped = "h_scene";

            if (!string.IsNullOrEmpty(mapped) && allowedInvitations.Contains(mapped))
            {
                return mapped;
            }

            return "none";
        }

        private static int ParseClampedDelta(Dictionary<string, object> obj, string key, int min, int max)
        {
            if (obj == null || !obj.ContainsKey(key) || obj[key] == null)
            {
                return 0;
            }

            int parsed = 0;
            if (obj[key] is double)
            {
                parsed = (int)Math.Round((double)obj[key]);
            }
            else
            {
                int.TryParse(Convert.ToString(obj[key]), out parsed);
            }

            return Mathf.Clamp(parsed, min, max);
        }

        private static string StripMarkdownFences(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            string trimmed = text.Trim();
            if (trimmed.StartsWith("```"))
            {
                int firstNewLine = trimmed.IndexOf('\n');
                if (firstNewLine >= 0)
                {
                    trimmed = trimmed.Substring(firstNewLine + 1);
                }
                if (trimmed.EndsWith("```"))
                {
                    trimmed = trimmed.Substring(0, trimmed.Length - 3);
                }
                trimmed = trimmed.Trim();
            }

            int startBrace = trimmed.IndexOf('{');
            int endBrace = trimmed.LastIndexOf('}');
            if (startBrace >= 0 && endBrace > startBrace)
            {
                return trimmed.Substring(startBrace, endBrace - startBrace + 1);
            }

            return trimmed;
        }

        private static string ExtractErrorMessage(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";

            try
            {
                Dictionary<string, object> root = SimpleJson.Parse(json) as Dictionary<string, object>;
                if (root == null) return "";

                if (root.ContainsKey("error"))
                {
                    Dictionary<string, object> errObj = root["error"] as Dictionary<string, object>;
                    if (errObj != null && errObj.ContainsKey("message"))
                    {
                        return errObj["message"] as string ?? "";
                    }
                    string errStr = root["error"] as string;
                    if (!string.IsNullOrEmpty(errStr)) return errStr;
                }
            }
            catch
            {
            }

            return "";
        }

        private static bool TryExtractOpenAIReplyText(string json, out string replyText, out string errorMsg)
        {
            replyText = "";
            errorMsg = "";

            if (string.IsNullOrEmpty(json))
            {
                errorMsg = "Empty server response.";
                return false;
            }

            try
            {
                Dictionary<string, object> root = SimpleJson.Parse(json) as Dictionary<string, object>;
                if (root == null)
                {
                    errorMsg = "Invalid JSON response from server.";
                    return false;
                }

                if (root.ContainsKey("error"))
                {
                    errorMsg = ExtractErrorMessage(json);
                    return false;
                }

                if (!root.ContainsKey("choices"))
                {
                    errorMsg = "Response missing 'choices' field.";
                    return false;
                }

                List<object> choices = root["choices"] as List<object>;
                if (choices == null || choices.Count == 0)
                {
                    errorMsg = "Empty 'choices' list.";
                    return false;
                }

                Dictionary<string, object> firstChoice = choices[0] as Dictionary<string, object>;
                if (firstChoice == null || !firstChoice.ContainsKey("message"))
                {
                    errorMsg = "Unexpected format in 'choices[0]'.";
                    return false;
                }

                Dictionary<string, object> message = firstChoice["message"] as Dictionary<string, object>;
                if (message == null || !message.ContainsKey("content"))
                {
                    errorMsg = "Response message missing 'content'.";
                    return false;
                }

                string content = message["content"] as string;
                replyText = string.IsNullOrEmpty(content) ? "" : content.Trim();

                if (string.IsNullOrEmpty(replyText))
                {
                    errorMsg = "Model returned empty content.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMsg = "JSON parse error: " + ex.Message;
                return false;
            }
        }

        private static bool TryExtractGeminiReplyText(string json, out string replyText, out string errorMsg)
        {
            replyText = "";
            errorMsg = "";

            if (string.IsNullOrEmpty(json))
            {
                errorMsg = "Empty Gemini response.";
                return false;
            }

            try
            {
                Dictionary<string, object> root = SimpleJson.Parse(json) as Dictionary<string, object>;
                if (root == null)
                {
                    errorMsg = "Invalid JSON response from Gemini.";
                    return false;
                }

                if (root.ContainsKey("error"))
                {
                    errorMsg = ExtractErrorMessage(json);
                    return false;
                }

                if (!root.ContainsKey("candidates"))
                {
                    errorMsg = "No response candidates returned by Gemini.";
                    return false;
                }

                List<object> candidates = root["candidates"] as List<object>;
                if (candidates == null || candidates.Count == 0)
                {
                    errorMsg = "Empty candidates list from Gemini.";
                    return false;
                }

                Dictionary<string, object> firstCandidate = candidates[0] as Dictionary<string, object>;
                if (firstCandidate == null)
                {
                    errorMsg = "Unexpected candidate format.";
                    return false;
                }

                if (!firstCandidate.ContainsKey("content"))
                {
                    string reason = firstCandidate.ContainsKey("finishReason") ? Convert.ToString(firstCandidate["finishReason"]) : "UNKNOWN";
                    errorMsg = "Gemini stopped/blocked response (reason: " + reason + ").";
                    return false;
                }

                Dictionary<string, object> content = firstCandidate["content"] as Dictionary<string, object>;
                if (content == null || !content.ContainsKey("parts"))
                {
                    errorMsg = "Gemini response missing text parts.";
                    return false;
                }

                List<object> parts = content["parts"] as List<object>;
                if (parts == null || parts.Count == 0)
                {
                    errorMsg = "Gemini returned no text.";
                    return false;
                }

                StringBuilder result = new StringBuilder();
                for (int i = 0; i < parts.Count; i++)
                {
                    Dictionary<string, object> part = parts[i] as Dictionary<string, object>;
                    if (part == null) continue;

                    if (part.ContainsKey("thought") && part["thought"] is bool && (bool)part["thought"])
                    {
                        continue;
                    }

                    if (part.ContainsKey("text"))
                    {
                        string t = part["text"] as string;
                        if (!string.IsNullOrEmpty(t))
                        {
                            result.Append(t);
                        }
                    }
                }

                replyText = result.ToString().Trim();
                if (string.IsNullOrEmpty(replyText))
                {
                    errorMsg = "Gemini returned empty visible content.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMsg = "Gemini JSON parse error: " + ex.Message;
                return false;
            }
        }
    }
}