# KKS_AIChat
Adds real-time AI conversations directly inside the game's TalkScene (which is used during gameplay/Free Mode, unlike Studio-only mods)

- Per-Character Persistent Memory & Auto-Summaries: Every character gets their own `.json` memory file (`BepInEx\config\KKS_AIChat_Memories\`). Conversations are automatically summarized in the background into long-term memories so characters never forget important details.

- Deep World & Character Awareness: Characters know their personality, relationship status, favor, H gauge (lewdness), current outfit, time of day, location, whether other people are nearby, and even their height difference* relative to the player.

- KK_Profile Support: Automatically reads custom character lore/backstories from `KK_Profile` if installed. This is your character sheet. 

- Live Reactions in TalkScene: Responses dynamically control facial expressions, blush, poses, Favor, and the H Gauge* in real time.

- Dynamic Invitations: Depending on the flow of the conversation, characters can invite you to eat, hang out, do activities, or start an H scene with clean Accept / Reject* buttons.

- Harmony World Hooks: Characters spontaneously react to touch/look gestures, native game dialogues, post-H scenes, and embarrassing/private encounters.

- Multi-Provider Support (BYOK): Works with OpenRouter (free tier available), Groq, Google Gemini, or 100% Free & Offline Local Models* via Ollama / LM Studio.

- Smooth UX: Automatically closes when exiting `TalkScene` and restores your unsent message to the input box if an API rate limit occurs.

## License

**KKS_AIChat** © 2026 by **JXBlight** is licensed under the [Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International (CC BY-NC-SA 4.0)](https://creativecommons.org).

* **Attribution:** You must give appropriate credit, provide a link to the license, and indicate if changes were made.
* **NonCommercial:** You may not use this mod or any derivative works for commercial purposes.
* **ShareAlike:** If you remix, transform, or build upon this project, you must distribute your contributions under the same license.
