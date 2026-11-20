# KKS AI Chat (v1.1.0)

Bring your **Koikatsu Sunshine** characters to life with dynamic, AI-powered conversations directly inside `TalkScene`. Unlike simple overlay chats, **KKS AI Chat** reads real-time game state, maintains persistent per-character memory, controls live expressions and stats, and triggers native in-game events.

---

## ✨ Features

* **Universal Language Support & Bilingual UI:** Write in **any language** (English, Spanish, Japanese, Russian, Italian, Portuguese, etc.) and characters automatically reply and summarize memories in that exact language. Toggle the mod UI between **English** and **Spanish** in-game (`IdiomaInterfaz`).
* **Visual Novel Mode (`VN Mode`):** Switch on the fly between the full chat log window and a clean, minimalist Visual Novel input bar directly above the dialog box.
* **Accelerated Stat Progression:** Meaningful conversations and gestures dynamically adjust **Favor** and the **H Gauge** from `-50` to `+50` based on context and emotional weight.
* **Proactive H-Scene Proposals:** Once a character's H Gauge crosses the configurable threshold (`UmbralPropuestaH`, default `80/100`), they will proactively ask to start an H scene or head somewhere private.
* **100% Guaranteed Chat Invitations:** Agree on eating, playing, kayaking, sightseeing, dating, or H scenes directly in chat. Includes a Harmony patch so accepted chat proposals are never randomly rejected by base-game RNG.
* **Local AI Optimization (Strict JSON Schema):** Built-in `json_schema` enforcement and emotion-first generation for **KoboldCpp**, **Ollama**, and **LM Studio**—preventing empty replies, broken JSON, or mismatched facial expressions.
* **Persistent Memory & Auto-Summaries:** Each character gets an individual `.json` file in `BepInEx\config\KKS_AIChat_Memories\` with automatic background summarization into long-term key memories.
* **Deep World & Character Awareness:** Reads personality, relationship status, favor, H gauge, outfit, time of day, day of week, location, nearby NPCs, **height difference** relative to the player, and **`KK_Profile`** custom lore.
* **Spontaneous Reactions:** Characters automatically react to touch/look gestures and native game dialogues.

---

## 📦 Requirements

* **Koikatsu Sunshine** with **BepInEx 5** installed.
* **BepInEx ConfigurationManager** (`F1` / `F11` menu, included in most repacks).
* *(Optional)* **KK_Profile** plugin for custom character backstories.

---

## 🚀 Installation

1. Close **Koikatsu Sunshine** if it is running.
2. Download the latest release `.zip` from the [Releases](../../releases) tab.
3. Extract `KKS_AIChat.dll` into:
   ```text
   <Game Folder>\BepInEx\plugins\
