using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace KKS_AIChat
{
    public class ChatMessage
    {
        public string Role; // "user", "model" o "system"
        public string Text;
        public string DisplaySender;
        public string Timestamp;

        public ChatMessage(string role, string text, string displaySender)
            : this(role, text, displaySender, DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
        {
        }

        public ChatMessage(string role, string text, string displaySender, string timestamp)
        {
            Role = role;
            Text = text;
            DisplaySender = displaySender;
            Timestamp = string.IsNullOrEmpty(timestamp) ? DateTime.Now.ToString("yyyy-MM-dd HH:mm") : timestamp;
        }
    }

    public class CharacterMemory
    {
        public string UniqueId;
        public string CharacterName;
        public string LastUpdated;
        public int LastSummarizedMsgCount = 0;
        public List<string> KeyMemories = new List<string>();
        public List<string> RecentGameEvents = new List<string>();
        public List<ChatMessage> Messages = new List<ChatMessage>();

        public int CountSavedDialogMessages()
        {
            int count = 0;
            for (int i = 0; i < Messages.Count; i++)
            {
                if (Messages[i].Role == "user" || Messages[i].Role == "model")
                {
                    count++;
                }
            }
            return count;
        }

        public List<ChatMessage> GetDialogMessagesOnly()
        {
            List<ChatMessage> list = new List<ChatMessage>();
            for (int i = 0; i < Messages.Count; i++)
            {
                if (Messages[i].Role == "user" || Messages[i].Role == "model")
                {
                    list.Add(Messages[i]);
                }
            }
            return list;
        }
    }

    public static class MemoryManager
    {
        private const int MaxStoredMessages = 120;
        private const int MaxStoredEvents = 40;
        private const int MaxRecentGameEvents = 12;

        public static string MemoriesFolder
        {
            get { return Path.Combine(Paths.ConfigPath, "KKS_AIChat_Memories"); }
        }

        public static CharacterMemory LoadOrCreate(string uniqueId, string characterName)
        {
            EnsureDirectory();

            string safeId = GetShortSafeId(uniqueId);
            string existingPath = FindExistingFile(safeId);

            if (!string.IsNullOrEmpty(existingPath) && File.Exists(existingPath))
            {
                try
                {
                    string json = File.ReadAllText(existingPath, Encoding.UTF8);
                    CharacterMemory loaded = DeserializeMemory(json, uniqueId, characterName);
                    if (loaded != null)
                    {
                        if (!string.IsNullOrEmpty(characterName))
                        {
                            loaded.CharacterName = characterName;
                        }
                        return loaded;
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning("[KKS AI Chat] No se pudo leer el archivo de memoria (" + existingPath + "): " + ex.Message);
                }
            }

            CharacterMemory fresh = new CharacterMemory();
            fresh.UniqueId = uniqueId;
            fresh.CharacterName = characterName;
            fresh.LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return fresh;
        }

        public static void Save(CharacterMemory memory)
        {
            if (memory == null || string.IsNullOrEmpty(memory.UniqueId))
            {
                return;
            }

            try
            {
                EnsureDirectory();
                memory.LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                string safeId = GetShortSafeId(memory.UniqueId);
                string filePath = FindExistingFile(safeId);

                if (string.IsNullOrEmpty(filePath))
                {
                    string safeName = SanitizeFilePart(memory.CharacterName, 24);
                    if (string.IsNullOrEmpty(safeName))
                    {
                        safeName = "Personaje";
                    }
                    filePath = Path.Combine(MemoriesFolder, safeName + "_" + safeId + ".json");
                }

                string json = SerializeMemory(memory);
                File.WriteAllText(filePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[KKS AI Chat] Error al guardar memoria en disco: " + ex.Message);
            }
        }

        public static void AddKeyMemory(CharacterMemory memory, string eventDescription)
        {
            if (memory == null || string.IsNullOrEmpty(eventDescription))
            {
                return;
            }

            string entry = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "] " + eventDescription.Trim();
            memory.KeyMemories.Add(entry);

            while (memory.KeyMemories.Count > MaxStoredEvents)
            {
                memory.KeyMemories.RemoveAt(0);
            }

            Save(memory);
        }

        public static void AddRecentGameEvent(CharacterMemory memory, string eventText)
        {
            if (memory == null || string.IsNullOrEmpty(eventText))
            {
                return;
            }

            string clean = eventText.Trim();
            if (string.IsNullOrEmpty(clean))
            {
                return;
            }

            string entry = "[" + DateTime.Now.ToString("HH:mm") + "] " + clean;
            memory.RecentGameEvents.Add(entry);

            while (memory.RecentGameEvents.Count > MaxRecentGameEvents)
            {
                memory.RecentGameEvents.RemoveAt(0);
            }
        }

        private static void EnsureDirectory()
        {
            if (!Directory.Exists(MemoriesFolder))
            {
                Directory.CreateDirectory(MemoriesFolder);
            }
        }

        private static string FindExistingFile(string safeId)
        {
            try
            {
                string[] matches = Directory.GetFiles(MemoriesFolder, "*_" + safeId + ".json");
                if (matches != null && matches.Length > 0)
                {
                    return matches[0];
                }
            }
            catch
            {
            }

            return null;
        }

        private static string GetShortSafeId(string uniqueId)
        {
            string cleaned = SanitizeFilePart(uniqueId, 36);
            if (string.IsNullOrEmpty(cleaned))
            {
                return "unknown";
            }

            return cleaned;
        }

        private static string SanitizeFilePart(string input, int maxLength)
        {
            if (string.IsNullOrEmpty(input))
            {
                return "";
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (char.IsWhiteSpace(c))
                {
                    sb.Append('_');
                    continue;
                }

                bool invalid = false;
                for (int j = 0; j < invalidChars.Length; j++)
                {
                    if (c == invalidChars[j])
                    {
                        invalid = true;
                        break;
                    }
                }

                if (!invalid && c != '.' && c != '*' && c != '?')
                {
                    sb.Append(c);
                }
            }

            string result = sb.ToString().Trim('_');
            if (result.Length > maxLength)
            {
                result = result.Substring(0, maxLength);
            }

            return result;
        }

        private static string SerializeMemory(CharacterMemory memory)
        {
            List<ChatMessage> toSave = memory.GetDialogMessagesOnly();
            int startMsg = Math.Max(0, toSave.Count - MaxStoredMessages);
            int savedCount = toSave.Count - startMsg;

            // Ajustar el contador si se recortaron mensajes antiguos del límite máximo
            int adjustedSummarized = Math.Min(memory.LastSummarizedMsgCount, savedCount);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");

            sb.Append("  \"uniqueId\": ");
            SimpleJson.AppendEscaped(sb, memory.UniqueId);
            sb.AppendLine(",");

            sb.Append("  \"characterName\": ");
            SimpleJson.AppendEscaped(sb, memory.CharacterName);
            sb.AppendLine(",");

            sb.Append("  \"lastUpdated\": ");
            SimpleJson.AppendEscaped(sb, memory.LastUpdated);
            sb.AppendLine(",");

            sb.AppendLine("  \"lastSummarizedMsgCount\": " + adjustedSummarized + ",");

            sb.AppendLine("  \"keyMemories\": [");
            for (int i = 0; i < memory.KeyMemories.Count; i++)
            {
                sb.Append("    ");
                SimpleJson.AppendEscaped(sb, memory.KeyMemories[i]);
                if (i < memory.KeyMemories.Count - 1)
                {
                    sb.Append(",");
                }
                sb.AppendLine();
            }
            sb.AppendLine("  ],");

            sb.AppendLine("  \"messages\": [");
            for (int i = startMsg; i < toSave.Count; i++)
            {
                ChatMessage msg = toSave[i];
                sb.Append("    { \"role\": ");
                SimpleJson.AppendEscaped(sb, msg.Role);
                sb.Append(", \"sender\": ");
                SimpleJson.AppendEscaped(sb, msg.DisplaySender);
                sb.Append(", \"timestamp\": ");
                SimpleJson.AppendEscaped(sb, msg.Timestamp);
                sb.Append(", \"text\": ");
                SimpleJson.AppendEscaped(sb, msg.Text);
                sb.Append(" }");

                if (i < toSave.Count - 1)
                {
                    sb.Append(",");
                }
                sb.AppendLine();
            }
            sb.AppendLine("  ]");

            sb.AppendLine("}");
            return sb.ToString();
        }

        private static CharacterMemory DeserializeMemory(string json, string fallbackId, string fallbackName)
        {
            Dictionary<string, object> root = SimpleJson.Parse(json) as Dictionary<string, object>;
            if (root == null)
            {
                return null;
            }

            CharacterMemory memory = new CharacterMemory();
            memory.UniqueId = GetDictString(root, "uniqueId", fallbackId);
            memory.CharacterName = GetDictString(root, "characterName", fallbackName);
            memory.LastUpdated = GetDictString(root, "lastUpdated", "");
            memory.LastSummarizedMsgCount = GetDictInt(root, "lastSummarizedMsgCount", 0);

            if (root.ContainsKey("keyMemories"))
            {
                List<object> eventsList = root["keyMemories"] as List<object>;
                if (eventsList != null)
                {
                    for (int i = 0; i < eventsList.Count; i++)
                    {
                        string ev = eventsList[i] as string;
                        if (!string.IsNullOrEmpty(ev))
                        {
                            memory.KeyMemories.Add(ev);
                        }
                    }
                }
            }

            if (root.ContainsKey("messages"))
            {
                List<object> msgList = root["messages"] as List<object>;
                if (msgList != null)
                {
                    for (int i = 0; i < msgList.Count; i++)
                    {
                        Dictionary<string, object> item = msgList[i] as Dictionary<string, object>;
                        if (item == null) continue;

                        string role = GetDictString(item, "role", "");
                        string text = GetDictString(item, "text", "");
                        if ((role == "user" || role == "model") && !string.IsNullOrEmpty(text))
                        {
                            string defaultSender = role == "user" ? "Tú" : memory.CharacterName;
                            string sender = GetDictString(item, "sender", defaultSender);
                            string timestamp = GetDictString(item, "timestamp", "");
                            memory.Messages.Add(new ChatMessage(role, text, sender, timestamp));
                        }
                    }
                }
            }

            return memory;
        }

        private static string GetDictString(Dictionary<string, object> dict, string key, string fallback)
        {
            if (dict != null && dict.ContainsKey(key))
            {
                string val = dict[key] as string;
                if (!string.IsNullOrEmpty(val))
                {
                    return val;
                }
            }

            return fallback;
        }

        private static int GetDictInt(Dictionary<string, object> dict, string key, int fallback)
        {
            if (dict != null && dict.ContainsKey(key) && dict[key] != null)
            {
                if (dict[key] is double)
                {
                    return (int)Math.Round((double)dict[key]);
                }
                int parsed;
                if (int.TryParse(Convert.ToString(dict[key]), out parsed))
                {
                    return parsed;
                }
            }

            return fallback;
        }
    }

    internal static class SimpleJson
    {
        public static void AppendEscaped(StringBuilder sb, string text)
        {
            sb.Append('"');
            if (!string.IsNullOrEmpty(text))
            {
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < 32)
                            {
                                sb.Append("\\u");
                                sb.Append(((int)c).ToString("x4"));
                            }
                            else
                            {
                                sb.Append(c);
                            }
                            break;
                    }
                }
            }
            sb.Append('"');
        }

        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            Parser parser = new Parser(json);
            return parser.ParseValue();
        }

        private sealed class Parser
        {
            private readonly string json;
            private int pos;

            public Parser(string json)
            {
                this.json = json;
                this.pos = 0;
            }

            private void SkipWhitespace()
            {
                while (pos < json.Length && char.IsWhiteSpace(json[pos]))
                {
                    pos++;
                }
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (pos >= json.Length) return null;

                char c = json[pos];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return ParseString();
                if (c == 't' && pos + 4 <= json.Length) { pos += 4; return true; }
                if (c == 'f' && pos + 5 <= json.Length) { pos += 5; return false; }
                if (c == 'n' && pos + 4 <= json.Length) { pos += 4; return null; }
                return ParseNumber();
            }

            private Dictionary<string, object> ParseObject()
            {
                Dictionary<string, object> dict = new Dictionary<string, object>();
                pos++;

                while (pos < json.Length)
                {
                    SkipWhitespace();
                    if (pos >= json.Length) break;
                    if (json[pos] == '}')
                    {
                        pos++;
                        break;
                    }

                    int beforePos = pos;
                    if (json[pos] != '"') break;

                    string key = ParseString();
                    SkipWhitespace();
                    if (pos < json.Length && json[pos] == ':')
                    {
                        pos++;
                    }

                    object value = ParseValue();
                    dict[key] = value;

                    SkipWhitespace();
                    if (pos < json.Length && json[pos] == ',')
                    {
                        pos++;
                    }

                    if (pos == beforePos) break;
                }

                return dict;
            }

            private List<object> ParseArray()
            {
                List<object> list = new List<object>();
                pos++;

                while (pos < json.Length)
                {
                    SkipWhitespace();
                    if (pos >= json.Length) break;
                    if (json[pos] == ']')
                    {
                        pos++;
                        break;
                    }

                    int beforePos = pos;
                    list.Add(ParseValue());

                    SkipWhitespace();
                    if (pos < json.Length && json[pos] == ',')
                    {
                        pos++;
                    }

                    if (pos == beforePos) break;
                }

                return list;
            }

            private string ParseString()
            {
                StringBuilder sb = new StringBuilder();
                pos++;

                while (pos < json.Length)
                {
                    char c = json[pos++];
                    if (c == '"')
                    {
                        break;
                    }

                    if (c == '\\' && pos < json.Length)
                    {
                        char esc = json[pos++];
                        switch (esc)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (pos + 4 <= json.Length)
                                {
                                    string hex = json.Substring(pos, 4);
                                    pos += 4;
                                    int code;
                                    if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                    {
                                        sb.Append((char)code);
                                    }
                                }
                                break;
                            default:
                                sb.Append(esc);
                                break;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }

                return sb.ToString();
            }

            private object ParseNumber()
            {
                int start = pos;
                while (pos < json.Length && "0123456789+-.eE".IndexOf(json[pos]) >= 0)
                {
                    pos++;
                }

                if (pos == start)
                {
                    pos++;
                    return 0.0;
                }

                string numStr = json.Substring(start, pos - start);
                double val;
                if (double.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out val))
                {
                    return val;
                }

                return 0.0;
            }
        }
    }
}