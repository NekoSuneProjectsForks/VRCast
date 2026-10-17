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

- [✓] VRChat OSC avatar-parameter sender exposed in Settings with endpoint configuration and float/bool test sends.
- [✗] Live tracking-provider values mapped to OSC avatar parameters; user-defined mappings and conflict-safe parameter ownership.
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
- [✗] Unity EditMode tests/build and live VRChat testing have not been run. UDP send success only confirms the local socket accepted the datagram, not that VRChat received or applied it.
- [✗] The OSC sender is not yet connected to live tracking values, VRCFT provider discovery, user-defined mappings, or skeletal tracking. It is not a complete VRCFT or full-body tracking integration.
- [✗] Audio capture/analysis, AudioLink-inspired mappings, VMD playback, trigger/action workflows, session server, and the wider tracking/provider integrations remain to be implemented and validated.

Status legend: `[✓]` = implementation exists in source; `[✗]` = outstanding or not yet validated end-to-end. Do not describe a feature as complete based only on a roadmap or an isolated helper class.
