# VNyan-style Feature Roadmap

This roadmap describes a VRCast expansion inspired by the *types* of workflows available in VNyan. It is not a plan to copy VNyan's implementation or assets. The goal is to keep VRCast's existing Unity runtime, avatar package format, tracking providers, and lightweight Windows output while adding optional features behind clear settings.

## Phase 1 — Configurable performance cap

- [x] Keep the existing Low load mode, including its tracker-related behaviour.
- [x] Add an explicit FPS cap with common presets from 30 to 360 FPS.
- [x] Keep Auto as the default so older settings retain the existing 30 FPS / 60 FPS behaviour.
- [x] Apply the cap through `Application.targetFrameRate` with VSync disabled.
- [ ] Validate the cap on target hardware and confirm the performance overlay reflects the real render rate.

## Phase 2 — Audio capture and reactive avatar controls

- [✗] Separate music/audio-reactive capture from the existing microphone lip-sync path.
- [✗] Windows WASAPI loopback capture and device/latency diagnostics.
- [✗] RMS, frequency bands, transient/beat detection, mappings, and audio-analysis tests.

### Audio sources

- Preserve the existing microphone input used for lip sync.
- Add a separate audio-reactive source selector so music capture does not interfere with microphone lip sync.
- Support microphone/input devices first, then add Windows WASAPI loopback for desktop output. Investigate process-specific capture where the Windows version supports it.
- Expose capture status, source selection, gain, noise gate, smoothing, and latency diagnostics.
- Keep capture opt-in and stop/release audio resources when the feature is disabled.

### Audio analysis

- Compute normalised RMS/volume and low, mid, and high frequency bands.
- Add transient/beat detection with configurable threshold, attack, release, and smoothing.
- Allow each signal to trigger an expression, animation, or mapped avatar property.
- Keep analysis allocation-free after initialization and run it at a configurable rate independent of rendering FPS.

### AudioLink compatibility boundary

VRCast renders an avatar in its own standalone Unity process. It cannot directly read the AudioLink texture generated inside an arbitrary VRChat world without an explicit bridge from that world. The practical first implementation is therefore an AudioLink-inspired analyser: read the selected audio source, calculate frequency-band signals, and expose those signals to VRCast's own avatar effects and mappings.

If a later bridge is added, it should be optional and documented separately. Do not claim direct in-world AudioLink texture compatibility unless the texture/data is actually received.

## Phase 3 — MMD motion playback

- [✗] VMD parsing, supported bone/morph tracks, retargeting, motion controls, and parser tests.

- Add a VMD motion importer for supported MMD bone and facial-morph tracks.
- Retarget motion to compatible Humanoid avatars and allow users to map MMD morph names to avatar BlendShapes.
- Add a motion library with play, pause, loop, seek, speed, and blend controls.
- Provide an explicit preview and validation step for unsupported bones, missing morphs, and incompatible rigs.
- Synchronise motion with the selected audio source when requested.
- Keep MMD model import (PMX/PMD) separate from motion playback; it has different format, shader, physics, and licensing requirements and should not be implied by VMD support alone.

The first milestone should play VMD motion on an avatar already loaded by VRCast. This avoids making the runtime dependent on a full MMD model importer before basic dance playback works.

## Phase 4 — VNyan-style interactions

- [✗] Versioned trigger/action model, safe persistence, built-in actions, and UI.

- Add a small trigger/action system for hotkeys, audio thresholds, timers, tracking state, and avatar events.
- Start with built-in actions: select expression, play/stop motion, toggle an object, and set a mapped BlendShape.
- Store user mappings as versioned data, not executable scripts.
- Consider a plugin/API surface only after the trigger model is stable.

## Tracking and VRChat OSC integration

- [✓] VRChat OSC avatar-parameter sender exposed in Settings with endpoint configuration and float/bool test sends. The sender also supports signed integer avatar parameters, with a Settings test button sending `VRCastTestGesture = 1`.
- [✓] Optional live face-frame mappings for left/right eye openness and mouth openness, plus smile when the selected provider supplies expression scores; output is rate-limited to 30 updates/second and blank mappings are disabled.
- [✓] Configurable mappings for all 51 ARKit-compatible blendshape channels, with empty mappings disabled and duplicate channel entries normalised.
- [✓] OSC sender integer packet encoding, endpoint address normalization, disposed-socket handling, destination form and persisted IPv4/port validation, parameter control-character rejection, 100 parameterized int/float packet round-trip test cases, invalid tracking-value filtering, and persisted-mapping validation test cases added in source (Unity tests not executed).\n- [✗] Head/body/hand provider mapping, per-avatar profiles, provider conflict resolution, and inbound OSC/VRCFT discovery.
- [✗] Incoming OSC/VRCFT provider discovery, parameter mapping, per-provider status, and conflict resolution.
- [✗] Unified face/head/body/hand provider controls and safe start/stop/reconnect lifecycle.
- [✗] Extend and test VMC receiver selection and calibration workflows; current code already has VMC pose reception and MediaPipe hand/body input, but the requested unified management is not complete.
- [✗] Webcam AI full-body pose inference, calibration, retargeting, recording/replay, and performance controls.
- [✗] Additional VMC receivers, SteamVR/tracked props, ARKit/iFacialMocap/MeowFace/VTube Studio and Leap Motion integrations.
- [✗] Shared-session server, secure rooms, synchronized state, Docker Compose, Windows/Linux hosting and health endpoint.
- [✗] Broader avatar controls, expression calibration/reset, image attachments, transparent screenshots, and HTTP command API.
- [✗] VRM 0.x/1.0/VSFAvatar compatibility audit, custom-content SDK, AudioLink material bridge, and plugin API.

