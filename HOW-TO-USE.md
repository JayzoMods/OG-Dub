# How to use OG Dub

Publisher: OG Digital Designs. Australian English.

Park the boombox on the desk. Punch **Record** for the deck (the default) or **Edit** for optional local-model tape rewrite. Punch **How to** next to Terms for a spotlight tour of the deck. Scan to an app that is making sound. The KEEP timer fills to 15 seconds. Punch **KEEP** to drop that riff in the crate, or **Rec** for a full C-60 side. Rec first counts **3-2-1** on the LCD; the tape does not roll until the leader finishes (Stop cancels). An amber CLIP lamp lights when the aimed peak hits the ceiling. Tick **Live voice**, pick your mic in the list under it, punch **MIC ON** to hear yourself in Play-to, and watch the **MIC** meter — the voice take is a second track (not mixed into the app tape, padded −12 dB). When you Stop (or the side runs out), a cassette drops into the crate with a Broadcast Wave `bext` tag and an integrated loudness readout. **Per song** (on by default) ejects and starts a fresh cassette when Windows reports a new track title. Pick **Play to** for headphones vs speakers. Play Side A. **Flip** plays the live voice take. **Mark** while Rec is on punches a 000–999 counter; **Cue** jumps to the next mark on Play (Side A only). Dub the two newest tapes onto a mixtape. Files live in Documents under OG Digital Designs\OG Dub, or a folder you pick with **FOLDER**.

It is not a stream ripper. Only record audio you are allowed to record.

## Steps

1. Accept the terms. Decline exits.
2. Leave **Top** ticked if you want the deck above other windows. Leave **Hotkeys** ticked for Ctrl+Alt+R Rec and Ctrl+Alt+S Stop. While the boombox is focused, **R** Rec, **K** KEEP, **Esc** Stop, **P** Play.
3. **Scan** or pick a station. **Whole mix** records everything except this boombox when process loopback works; otherwise it records the Windows mix and says so. Punch **How to** for a walkthrough that highlights each control.
4. **KEEP** — dumps the last 15 seconds of the aimed app into the crate. The LCD timer fills from 00:00 to 00:15 while the buffer arms. Transport keys are silent so they do not appear on the tape.
5. **Rec** — the LCD counts **3**, **2**, **1** (one second each). The WAV does not start until that leader finishes, and no countdown tone is baked into the file. Stop during 3-2-1 cancels. Then the LCD counts down 30:00 and the tape counter runs 000–999 over that C-60 side. An amber CLIP lamp lights if the aimed peak hits 0.99. Leave **Per song** ticked to eject and start a new cassette when the aimed app publishes a new track title (no extra 3-2-1 on those rolls). Untick it for one long take (a mix, radio, a stream). Tick **Live voice** and choose the microphone in the list under it — the **MIC** meter shows live input for that device. Punch **MIC ON** to hear yourself in Play-to (**MIC OFF** mutes the monitor; Rec still lays the voice take). The voice take is a second WAV, not mashed into Side A. The mic is captured as 32-bit float when Windows gives float, with a −12 dB pad. **Mark** punches the current counter onto this take (up to 99 marks). Transport keys are silent so they do not appear on the tape.
6. **Stop** ejects the current cassette. If the aimed app published a Windows now-playing title, that song and artist go on the J-card with album art in the crate. Silence (including protected/DRM audio) still ejects as nothing playing — the LCD says **PROTECTED** if Windows named a track that did not land on the tape. The voice take still saves if the mic heard something. Status and the crate show integrated loudness in LUFS (ITU-R BS.1770). The WAV gets a Broadcast Wave `bext` chunk (originator OG Digital Designs, Sydney date/time). Marks are stored on the cassette and as BWF `cue` / `LIST adtl` chunks. Playback is not auto-normalised.
7. Click a cassette, **Play** (Side A), or double-click the tile. Use **Play to** to send it to headphones or speakers (WASAPI). **Flip** plays the live voice take when the crate shows a **B**. **Cue** jumps to the next mark on Side A, then wraps to the first. **Dub** concatenates the two newest Side A cassettes with 2 second gaps and tags the mixtape (no marks copied). **Aa** renames the J-card. Star copies into Favorites (click again to unfavourite). Trash asks before delete. Drag a tile onto Explorer to copy the WAV.
8. Click **CRATE** to open the library folder, **FAV** for Favorites, **FOLDER** to choose a different crate location.
9. **Edit** (optional) — punch the **Edit** tab. OG Dub probes localhost only (Ollama `:11434`, LM Studio-style `:1234`, LocalAI `:8080`, plus a loopback URL you type). Chat with a chat model. If a local **audio** runtime is up, Send rewrites the selected crate cassette into a **new** take. The original is not overwritten. **NO LOCAL MODEL** means nothing is listening — Record still works. Cancel aborts one job. Use headphones; GPU load is the model server, not OG Dub.

## What it will not do

- Isolate one Chrome tab from another (same process).
- Capture exclusive-mode audio (some games).
- Invent a song when Windows hands back silence. A blank or missing now-playing title does not start a new tape.
- Mix the microphone into the app tape. The live voice take is a separate file.
- Auto-match playback loudness to a target LUFS.
- Bake a 3-2-1 beep onto the cassette. The leader is LCD only.
- Transcribe as a product, add tape hiss to the file, or mix other apps’ volume.
- Call a cloud API or upload a cassette. Edit is localhost only.

## First launch

Accept the terms or the app exits. If the terms version changes, you will be asked again. **Terms** on the boombox reopens them. **How to** runs the interactive tour. Esc closes the tour.
