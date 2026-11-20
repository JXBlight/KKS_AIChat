====================================================================
                  KKS AI CHAT (v1.0.0)
         AI Conversation Mod for Koikatsu Sunshine
====================================================================

1. DESCRIPTION
--------------------------------------------------------------------
KKS AI Chat lets you have dynamic, AI-powered conversations with
characters in Koikatsu Sunshine during TalkScene interactions.

Key Features:
- Persistent per-character memory with automatic long-term summaries
  (stored in BepInEx\config\KKS_AIChat_Memories\).
- Automatic Multilingual AI: Characters reply and summarize memories
  in ANY language you type in (English, Spanish, Japanese, Russian,
  Italian, Portuguese, etc.).
- Bilingual UI: Switch the chat interface between English and Spanish
  anytime via "IdiomaInterfaz" in F1/F11.
- Visual Novel Mode: Toggle between the full chat log window and a
  clean, minimalist input bar ("VN Mode" / "Log").
- Real-time awareness of personality, relationship status, favor,
  lewdness (H gauge), outfits, time of day, current location, nearby
  people, and height difference relative to the player.
- Supports custom character lore via KK_Profile (if installed).
- Live control over facial expressions, poses, favor, and H gauge.
- Dynamic invitations to eat, hang out, do activities, or start H
  scenes directly from chat (with guaranteed acceptance once agreed).
- Spontaneous reactions to touch/look gestures and native dialogues.
- Multi-provider support: OpenRouter, Groq, Google Gemini, and
  local models (KoboldCpp / Ollama / LM Studio).


2. REQUIREMENTS
--------------------------------------------------------------------
- Koikatsu Sunshine with BepInEx 5 installed.
- BepInEx ConfigurationManager (included in most repacks; opened
  with F1 or F11).
- (Optional) KK_Profile plugin for detailed custom character bios.


3. INSTALLATION
--------------------------------------------------------------------
1. Close Koikatsu Sunshine if it is running.
2. Copy "KKS_AIChat.dll" into:
   <Game Folder>\BepInEx\plugins\
3. Launch the game normally. The mod will automatically create its
   config file and memory folder on startup.


4. API CONFIGURATION (REQUIRED BEFORE FIRST USE)
--------------------------------------------------------------------
In-game, press F1 (or F11) to open the plugin manager and look for
"KKS AI Chat". Configure it based on your preferred provider:

[Option A: OpenRouter - Recommended (Free)]
- Create an account and generate a key at: https://openrouter.ai/keys
- Proveedor (Provider): OpenRouter
- ApiKey:               sk-or-v1-... (your key)
- Model:                openrouter/free
  (Automatically routes to an available free model on every turn)

[Option B: Groq (Free & Ultra-Fast)]
- Generate a key at: https://console.groq.com
- Proveedor (Provider): Groq
- ApiKey:               gsk_... (your key)
- Model:                llama-3.3-70b-versatile
  (Note: Groq may require a VPN in certain regions)

[Option C: Google Gemini]
- Generate a key at: https://aistudio.google.com/apikey
- Proveedor (Provider): Gemini
- ApiKey:               AIza... (your key)
- Model:                gemini-3-flash-preview

[Option D: Local Models - 100% Free, Offline & Unlimited]
- Requires KoboldCpp, Ollama, or LM Studio running on your PC.
- Proveedor (Provider): LocalOrCustom
- ApiKey:               (leave blank)
- Model:
  * KoboldCpp: koboldcpp (automatically uses your loaded GGUF model)
  * Ollama / LM Studio: Exact name of your loaded model (e.g. llama3.1)
- UrlPersonalizada (Custom URL):
  * KoboldCpp: http://localhost:5001/v1/chat/completions
  * Ollama:    http://localhost:11434/v1/chat/completions
  * LM Studio: http://localhost:1234/v1/chat/completions
- Tip for KoboldCpp: Set Context Size to at least 4096 (8192 is
  recommended) and use an Instruct/Roleplay model (7B-8B+ Q4_K_M)
  for best JSON emotion, gauge, and invitation support.


5. HOW TO USE
--------------------------------------------------------------------
- Start a normal conversation with any character.
- Press F8 to open or close the chat window (you can rebind this
  under "TeclaChat" in the F1/F11 menu).
- Use the "VN Mode" / "Log" button to switch between the full window
  and the minimalist Visual Novel input bar.
- Type your message and press Enter to send (Shift + Enter for a
  new line).
- Exiting TalkScene back to free roam automatically closes the chat.


6. QUICK TROUBLESHOOTING
--------------------------------------------------------------------
- Error 401 (Unauthorized): Your API key is missing or invalid.
- Error 403 (Forbidden): The provider blocks your region (use a VPN
  or switch to OpenRouter / LocalOrCustom).
- Error 429 (Rate Limit) or 503 (High Demand): The free model is
  temporarily busy. Your unsent message is automatically restored
  to the input box; wait a few seconds and retry, or increase
  "CooldownReaccionAuto" in F1/F11 to save requests.
- Character memories are saved as .json files in:
  BepInEx\config\KKS_AIChat_Memories\
  You can freely inspect, edit, or delete them at any time.