# VNyan-style Feature Roadmap

This roadmap describes a VRCast expansion inspired by the *types* of workflows available in VNyan. It is not a plan to copy VNyan's implementation or assets. The goal is to keep VRCast's existing Unity runtime, avatar package format, tracking providers, and lightweight Windows output while adding optional features behind clear settings.

## Phase 1 — Configurable performance cap

- [x] Keep the existing Low load mode, including its tracker-related behaviour.
- [x] Add an explicit FPS cap with common presets from 30 to 360 FPS.
- [x] Keep Auto as the default so older settings retain the existing 30 FPS / 60 FPS behaviour.
- [x] Apply the cap through `Application.targetFrameRate` with VSync disabled.
- [ ] Validate the cap on target hardware and confirm the performance overlay reflects the real render rate.

## Phase 2 — Audio capture and reactive avatar controls

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

- Add a VMD motion importer for supported MMD bone and facial-morph tracks.
- Retarget motion to compatible Humanoid avatars and allow users to map MMD morph names to avatar BlendShapes.
- Add a motion library with play, pause, loop, seek, speed, and blend controls.
- Provide an explicit preview and validation step for unsupported bones, missing morphs, and incompatible rigs.
- Synchronise motion with the selected audio source when requested.
- Keep MMD model import (PMX/PMD) separate from motion playback; it has different format, shader, physics, and licensing requirements and should not be implied by VMD support alone.

The first milestone should play VMD motion on an avatar already loaded by VRCast. This avoids making the runtime dependent on a full MMD model importer before basic dance playback works.

## Phase 4 — VNyan-style interactions

- Add a small trigger/action system for hotkeys, audio thresholds, timers, tracking state, and avatar events.
- Start with built-in actions: select expression, play/stop motion, toggle an object, and set a mapped BlendShape.
- Store user mappings as versioned data, not executable scripts.
- Consider a plugin/API surface only after the trigger model is stable.

## Performance and compatibility rules

- Preserve existing settings when loading older `settings.json` files.
- Keep audio analysis, tracking, and rendering update rates independently configurable.
- Avoid per-frame allocations and avoid scanning every BlendShape on every frame.
- Make advanced features optional so users can disable them on lower-end hardware.
- Do not remove existing VRCast features or rewrite the avatar-loading pipeline as part of these additions.
- Add EditMode tests for settings sanitisation, audio analysis math, motion parsing, and retargeting edge cases before enabling each feature by default.

## Current implementation status

The configurable FPS cap is implemented on the `feature/vnyan-audio-mmd-performance` branch. Audio capture/analysis, AudioLink-inspired mappings, VMD playback, and trigger/action workflows remain planned work and must be implemented and tested before being described as available features.
