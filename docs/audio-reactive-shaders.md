# Basic AudioLink data in VRCast

The **Face → Audio-reactive shaders** card enables a local, opt-in music-reactive
data feed. The analyzer reuses the selected microphone input at a configurable
5–60 Hz; it does not interfere with microphone lip-sync processing. To react to
desktop music, select a working **Stereo Mix** capture device or route system
audio to a microphone device using a virtual audio cable. Direct Windows WASAPI
loopback capture, separate independent lip-sync/music devices and per-app audio
capture are not implemented yet.

VRCast publishes a linear 128×64 texture as the global shader property
`_AudioTexture` while the feature is enabled. Its four primary rows follow the
AudioLink band coordinates: `(0,0)` bass (20–250 Hz), `(0,1)` low mids
(250–500 Hz), `(0,2)` high mids (500–2000 Hz), `(0,3)` treble (2000–8000 Hz).
Pixel x=0 is the latest amplitude; x=1..127 contain the latest successive
history samples. Red, green, and blue are equal normalized amplitudes, alpha=1.
The separate global `_VRCastAudioBands` is a `float4` in bass → treble order.
The texture uses linear RGBA32 so Unity's CPU-driven partial `SetPixels` updates
are supported consistently; 8-bit normalized bands are sufficient for this
basic visual effect.
`_AudioLinkEnabled` is 1 while enabled and 0 otherwise. Inputs are analyzed
using a Hann-windowed 2048-point FFT, gain, attack/release smoothing, RMS and a
basic bass transient detector. Buffers are reused after initialization.

**Compatibility boundary:** this supplies only **approximate** four-band values
and a history layout. It is **not a full AudioLink implementation**. AudioLink's
DFT, waveform, ColorChord, chronotensity, VU, theme colors, network time and
filtered-history layouts are unimplemented; even the implemented band magnitudes
and temporal response differ from official AudioLink. Complex Poiyomi/lilToon
or other VRChat audio-reactive shader modes may therefore appear static or look
different. Shaders that sample the global `_AudioTexture` at the basic band
coordinates are the initial compatibility target, provided their shaders are
included and supported by the VRCast player. Data is local to VRCast; audio
playing in a VRChat world is not accessible without a separate bridge.

When disabled, the texture is replaced with black and scalar/vector globals
are zeroed. An unavailable capture device clears any stale levels after a short
grace interval. This avoids a previously reactive avatar remaining lit when
the source disappears. No recording is saved to disk.

Official pass coordinates, names and dimensions: [AudioLink DataAPI](https://github.com/llealloo/audiolink/blob/master/Packages/com.llealloo.audiolink/Runtime/Scripts/AudioLink.DataAPI.cs)
and [AudioLink documentation](https://github.com/llealloo/audiolink/blob/master/Docs/README.md).

For a working built-in-render-pipeline example, select the
`VRCast/AudioReactive/Unlit` shader on a material, assign a base texture and
music glow colour, then choose bass/low mids/high mids/treble and glow strength.
Only materials using this shader (or explicitly sampling the data globals)
will respond automatically. Importing a VRChat avatar with a Poiyomi or lilToon
AudioLink material does not guarantee that its original shader can run here.
