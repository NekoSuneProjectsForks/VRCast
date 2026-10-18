# VRCast — consolidated TODO checklist

> Source: the six feature/status messages on [PR #1](https://github.com/NekoSuneProjectsForks/VRCast/pull/1). This preserves their checklist wording and current status. `[x]` means implemented in source, **not** validated in Unity; `[ ]` means incomplete or unverified. Hardware tests and full builds remain outstanding. The two OSC messages overlap intentionally to retain all original requests.

## Original feature backlog

Source: [Original PR comment](https://github.com/NekoSuneProjectsForks/VRCast/pull/1#issuecomment-6232163064)

**Status: ❌ IN PROGRESS — backlog reviewed; the configurable FPS cap is implemented in source, but still needs Unity/Windows validation; the remaining broader requests are outstanding.**

### Avatar controls and expressions

- [ ] **Adjustable eye openness** — provide a dedicated control for eye openness in addition to editing blink shapes.
- [ ] **Switch avatars or outfits** — make avatar switching easier; investigate switching clothing/outfits on the same avatar separately.
- [ ] **PNG/GIF attachments** — investigate displaying PNG or GIF images as avatar-attached/in-scene objects, beyond avatar-list thumbnails.
- [ ] **Vowel mouth shapes and eyebrow tracking** — retain A/I/U/E/O lip shapes and verify whether dedicated eyebrow tracking is supported.
- [ ] **Custom expression calibration** — investigate registering custom expressions through a calibration workflow.
- [ ] **Reset gaze, head direction and expressions** — provide a reliable way to return these controls to a neutral/default state.
- [ ] **Avatar animations** — verify automatic blinking and animation controls; investigate MMD dance/motion playback as a separate feature.
- [ ] **Transparent avatar screenshots** — investigate exporting screenshots with a transparent background, beyond avatar thumbnail capture.
### Audio and lip-sync

- [ ] **Reduce jittery microphone lip-sync** — improve smoothing and/or expose mouth movement attack/release controls to reduce rapid, noisy changes.
- [ ] **Microphone volume visualisation** — verify the input-level meter and microphone status feedback are clear and useful.
- [ ] **Siusi​​u lip-sync compatibility** — investigate compatibility with SiuSiu lip-sync specifically.
- [x] **Basic audio-reactive shader data** — local opt-in FFT analysis for RMS, four bands and basic beat events; 128x64 `_AudioTexture` with four AudioLink-compatible band rows, independent analysis rate and sensitivity. Live shader validation and full AudioLink passes remain open. See [audio-reactive shaders](docs/audio-reactive-shaders.md).
### Tracking and automation

- [ ] **iPhone tracking plus webcam hand tracking** — verify the existing hybrid tracking modes and document setup/limitations.
- [ ] **HTTP avatar-switch commands** — investigate a documented HTTP command/API for switching avatars, with appropriate validation and security.
### Application maintenance

- [ ] **In-app updates** — investigate a complete update/download/install flow; an update-check setting alone does not necessarily provide one-click updating.
### Additional roadmap items for this fork

- [x] **Configurable FPS limit** — implemented in the PR branch with Auto/explicit limits; Unity/Windows validation is still outstanding.
- [ ] **MMD/VMD motion support** — start with VMD motion playback on already-loaded avatars; treat PMX/PMD model importing as a separate, larger milestone.
- [ ] **Performance and stability** — profile rendering, tracking and audio processing; keep expensive work off the main thread where safe and avoid unnecessary per-frame allocations.
### Verification notes


---

## Shared sessions and hosting

Source: [Original PR comment](https://github.com/NekoSuneProjectsForks/VRCast/pull/1#issuecomment-6232180210)

**Status: ✅ INITIAL IMPLEMENTATION — headless room server, Control tab host/join, roster, pose placeholders, reconnect and room revocation are implemented and covered by automated tests. Real avatar/tracking replication and full desktop GUI verification remain open.**

## Additional roadmap: shared sessions, portable server, and performance

### Shared session server

- [x] Add a lightweight, headless VRCast Session Server with named rooms so two or more clients can host/join the same session.
- [x] Make joining simple: room code or invite link, visible connection status, and host/join controls.
- [ ] Synchronize only necessary state: participant identity, supported avatar/actor selection, transforms/facing, expression parameters, animation/motion state, and scene events.
- [x] Use a versioned protocol, event-driven updates, periodic snapshots when needed, interpolation for remote movement, and clear rules for state ownership/conflicts.
- [x] Handle heartbeat/timeouts, disconnects, reconnects, room membership changes, and client/server version mismatch.
- [x] Relay state/events rather than rendered video or duplicated rendering work. Keep media streaming separate and optional.
- [ ] Start with LAN/direct-connect sessions, then add internet hosting. HTTPS reverse-proxy support exists; optional relay and WAN validation remain.
- [x] Secure sessions with pairing codes or revocable tokens, input validation, rate limits, and TLS guidance. Do not expose unauthenticated control endpoints by default.
### Portable hosting

- [x] Docker Compose deployment with a small image, configurable ports/environment, health check, restart policy, persistent config, graceful shutdown, and reverse-proxy/TLS documentation.
- [ ] Windows x64 standalone release, with an optional Windows Service installer.
- [ ] Linux x64 standalone binary and optional systemd unit; assess ARM64 support separately.
- [x] Keep the server headless and independent of Unity or graphics hardware so it can run on a VPS, home server, or spare PC.
- [x] Include example configuration, `.env.example`, structured logs, and a status/health endpoint. Avoid a database for the initial small-session use case unless persistence actually requires one.
- [ ] Publish versioned binaries/container images and document client/server compatibility.
### Optimisation ideas

- [ ] Profile CPU/GPU usage, frame-time spikes, memory, garbage collection, and network traffic before making assumptions; record repeatable before/after benchmarks.
- [ ] Avoid per-frame allocations and repeated JSON/string work in hot paths; reuse buffers where profiling shows a benefit.
- [ ] Send only changed parameters (dirty-state/event-driven updates) rather than repeatedly processing unchanged state.
- [ ] Bound network queues and adapt update rates to activity; interpolate remote state and avoid unbounded backlogs when a client is slow.
- [ ] Separate render FPS, network tick rate, and audio-analysis rate so each can be tuned independently.
- [ ] Add optional quality controls for expensive effects and diagnostics for CPU/GPU frame time, GC, latency, packet loss, and bandwidth.
- [ ] Test long-running sessions, multiple clients, reconnects, simulated latency/packet loss, and memory growth.
### Other useful features

- [ ] Scene/session presets for participant layout, expression defaults, and relevant settings.
- [ ] Timestamped recording/replay of scene events and motion/parameter changes for debugging without recording full video by default.
- [ ] A documented plugin/event API so integrations do not need to modify core code.
- [ ] Optional OSC/VMC bridges for compatible tracking and avatar-control tools, with explicit mapping and loop prevention.
- [ ] One-click diagnostics export with logs/settings/performance data and secrets or tokens redacted.
- [x] Headless protocol/server tests in CI that do not require Unity or a GPU.
- [ ] Better keyboard shortcuts, searchable settings, and actionable connection error messages.
### Suggested order

1. Benchmark the current client and add regression tests.
2. Define and test a small versioned session protocol.
3. Implement two-client LAN rooms and synchronization.
4. Package the headless server for Docker Compose, Windows, and Linux.
5. Add security, reconnect handling, and network-condition tests.
6. Expand to internet relay, plugins, and optional integrations based on real-world use.

---

## VNyan-style integration backlog

Source: [Original PR comment](https://github.com/NekoSuneProjectsForks/VRCast/pull/1#issuecomment-6232186790)

**Status: ❌ IN PROGRESS — feature-parity backlog only; most integrations below are not implemented by the current FPS-cap change.**

### Avatar formats and motion

- [x] **VRM compatibility audit:** code-path matrix for VRM 0.x, VRM 1.0, and unsupported VSFAvatar; expressions, materials, colliders, spring hair and limitations documented in [avatar compatibility](docs/avatar-compatibility.md). Live avatar testing remains open.
- [ ] **MMD motion:** play VMD motion on compatible loaded avatars and optionally drive a camera; keep motion import/retargeting separate from PMX/PMD model import.
- [ ] **Custom content SDK:** investigate an SDK for user-created props, items, worlds/scenes, and extensions, with versioning and clear sandbox boundaries.
### Tracking and props

- [ ] **SteamVR tracking:** advanced configurable tracker assignment, calibration, offsets, smoothing, axis mapping, and per-device enable/disable controls.
- [ ] **Tracked props:** investigate VMC and VR/SteamVR-tracked props, with configurable attachment points, calibration, and loss-of-tracking handling.
- [ ] **VMC receivers:** support multiple independently configured VMC-protocol inputs (up to four as a target), with per-receiver status, port/configuration, source selection, and conflict prevention.
- [ ] **Face tracking:** investigate ARKit blendshape input from webcam-based solutions and integrations such as iFacialMocap, VTube Studio on iPhone, and MeowFace, using documented protocols/APIs and user-configurable mappings.
- [ ] **Hand tracking:** optional Leap Motion support with calibration, left/right-hand mapping, smoothing, and graceful fallback when the device is unavailable.
### Audio and shaders

- [ ] **Audio-based lip-sync:** configurable smoothing, sensitivity, attack/release, silence threshold, and vowel/viseme mapping to reduce jitter.
- [ ] **Audio analysis actions/plugins:** basic local RMS, four bands, beat detection and controls are implemented; exposing them to action graphs/plugins and WASAPI music loopback are still open.
- [ ] **Full AudioLink shaders:** basic local four-band texture bridge is implemented; verify actual imported shaders and add remaining DFT/waveform/ColorChord/etc. passes. No direct bridge to VRChat worlds.
### Collaboration and external control

- [x] **Multi-person sessions:** two or more users can join one room, exchange the supported actor pose and host scene events, and see connection/reconnection status. Remote avatars currently render as placeholders; full-body/face/animation replication remains open.
- [ ] **VNyanNet-style collaboration:** investigate an interoperability layer for cross-client collaboration, while avoiding assumptions about a proprietary protocol. Prefer a documented, versioned protocol with authentication and explicit permissions.
- [ ] **WebSocket commands:** provide a documented, authenticated command API for Stream Deck, Streamer.bot, and other tools. Include command allowlists, validation, rate limits, and safe defaults.
- [ ] **Twitch integration:** investigate events such as follows, subscriptions, channel points, raids, and hype trains where the official API exposes them; use OAuth scopes minimally and handle expired/revoked tokens.
- [ ] **Chat emotes:** investigate Twitch emotes and optional 7TV, BetterTTV (BTTV), and FrankerFaceZ (FFZ) support, respecting their APIs, caching rules, and licensing/attribution requirements.
- [ ] **Heart-rate integration:** optional Pulsoid integration, with clear permission/setup steps, connection status, and a way to disable or hide biometric data. Avoid logging sensitive readings unnecessarily.
### Actions, node graphs, physics, and visuals

- [ ] **Plugin/modding API:** define a stable extension API, version compatibility rules, permission boundaries, and safe failure handling.
- [ ] **Visual node graph:** create actions and event flows with triggers, conditions, variables, delays, and outputs; validate graphs before running them and prevent runaway loops.
- [ ] **Shareable graphs:** export/import node graphs as versioned files with dependency checks, validation, and a preview of actions before enabling them.
- [ ] **Built-in actions:** configurable throw/drop/launch (including “yeet”-style) actions, with adjustable force, cooldowns, collision handling, and reset behaviour.
- [ ] **Liquid physics:** evaluate high-quality liquid simulation as an optional effect, with quality levels and performance budgets; keep it disabled or simplified on weaker hardware.
- [ ] **Pendulum/spring physics:** configurable secondary motion for suitable avatar parts or props, including limits, damping, collision options, and reset/disable controls.
- [ ] **Props:** support 3D models, static PNGs, animated GIFs, and other appropriate image formats as scene/avatar-attached objects, with transform controls, visibility toggles, and resource limits.
- [ ] **Capture outputs:** investigate Spout2 and NDI output/capture options, including platform-specific support, resolution/FPS settings, and clear licensing/dependency documentation.
- [ ] **Post-processing:** optional effects with per-effect quality controls, sensible defaults, and profiling to avoid unnecessary GPU cost.
### Optional integrations and privacy

- [ ] **Optional adult/NSFW integrations:** if considered appropriate for the project, make these opt-in, disabled by default, clearly age-gated where required, and separated from the core app. Any Lovense/device-control integration should require explicit pairing and consent, have a prominent stop/disconnect control, and never activate unexpectedly. Support hiding/removing related props and UI.
- [ ] **Security review:** all external integrations should use minimal permissions, protect credentials, provide disconnect/revoke controls, validate incoming messages, and redact secrets from logs.
### Performance and delivery requirements

- [ ] Keep integrations modular so users only load the plugins/devices/services they need.
- [ ] Avoid doing network, file, or expensive analysis work on the rendering/main thread; queue work safely and bound queues.
- [ ] Add per-integration enable/disable controls, health/status indicators, diagnostics, and clear fallback behaviour when dependencies are missing.
- [ ] Benchmark frame time, CPU/GPU use, memory/GC, and network traffic with features both enabled and disabled; do not promise “more optimized than VNyan” without repeatable comparisons on equivalent hardware and scenes.
- [ ] Add automated tests for protocol parsing, node-graph validation, reconnects, and settings; add integration test plans for hardware-dependent features.
### Suggested priority

1. Stable plugin/event API and performance baselines.
2. Audio analysis/lip-sync improvements and authenticated WebSocket commands.
3. VMC/face/hand tracking adapters and multi-person session protocol.
4. VRM compatibility, props, node graphs, and shareable actions.
5. Twitch/chat integrations, capture outputs, and advanced physics/post-processing.
6. Optional sensitive integrations only after consent, privacy, and security review.

---

## Full-body motion capture

Source: [Original PR comment](https://github.com/NekoSuneProjectsForks/VRCast/pull/1#issuecomment-6232198208)

**Status: ❌ IN PROGRESS — existing MediaPipe webcam tracking and external VMC full-body input are documented; a new optimised motion-capture pipeline, retargeting upgrades, recording/export and benchmarking are not yet implemented/validated by this PR.**

## Additional roadmap: optimised full-body motion capture and tracking

### Tracking input options

- [ ] **Single-webcam AI mocap:**** estimate body pose from a webcam, with configurable full-body, upper-body, arms/hands, or face-only tracking. Investigate MediaPipe or another suitable model, including whether inference can run in a worker or separate process.
- [ ] **External full-body tracking:**** receive supported VMC/OSC tracking data from applications and hardware trackers, including hips, spine, head, arms, legs, feet, fingers, and root position where provided.
- [ ] **Hybrid tracking:**** combine external face tracking with webcam body/hand tracking, or combine other compatible sources without running duplicate trackers unnecessarily.
- [ ] **Calibration and recovery:**** floor height, avatar proportions, T-pose/rest-pose calibration, tracking-space alignment, smoothing, confidence thresholds, and graceful handling of occlusion or lost trackers.
### Avatar retargeting and motion tools

- [ ] Retarget motion reliably to supported humanoid avatars with different bone proportions and rest poses.
- [ ] Provide per-body-part enable/disable controls and clear indication of which input currently drives each part.
- [ ] Reduce jitter and foot sliding with configurable filtering and optional foot/hip constraints, without adding excessive latency.
- [ ] Record and replay captured motion; investigate export/import formats such as VMD, BVH, and VRMA where licensing and format compatibility permit.
- [ ] Keep live tracking, prerecorded motion, and externally received motion as separate selectable sources.
### Performance goals

- [ ] Decouple camera capture, pose inference, avatar updates, and rendering so a slow inference frame does not stall the whole application.
- [ ] Use asynchronous inference where supported, reuse buffers, avoid per-frame allocations, and update only changed tracking data.
- [ ] Offer selectable inference resolution/model quality and tracking frequency, with a low-CPU mode and optional GPU acceleration where available.
- [ ] Measure end-to-end latency, tracking FPS, render FPS, CPU/GPU use, and memory on a documented range of hardware. Treat targets as benchmarks to validate rather than guaranteed performance claims.
### Suggested implementation order

1. Define a common tracking-pose interface and coordinate-space conventions.
2. Improve and benchmark the existing external VMC/OSC path, including full-body retargeting and calibration.
3. Add a webcam full-body proof of concept with selectable body/hand/face tracking and measurable performance.
4. Add hybrid sources, recording/replay, and optional motion export after the core path is stable.

---

## VRCFT and face OSC

Source: [Original PR comment](https://github.com/NekoSuneProjectsForks/VRCast/pull/1#issuecomment-6232207019)

**Status: [✓] optional live face values and configurable ARKit-compatible blendshape mappings can be sent to configured VRChat avatar parameters; [✗] unified provider management and VRCFT discovery are still outstanding.**

### Progress so far

- [x] Added an OSC 1.0 encoder and UDP sender for VRChat `/avatar/parameters/{name}` float/bool/int messages, with EditMode packet-encoding tests.
- [x] Added persistent endpoint settings and a Settings UI for enabling/disabling output, applying the destination, viewing sender status, and sending test messages.
- [x] Added optional live mappings for left/right eye openness and mouth openness, plus smile when the provider supplies expression scores.
- [x] Added named ARKit-compatible mappings and configurable mappings for all 51 ARKit-compatible blendshape channels, when the provider supplies the blendshape array.
- [x] Added mapping sanitisation for whitespace, invalid OSC path segments, duplicate channels, and duplicate destinations across named and custom mappings.
- [x] Added UI diagnostics for duplicate destinations and invalid OSC parameter paths/control characters before the settings are sanitised.
- [x] Runtime prevents more than one value being sent to the same avatar parameter per update; named mappings have priority over custom mappings.
- [x] Cached custom ARKit channel-index lookups per mapping, refreshing only when the configured source name changes rather than scanning all 51 channel names on every OSC update.
- [x] Added regression coverage that the runtime-only lookup cache is excluded from JSON settings serialization.
- [x] Output is disabled by default, uses configurable endpoint settings, requires fresh face-tracking data, and is rate-limited to a configurable 1–60 updates/second (30 by default).
- [x] Endpoint retry attempts are throttled, and UDP status text does not imply that VRChat has confirmed receipt.
- [x] Hardened sender validation: IPv4-only endpoints are enforced for the IPv4 UDP socket; endpoint and parameter whitespace is trimmed; empty/path-containing parameter names are rejected; non-finite float values are rejected.
- [x] Added EditMode tests for IPv6 rejection and invalid/non-finite float values. These tests have been authored but have not been run in Unity.
- [ ] Unity EditMode tests/build and live VRChat validation have not been run; authored 100 packet round-trip test cases are not evidence of execution.
- [ ] VRCFT discovery/control, incoming OSC, per-avatar profiles, independent face/head/body/hand lifecycle controls, and skeletal motion forwarding remain outstanding.
### Next tracking milestones

1. Add per-avatar mapping profiles and visible diagnostics for disabled/conflicting mappings.
2. Add provider status and safe start/stop/reconnect controls using documented provider APIs.
3. Keep VMC skeletal pose, avatar-parameter OSC, and actual tracker devices as separate capability types.
4. Validate multiple providers together, including stale input and parameter conflicts.

---

## VRChat OSC and body/head/hand tracking

Source: [Original PR comment](https://github.com/NekoSuneProjectsForks/VRCast/pull/1#issuecomment-6232216911)

**Status: [✓] optional live face values and configurable ARKit-compatible blendshape mappings can be sent to configured VRChat avatar parameters; [✗] unified provider management and VRCFT discovery are still outstanding.**

### Progress so far

- [x] Added an OSC 1.0 encoder and UDP sender for VRChat `/avatar/parameters/{name}` float/bool/int messages, with EditMode packet-encoding tests.
- [x] Added persistent endpoint settings and a Settings UI for enabling/disabling output, applying the destination, viewing sender status, and sending test messages.
- [x] Added optional live mappings for left/right eye openness and mouth openness, plus smile when the provider supplies expression scores.
- [x] Added named ARKit-compatible mappings and configurable mappings for all 51 ARKit-compatible blendshape channels, when the provider supplies the blendshape array.
- [x] Added mapping sanitisation for whitespace, invalid OSC path segments, duplicate channels, and duplicate destinations across named and custom mappings.
- [x] Added UI diagnostics for duplicate destinations and invalid OSC parameter paths/control characters before the settings are sanitised.
- [x] Runtime prevents more than one value being sent to the same avatar parameter per update; named mappings have priority over custom mappings.
- [x] Cached custom ARKit channel-index lookups per mapping, refreshing only when the configured source name changes rather than scanning all 51 channel names on every OSC update.
- [x] Added regression coverage that the runtime-only lookup cache is excluded from JSON settings serialization.
- [x] Output is disabled by default, uses configurable endpoint settings, requires fresh face-tracking data, and is rate-limited to a configurable 1–60 updates/second (30 by default).
- [x] Endpoint retry attempts are throttled, and UDP status text does not imply that VRChat has confirmed receipt.
- [x] Hardened sender validation: IPv4-only endpoints are enforced for the IPv4 UDP socket; endpoint and parameter whitespace is trimmed; empty/path-containing parameter names are rejected; non-finite float values are rejected.
- [x] Added EditMode tests for IPv6 rejection and invalid/non-finite float values. These tests have been authored but have not been run in Unity.
- [ ] Unity EditMode tests/build and live VRChat validation have not been run; authored 100 packet round-trip test cases are not evidence of execution.
- [ ] VRCFT discovery/control, incoming OSC, per-avatar profiles, independent face/head/body/hand lifecycle controls, and skeletal motion forwarding remain outstanding.
### Next tracking milestones

1. Add per-avatar mapping profiles and visible diagnostics for disabled/conflicting mappings.
2. Add provider status and safe start/stop/reconnect controls using documented provider APIs.
3. Keep VMC skeletal pose, avatar-parameter OSC, and actual tracker devices as separate capability types.
4. Validate multiple providers together, including stale input and parameter conflicts.

---

## Verification gate

- [ ] Open project in Unity 2022.3.62f3, resolve packages, compile without errors.
- [ ] Run EditMode tests (including the 100 authored OSC round-trip cases).
- [ ] Build Windows x64 IL2CPP and smoke-test VRCast.exe.
- [ ] Test live OSC with VRChat/VRCFT and separately verify head, hands and skeletal body tracking.

See [BUILDING.md](BUILDING.md) for the build process.
