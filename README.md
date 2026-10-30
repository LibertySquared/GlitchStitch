# GlitchStich

GlitchStich is a native Windows video review and editing prototype. It uses a familiar editor workspace: a preview, multi-track timeline, audio waveform rows, cue review controls, and a trim range.

## Public testing and feedback

Download the Windows x64 testing ZIP from [GitHub Releases](https://github.com/LibertySquared/GlitchStitch/releases). Extract the entire ZIP to a writable folder and run **GlitchStich.exe**. The release ZIP includes the .NET runtime; keep its files together and read `START-HERE.txt` for detector/Ollama setup. GitHub's source-code ZIPs are not runnable app packages.

To create a clean, self-contained download from committed source, run `powershell -ExecutionPolicy Bypass -File tools/Package-WindowsRelease.ps1`. It excludes local user data, verifies the extracted ZIP, and writes the ZIP and SHA-256 checksum under `release-assets`. This does not replace your local installed app. The developer publishing workflow below creates a smaller framework-dependent package instead.

This repository contains the current app source, artwork, notification/intro audio, setup scripts, and regression checks. It is an early testing build: review suggested glitches and draft stitches before applying or exporting them. Detector accuracy and AI suggestions vary by recording. Approved preferences personalize suggestions; they do not retrain pretrained model weights.

Requirements: Windows x64, the .NET 9 Desktop Runtime to run a published build, and the .NET 9 SDK to build one. Setup downloads the playback runtime and optional detector dependencies/models. Glitchy requires a running local Ollama service with a suitable installed model; a fresh checkout does not include the developer's installed models. YouTube is optional and requires your own Google desktop OAuth setup.

Build from source using the steps below; the `portable-release` app package is not committed to source history. The repository name is **GlitchStitch**; the current app, executable, and project extension retain **GlitchStich** for compatibility.

Report bugs and feature requests through [GitHub Issues](https://github.com/LibertySquared/GlitchStitch/issues). Include reproduction steps, expected and actual behavior, Windows/GPU details, and the commit or build tested. Do not post credentials, OAuth client files, tokens, saved projects, chat logs, or private paths. Share a minimal recording only if you have permission to publish it.

Recordings, extracted WAVs, project saves, local preferences, credentials, model downloads, caches and generated check results are excluded from Git. Only the two app sound effects under `Resources/Audio` are included as audio assets. Third-party components and media retain their upstream terms; the repository's MIT license does not relicense them. See the notices under `Resources` and `tools`, including `Resources/Audio/DraftReady-source.txt` for the Samsung whistle's provenance.

## GlitchStich identity and terminology

The application and desktop shortcut are now **GlitchStich**. Video clips are **glitches**; edits and assembled timelines are **stitches**. The 6.2-second opening ident keeps the blocky camera-headed mascot and layered cyberpunk jacket. The character runs in from a real perspective distance along an elevated neon runway, performs one somersault over a hazard-marked crate, then launches into an airborne corkscrew across an actual gap onto a separate landing deck. The camera initially looks down the course with the projection wall outside the shot, then pans toward the wall as the character lands and projects **GlitchStich**. Sound effects follow the approach, obstacle vault, gap jump, landing and projector. The editor initializes separately; the intro has a 6.5-second startup watchdog and stops its animation/sound when closed.
New stitch files use `.glitchstich`; existing `.reelgood` files still open with the same project format. The source folder, managed assembly/resource identifiers, model directories and `ReelGoodData` location remain stable to preserve media, preferences and installed detectors. `tools/Publish-ReelGood.ps1` packages **GlitchStich.exe**, verifies the update, then removes the previous ReelGood executable. `tools/Install-DesktopShortcut.ps1` creates GlitchStich.lnk and removes only the old shortcut pointing to this same app.
## Glitchy companion and screen bounds

Glitchy replaces the two Copilot launch buttons. The same 3D mascot used in the intro hangs out below the timeline, alternates between a handheld game and cards, and responds to clicks or Enter/Space by jumping into the chat window. Closing chat animates the return jump. Replies and occasional automation reminders use the same dismissible speech-bubble style; dismissing a reminder postpones more reminders for ten minutes, and the character's right-click menu can pause them. Glitchy remains clickable during processing, while edit controls stay protected. Suggestions still require Apply; the local model, review pipeline and saved preferences retain their compatible internal names.

Animation runs at a bounded 20 updates per second and pauses when the companion is hidden, minimized or unloaded. Flight overlays are click-through, do not take focus, and are removed after completion or cancellation. The accepted platformer intro is preserved, with its assistant label renamed Glitchy.

Borderless editor/chat windows use the current monitor's native work area, including taskbar space, monitor offsets and DPI changes. The layout reserves a visible companion row. Scan settings scroll independently above the pinned Start scan action so adding the companion does not hide that action.
## Run the standalone Windows app

Open `portable-release\GlitchStich.exe`. Keep the files in the `portable-release` folder together. FFmpeg and FFprobe are embedded in GlitchStich and installed automatically to `ReelGoodData\tools` beside the EXE the first time it starts. You do not need to select FFmpeg or keep a separate tools folder beside the app. This build requires the .NET 9 Desktop Runtime.

Keep the Resources\LaughDetection folder beside the EXE; it contains the first-run model setup files. Runtime files, FFmpeg, settings, and review preferences are stored in `ReelGoodData` beside the EXE so the large model install stays on the same drive as the app.

## Build from source

1. Run `powershell -ExecutionPolicy Bypass -File tools\Install-FFmpeg.ps1` to download FFmpeg 9.0.2 Essentials and verify its SHA-256 before extracting the binaries into the build inputs.
2. Run `powershell -ExecutionPolicy Bypass -File tools\Publish-ReelGood.ps1` with the .NET 9 SDK on Windows. This publishes to `portable-release`, preserves `ReelGoodData`, and removes generated build copies so there is one app folder to open.
3. The published app embeds FFmpeg and FFprobe and requires the .NET 9 Desktop Runtime. The app folder keeps runtime files beside the executable to avoid temporary single-file extraction at launch.

FFmpeg executables and build output are intentionally excluded from Git history. The setup script restores the local FFmpeg tools.

## Current features

- Import and preview video files.
- Use the always-visible top toolbar for import, audio extraction, combined scan, recording, and timeline export. A top progress strip shows when GlitchStich is processing, and Glitchy has a dedicated space at the bottom of the editor.
- Build a cached 720p/30 fps **Smooth preview** to reduce playback stutter. This changes only playback; scanning and export still use the original video.
- Inspect separate audio streams, extract each as WAV, and display separate waveform rows.
- Select editing modes, optionally enter a prompt, and choose **Start scan** beneath the prompt to search each audio track and compare sampled video frames against the same description. Results appear in two in-app, clickable lists: prompt categories and matching bookmarks. Select a category to browse its moments, then jump to, keep, or skip a bookmark.
- Optionally enter a game title to focus visual matches on that game's footage, or select **IRL / real life** to ignore the title and switch to real-world visual prompts.
- Use **Extract audio** separately to export each audio track as WAV and show independent waveform rows.
- Click a video or WAV lane to select it, then choose **Remove selected** to clear it from the project. Removing a WAV lane retains the extracted file for Undo and excludes its audio stream until restored; removing video clears it from the project but keeps the original recording on disk.
- Laughter bookmarks include at least four seconds of audio lead-in. Selecting any scanned bookmark highlights its clip range on the timeline; drag the glowing edges to add or trim context, then choose **Clip selected bookmark** to export it. Prompt categories and their bookmarks remain in the main editor window.
- Set the lead-in bookmark (3 seconds by default) before a detected event and adjust the minimum model match score.
- Record the desktop system-audio mix and microphone as separate streams.
- Split the video into timeline segments, trim segment edges, remove unwanted segments, play through the kept edit, and export the assembled timeline with audible tracks mixed into one stereo stream.
- Video cuts appear on separate, automatically added clip layers. Select a scanned bookmark and choose **Cue to video layer** to turn that review range into its own editable layer while keeping chronological export order.
- Store cue feedback and approved preference notes on this Windows account. Keep/Skip choices adjust future ranking per sound type.
- Click **Glitchy** from the top bar for local LLM chat, a one-click scan review, and suggestions informed by the current candidate labels and your saved Keep/Skip preferences. It never applies changes automatically.
- **Copilot reviews footage with detectors** adds a second pass using sampled frames, screen text, and separate audible tracks. Choose the review depth in advanced settings. It adds evidence and reviewable suggestions without replacing your Keep/Skip choices.
- When **Fortnite** is entered as the game and the local \`D:\Videos\NVidiaFortnite\` folder exists, elimination/down/win scans use up to six frames from each labeled clip as cached visual examples to gently rank matching moments. The clips stay local; their embeddings are cached under \`ReelGoodData\`.

## Limits

The combined scan runs both detectors from one prompt. Comma-separated visual cues are compared separately, so prompts such as “high excitement, laughter, eliminations” can match different visual concepts. The audio model matches known sound classes, not arbitrary spoken meaning. The visual model samples two frames per second and returns spaced review ranges using the Clip length setting (2 seconds to 5 minutes). It can still miss movement shorter than the half-second sampling interval, subtle expressions, and context-dependent jokes. If no frame passes the visual threshold, it offers up to five spaced, lower-confidence matches for review. Visual scores rank similarities; they do not establish that a moment is funny. Bookmarks include the chosen lead-in, and Keep/Skip stays under user control. Fortnite examples are few-shot reference matches, not a fine-tuned or fully trained elimination detector: the current folder has positive clips but lacks labeled ordinary gameplay negatives and exact event timestamps.

The first audio scan downloads an isolated Python/TensorFlow runtime and YAMNet model to `ReelGoodData\LaughDetection` beside the EXE after confirmation. The first video scan downloads a 153 MB quantized CLIP model and installs ONNX Runtime with CUDA/cuDNN support on the same drive. The visual scan selects NVIDIA CUDA when available and falls back to CPU if the GPU runtime cannot start. Video and WAV files are processed locally.

Desktop loopback records the Windows system mix, so it cannot reliably isolate Xbox party chat from game audio when Windows presents them as one mixed device. Recording availability and quality depend on the selected Windows input devices and permissions.

Approved prompt text and per-class Keep/Skip counts are saved locally. Feedback shifts event scores by a small bounded amount; it does not retrain YAMNet weights. Recommendations remain reviewable and require user action.

## GlitchStich Copilot

Every launch starts a new, empty project: no media, timeline, bookmarks, or previous scan prompt is reopened automatically. Use **Open project** to restore an existing `.reelgood` save. Media-library paths now belong to the saved project, including imported audio and PNG assets. Older saves restore their known source and attached external audio; the old global media index is no longer loaded. Saved projects, source files, and learned Copilot preferences remain on disk.

Copilot uses Ollama on this PC; media is not sent to a hosted service. The installed Gemma 4 E2B model now handles editing advice as well as reviewing sampled frames and audible audio tracks. You can still select another installed chat model. **Review footage + scan** checks detector hits plus spaced additional windows that may contain missed moments. **Inspect current bookmark / playhead with my message** reviews the current moment before answering; turn it off for general chat. Suggestions include editable prompt and timestamp cards with an **Apply reviewed change** button. Explicit relative lead-in values are calculated in app code from the event timestamp. An empty model reply is an error, not a completed review. No source footage is overwritten.

The optional Copilot second pass is enabled by default. Advanced scan settings offer focused (6), balanced (12), or thorough (24) sampled windows. Each window covers up to 12 seconds, using four spaced frames, higher-resolution Windows screen-text recognition, and up to two separate audible WAV tracks. Coverage is explicitly reported; this does not watch every second of a long recording. Supported scout findings become unscored review candidates, and existing candidates receive reasons and observations. Verified elimination-detector evidence is retained when the model disagrees. User Keep/Skip decisions remain unchanged until approved. The model can still miss events or misinterpret audio; these are review suggestions, not verified labels.

Only one model request runs at a time, media extraction is sequential, and the media model unloads before the chat model loads. On this device a cold model load can take over a minute; warm windows are faster. Cancel remains available. Models installed on the developer's PC are not included in this repository. If the media model is absent, ReelGood asks before downloading it into Ollama's configured folder. The larger Scanned Bookmarks panel is resizable, with **Expand clips** to collapse settings and give the list more room.

## Laughter model recommendation

Keep YAMNet as the fast sound-event candidate finder, but train a small per-user laughter-versus-not-laughter classifier on YAMNet audio embeddings and short snippets explicitly labeled Keep or Skip. Include hard negatives such as ordinary friend speech, shouts, game sounds, breathing, and mic noise. Split evaluation by recording/session so adjacent frames from one laugh do not appear in both training and validation. Do not fine-tune the full audio model from scratch; collect enough balanced reviewed clips first, then test recall and false alarms on recordings not used for training. This keeps user preference changes reviewable and gives a measurable path to improving the current sound-class approach.

## Audio event model

The detector uses Google's pretrained YAMNet audio event classifier, which predicts 521 sound classes. See Resources\LaughDetection\THIRD-PARTY-NOTICES.txt and the upstream TensorFlow Model Garden documentation for model details and attribution.

## Visual model

The visual scanner uses the quantized ONNX export of CLIP ViT-B/32 from the ONNX Community Hugging Face repository. It compares each sampled frame to several prompt phrasings, groups adjacent matches, and offers a small fallback set when no frame crosses the threshold. It samples two frames per second and uses CPU inference for the current quantized model, which benchmarked faster than CUDA on this device; the model and GPU runtime are downloaded to ReelGoodData on first setup and are not bundled into the app executable. The scan reports whether it is currently using NVIDIA GPU or CPU mode.

## Video model research

The Microsoft VideoX repository includes X-CLIP, which adds cross-frame attention and multi-frame integration to CLIP for video recognition. It is a useful temporal-model reference, but GitHub currently marks the repository archived. OpenGVLab's InternVideo repository includes broader video-language retrieval, temporal localization, and question-answering work under Apache-2.0; its newer models are a larger deployment step. ReelGood keeps its compact local CLIP model for now and can revisit a temporal model after testing on representative recordings.

TEMPURA is the strongest candidate reviewed so far for locating prompt-matched moments with timestamps. Its 2B InternVL3 checkpoint is BF16 and accepts timestamped frames at 0.5 fps; its public setup was tested on H100 hardware, so performance and memory use on consumer GPUs still need measurement. It can be evaluated as a second-stage temporal detector after CUDA acceleration is in place, rather than replacing the stable CLIP path unconditionally. User Keep/Skip feedback remains a separate local personalization signal; it does not fine-tune TEMPURA weights. MomentSearch is a useful product-pattern reference: it fuses visual keyframe search with transcript search and returns clickable moments, but relies on a Docker/Qdrant service stack. Gameplay Vision LLM combines video, audio, and OCR for game footage, but its Qwen3-VL-8B core and high-end GPU requirements are too heavy for ReelGood's current local scanner.

## Third-party media tools

Projects save as `.reelgood` JSON files referencing local media. Saves replace the project atomically and retain the previous version as `.reelgood.bak`. Save/Open preserves clip layers, empty timelines, bookmarks, decisions, and per-track volume/mute. Changing videos asks about unsaved edits; switching back from an image to the active video keeps the edit. Media replacement and editing are blocked while a scan, load, or export runs.

Both clip and timeline exports honor the active audio tracks and their gain/mute settings. Recording uses NAudio 2.2.1 (MIT) to capture the default Windows output and microphone independently, then FFmpeg combines the WAV tracks with desktop video. Intermediate recording files remain in `ReelGoodData/Recordings` if finalization fails. Live recording synchronization and playback smoothness still require footage testing.

Regression checks: build the app in Release for win-x64, then run `dotnet run --project checks/ReelGoodChecks.csproj`. These checks generate synthetic media to verify audio selection, gain, muting, edited duration, recording muxing, atomic project backups, and project data compatibility. Device initialization checks do not record microphone or desktop audio.

Use the user's NVIDIA/Medal recordings under `D:\Videos` for real-footage checks. Refresh the inventory with `python checks/inventory_footage.py` whenever recordings are added, then run `dotnet run --project checks/ReelGoodChecks.csproj -- --footage checks/results/inventory.json`. The representative footage checks exercise short sections of five baseline recordings. They write derived test media and reports only under `checks/results`, which is excluded from Git. Original footage stays untouched. Metadata probes cover the inventory; short-section tests do not certify entire recordings or detection accuracy. Windows GUI playback still needs a separate check.

Medal recordings with explicitly named All Audio, All PC Audio, and Microphone tracks retain all WAVs, but default to the separate PC/microphone tracks. The redundant combined mix is muted, excluded from scanning, and omitted from exports while muted; it stays available to unmute manually. Native title/name metadata is used for track labels. Project files retain a user's deliberate mix choice; older project files receive the duplicate-mix default.

Audio prompts now match explicit aliases or whole class names. Excitement requests use cheering/shouts/screams as review cues, and game outcomes are explicitly routed to visual review. Unsupported requests are reported rather than silently mapping unrelated words to sounds. Fortnite reference clips distinguish Eliminated (player death) from Elimination, sample their final six seconds, and invalidate the previous reference cache once. Run `python checks/scanner_tests.py` for the prompt/reference regression checks.

The bundled FFmpeg 9.0.2 Essentials build is from gyan.dev and is licensed under GPLv3. See `tools\FFmpeg-GPLv3-LICENSE.txt` and `tools\FFmpeg-build-notes.txt` for license and build information. The installer script verifies archive SHA-256 `60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba` before extraction.

Elimination scans compare event descriptions with ordinary gameplay and require a changing central English outcome indicator using built-in Windows OCR. Static counters, corner feeds, and player-death messages are filtered. Unsupported or unreadable indicators can cause missed events; these remain review suggestions rather than proof of a kill. Generic visual similarity no longer creates fallback elimination bookmarks. CUDA remains available for frame extraction and preview conversion; --device cuda is an optional inference profiling override. The benchmark used six identical frames: CPU inference 0.81 seconds versus CUDA 52.38 seconds; it is not a full-video speed guarantee.


### Measured progress and Copilot corrections

The top progress rail reports actual processed seconds, video frames, and reviewed candidates for each named stage. Percentages restart when the stage changes; they are not a prediction of total scan time. Stages with no measurable total show their activity and elapsed time. Lists, scrollbars, and inputs keep the dark studio theme while the editor is locked for processing.

Copilot receives the current scan prompt, bookmark IDs, exact start/end/event times, scores, sampled media observations, and optional approved local feedback. It can propose editable prompt or bookmark corrections. Chat-only timing patches preserve Keep/Skip; separate footage-review cards can suggest a decision, and you can edit it before applying. Click **Apply reviewed change** to approve each proposal. Changes are rejected if the project has changed since the suggestion, if processing is active, or if values are outside the source. Applying a prompt does not automatically rescan; applying a bookmark does not export or overwrite footage.

Approved corrections are recorded in `ReelGoodData/copilot-corrections.jsonl`. Keep/Skip updates local event preferences; recent approved corrections also guide Copilot when its preference checkbox is enabled. This is feedback and personalization, not retraining pretrained model weights. Copilot needs a responsive model in the existing local Ollama service. Replies have a three-minute limit and closing Copilot cancels the request.

The studio now shares cut-corner controls and panel silhouettes with its dialogs. The short intro uses a 3D projector, spinning reels, a layered neon skyline, a perspective floor, and a moving projection reveal. It remains separate from editor startup and closes at the end of its existing bounded startup sequence. Playback configuration is unchanged by these interface changes.

The intro includes an original 6.2-second stereo sound logo: a mechanical spin-up, projection sweep, and soft synth reveal. It is embedded in the app, plays once without blocking the interface, and stops when the intro closes. Audio-device failure leaves startup running normally. The reproducible synthesis source is tools/Generate-IntroSound.py; no sampled recordings or additional downloads are used.

### Generate a draft edit

**Game / mode** fields are visible beside **Editing intent**, for example Fortnite / Zero Build or Gears of War / Campaign. Both are saved in the project and included in visual-detector templates and Copilot media/chat context. IRL ignores both fields. New preference scores and approved corrections are scoped by game, mode, and editing intent; older unscoped records remain on disk but are not injected into a specific game profile. The game/mode describes the footage; it is not evidence that an event occurred and does not switch to a separately trained model.

Approved scan guidance for each profile is stored in `ReelGoodData/preferences-by-context.json`, so switching to another game does not overwrite the previous game's guidance. New reviewed cues retain their feedback scope in project saves. Verified detector outcomes also protect their ranges against quiet cleanup unless the user explicitly skips them.

Choose **Story / preserve context** (the safe default), **Competitive highlights**, or **Comedy / player reactions**. With an empty prompt, each supplies scan objectives; a custom prompt adds targets to those mode objectives. Story drafts retain unreviewed scenes and only propose removing reviewed quiet downtime. Highlight/comedy drafts select bookmarked moments plus identified scene context. Skipping a bookmark in Story mode does not delete that story scene. An explicit Keep protects its range against quiet cleanup.

**Second mode (optional)** supports Highlights + Comedy, Story + Comedy, or Story + Highlights. Leave it at **None** for the original single-mode behavior. Both modes contribute objectives to one scan; a moment can match either. Story in either slot preserves unreviewed scene continuity. Shared moments are merged into the same timeline range. Choosing the same mode twice resets the second slot to None. Both choices are saved with the project and available in draft review; older saves open with no second mode. Pair-specific feedback is shared when the two slots are swapped and remains separate from single-mode feedback. A custom scan prompt combines with the selected mode objectives.

Copilot can classify reviewed windows as cutscene, character dialogue, player speech, action, downtime, or unknown. Character-dialogue/cutscene windows protect surrounding context. A proposed downtime cut requires an explicit inactive-footage verdict AND at least two interior seconds below -40 dBFS on every reviewed audible independent track, with a second retained on each side. Missing/unreviewed audio, uncertain classifications, and silence by itself do not authorize cuts. If more independent audible tracks exist than the model reviewed, quiet cleanup is disabled for that window. Changing the declared game/mode clears pending quiet cuts until re-review.

**Remove reviewed quiet downtime** can be disabled in draft review. **Scene protection / proposed quiet cuts** lists the evidence and time ranges; applying the draft remains a separate approval. This is conservative dead-time editing, not noise reduction or a noise gate. Copilot's sampling is bounded by the selected review depth, so a long recording's unreviewed downtime is not automatically removed. In highlight modes, sampled context does not establish the complete boundaries of every long cutscene; Story mode retains unreviewed narrative footage. Automatic dialogue/cutscene classification can still be wrong.

After scanning, click **Generate draft stitch** in the top toolbar. The review window groups overlapping audio and video bookmarks into chronological clips, preserves each bookmark's existing lead-in, excludes Skip decisions, and excludes unapproved weak fallback matches. Unreviewed matches are included by default; uncheck that option for a Keep-only draft. Join gap and end padding are adjustable from 0 to 10 seconds. Click Update draft after changing settings, then preview and uncheck individual clips before applying.

The short [Twitter Chirp notification whistle](https://quicksounds.com/sound/22704/twitter-chirp) plays when a nonempty draft finishes generating and is ready for review (including Update draft). Invalid settings and empty results do not play a success notification. The WAV is embedded and plays asynchronously; a missing audio device never blocks draft generation, and closing the editor cancels playback. Source provenance is recorded in `Resources/Audio/DraftReady-source.txt`.

**Apply draft to timeline** replaces the video ranges with the reviewed selection. It does not export or overwrite source recordings, change bookmarks, or change audio mute/gain settings. The timeline retains source timestamps; playback skips unused gaps, and Export timeline concatenates the selected ranges with their synchronized audio streams. End padding is clamped to the source duration.

**Restore pre-draft** restores the video ranges and layers from before the most recent applied draft. This single restore snapshot is saved with the .reelgood project and remains available after reopening it. It is not a general Undo history, and it does not revert later audio changes. A fresh video source clears the restore snapshot. Excluding a clip in draft review does not change its bookmark's Keep/Skip training feedback.

If a skipped bookmark overlaps an included moment, review warns about the shared footage. A skipped bookmark does not erase footage belonging to another included bookmark. Draft generation assembles existing detections; it does not improve detector accuracy or require a new language-model request.


### Clear audio and resource-aware exports

Extraction compares complete WAV audio payloads and format rather than filenames or loudness peaks. Exact duplicate tracks are muted automatically; recognized redundant Medal combined mixes remain excluded by default. The timeline marks duplicates and provides **Remove duplicate audio** to remove muted redundant tracks from the project while retaining source/WAV files on disk. Selecting the combined mix mutes its separate source components; selecting a component mutes the combined mix. Identical audio copies also play one at a time. Distinct mic/game tracks and external music remain independently controllable. This does not repair echo already recorded inside a single waveform or assume similar audio is identical.

Video export combines audible source/WAV tracks into one 48 kHz stereo AAC stream at 320 kb/s, respecting mute/gain and applying a peak limiter. Extracted WAVs are used when present. High retains source resolution/frame rate with higher quality compression; Balanced caps the frame at 1920x1080 and 60 fps without upscaling; Maximum uses less compression and larger files. Output is compatible 8-bit H.264 MP4. None restores detail absent from the source.

Exports probe NVIDIA, Intel, and AMD encoders locally and fall back to software. NVIDIA export can also use hardware video decoding. Automatic CPU worker budgets are bounded from detected cores/memory; Low impact reduces workers further. On supported Windows configurations, FFmpeg CPU affinity and below-normal priority reserve CPU capacity. Each clip processes serially into a temporary Matroska part with lossless PCM audio; finalization joins video without re-encoding it and encodes the mixed audio once. Temporary files live beside app data on its drive and are cleaned on success/cancellation. Existing destination files are replaced only after successful finalization. Hardware failure restarts with software; cancellation stops the FFmpeg child. ETA follows measured throughput and can change. Resource use is bounded, not a guarantee of constant utilization or export speed on every PC.

**Start scan** sits beneath the prompt. It scans the selected mode(s), plus any optional prompt, through one audio/video scan. Enter in the prompt uses the same action. After a successful Save decision, No, or an already-saved project, closing immediately hides the editor and its taskbar entry. Cancel leaves it open. Timers stop and playback pauses; existing tasks finish in the background before the WPF video surface is detached on its dispatcher and the native player is disposed on a worker thread. Recording finalization preserves its separate audio/video files. Repeated close requests are ignored during cleanup; the process can remain briefly while work finishes. Late task dialogs do not reopen the hidden editor. Timings are written to ReelGoodData/shutdown.log.

### Editing tools and timed media

Video autoplay is disabled throughout the editor. Importing/opening a source, selecting a scanned bookmark, switching original/smooth preview, and opening a rendered preview all remain paused. Use Play (or the deliberate Play command) to start; draft review has an explicit **Play clip** button. A private requested-play state coordinates the transport instead of treating the button label or an engine-side autoplay event as permission to start WAV playback.

Preview WAV playback now follows the video clock. Accurate seeks pause the decoder and WAV players until the completed paused-seek callback, then resume together; pausing while a seek is pending keeps playback paused. Newly opened WAVs inherit the current speed and position. The existing 100 ms UI timer checks audio drift and corrects audio alone when needed, without repeatedly seeking the video. Native speed changes use the typed player API. This affects playback timing, not detector or Copilot scan behavior.

**Stitch tools** provides trim, split, delete/close gap, duplicate, reorder, undo/redo, speed (0.25–4×), edge crop, rotation, brightness/contrast, clip volume and fades. The ordered clip list is the export sequence. The main timeline remains a source-time view; duplicate source ranges can be distinguished with a media placement's occurrence number (0 is the first). Edits are non-destructive and clip effects, assets and placements are saved in the project. Removing an extracted audio lane retains its WAV on disk so Undo can restore it.

Import images, videos and audio in **Media / captions**, describe the asset, then add timed image/video/audio overlays or text captions. Position, size, volume, asset offset and source anchor are editable. Additional video files are timed overlays over the single source sequence, not independent base-video projects. Use **New project** to start a different source sequence. The main Import button adds editing assets when a source is already loaded.

**Render preview · first 30s** produces an exact effects/media preview of up to 30 edited seconds. The live source player previews clip order, speed and gain; crop, rotation, fades, captions and timed overlays are evaluated by rendered preview and final export. Final export normalizes the canvas across crop/rotation changes and mixes audible source tracks and media into one stereo stream, with the existing bounded CPU/hardware-encoding policy.

**AI placement** reviews sampled footage and explicitly enabled assets through the existing local Gemma model. It receives actual asset images/first video frames, short asset audio, source observations, candidate times and scoped user feedback. Up to 12 assets are considered per pass. Placements must fit reviewed kept source ranges and avoid protected dialogue/cutscenes. If none fit, the reason is shown instead of an empty Apply action. Select suggestions, add them to the review, then **Apply edits to project** to approve them. Model placement quality still requires user review; this uses saved preferences and evidence, not live weight training.

Current validation limit: the live Fortnite/test-caption control returned no suitable AI placement, so reliable automatic meme placement is not yet established. Manual timed placements and actual rendering passed. The app reports model refusal rather than claiming edits were generated.

### Optional YouTube setup

**YouTube** contains the Google Desktop OAuth client JSON import, browser authorization, channel connection/revocation and upload settings. The official Google .NET client library handles authorization and resumable uploads. The user must configure their own Google project/consent screen; no embedded account or shared client credentials are supplied. Client details and refresh tokens are encrypted with Windows DPAPI for the current user under ReelGoodData/YouTube. Automatic upload is OFF until a connected user explicitly enables and saves it. Manual upload has a separate confirmation. Previews and source recordings are not automatically uploaded. An upload failure leaves the completed local export intact. Google may restrict unaudited applications to private uploads.

YouTube tests use a fake client and mocked HTTP only: encrypted storage, resumable chunking, privacy, subscriber-notification settings and disconnect are checked without connecting a real account or publishing a video. Live streaming/OBS-style capture and online model-weight training remain future work.