## Performance and compatibility rules

- Preserve existing settings when loading older `settings.json` files.
- Keep audio analysis, tracking, and rendering update rates independently configurable.
- Avoid per-frame allocations and avoid scanning every BlendShape on every frame.
- Make advanced features optional so users can disable them on lower-end hardware.
- Do not remove existing VRCast features or rewrite the avatar-loading pipeline as part of these additions.
- Add EditMode tests for settings sanitisation, audio analysis math, motion parsing, and retargeting edge cases before enabling each feature by default.

## Current implementation status

- [✓] Configurable FPS cap and settings sanitisation are present on `feature/vnyan-audio-mmd-performance`.
- [✓] Reusable VRChat OSC avatar-parameter packet encoder and UDP sender (`Remote/OscParameterSender.cs`) supports float and bool values, defaulting to 127.0.0.1:9000.
- [✓] Added persistent OSC output endpoint settings, Settings UI enable/disable and destination editing, plus test float/bool messages and settings validation tests.
- [✓] Added optional live face mappings for eye openness, mouth openness, and smile scores. Output runs only while tracking is enabled and fresh face data is available, and is capped at 30 updates/second.
- [✓] Added named ARKit-compatible mappings for eyeBlinkLeft, eyeBlinkRight, jawOpen, mouthSmileLeft, mouthSmileRight, and browInnerUp, only when the provider supplies a blendshape array.
- [✓] Added an extensible settings list and UI for all 51 ARKit-compatible blendshape channels; additional channels are opt-in, preserve existing parameter names, and are sent only when present in the input frame.
- [✓] Added settings sanitisation/tests for mapping names; blank mappings are disabled and names containing `/` are rejected.
- [✓] Prevent duplicate ARKit channels and duplicate destination parameter names across named face mappings and the configurable blendshape list; runtime also sends at most one value per destination per update (first mapping wins).
- [✓] Cache custom ARKit blendshape-name lookups per mapping and refresh only when the source name changes, avoiding repeated 51-channel scans on every OSC update.
- [✓] Added a regression test asserting the runtime-only ARKit lookup cache is excluded from JSON settings serialization.
- [✓] Display mapping conflict and invalid-path diagnostics in the OSC settings UI so conflicts can be corrected before runtime.
- [✓] Added EditMode test cases verifying the selected ARKit mapping indices still match their named blendshape channels.
- [✓] Added settings sanitisation coverage for duplicate blendshape entries, duplicate destinations across named/custom mappings, whitespace, invalid OSC path segments, and null entries.
- [✓] Clarified UDP status as “Ready to send” rather than implying remote receipt, and throttled retries for unavailable endpoints.
- [✓] Hardened OSC endpoint validation to reject IPv6 addresses explicitly (the sender uses an IPv4 UDP socket), trim endpoint/parameter whitespace, and reject empty/path-containing parameter names before sending.
- [✓] Added EditMode regression coverage for IPv6 endpoint rejection, invalid/non-finite float parameter sends, independent big-endian float payload bytes, and OSC boolean type tags; tests have been authored but not executed in Unity.
- [✗] Unity EditMode tests/build and live VRChat testing have not been run. UDP send success only confirms the local socket accepted the datagram, not that VRChat received or applied it.
- [✗] Head/body/hand provider mapping, per-avatar profiles, source conflict resolution, inbound OSC/VRCFT discovery, and skeletal tracking forwarding are not implemented. This is not a complete VRCFT or full-body tracking integration.
- [✗] Audio capture/analysis, AudioLink-inspired mappings, VMD playback, trigger/action workflows, session server, and the wider tracking/provider integrations remain to be implemented and validated.

Status legend: `[✓]` = implementation exists in source; `[✗]` = outstanding or not yet validated end-to-end. Do not describe a feature as complete based only on a roadmap or an isolated helper class.
