# Avatar compatibility and secondary motion

Implementation audit (Unity 2022.3, UniVRM 0.131.3). These are code-path capabilities;
they do **not** imply that every asset has been tested visually.

| Feature | VRM 0.x | VRM 1.0 | VSFAvatar |
| --- | --- | --- | --- |
| Direct file loading | Via UniVRM 0.x-to-1.0 conversion | UniVRM `Vrm10.LoadPathAsync` | **Unsupported**: `.vsfavatar` is not a recognized input |
| Humanoid mesh and transforms | Imported | Imported | No loader |
| Shading | UniVRM-converted MToon10 / UniUnlit materials | MToon10 / UniUnlit | No material importer |
| Emotional and custom expressions | Converted to VRCast BlendShape presets | Converted to VRCast BlendShape presets | No expression mapping |
| Blink / wink | Converted when BlendShape-backed | `blink`, `blinkLeft`, `blinkRight` | Unsupported |
| Mouth shapes | Vowel presets converted by UniVRM | `aa`, `ih`, `ou`, `ee`, `oh` → A / I / U / E / O | Unsupported |
| Spring hair and accessories | UniVRM conversion followed by VRCast PhysBone simulation | Same | Unsupported |
| Spring colliders | Convertible sphere, capsule and plane information | Same | Unsupported |
| Node constraints | Not mapped | Not mapped | Unsupported |
| Material/UV-only expression binds | Not mapped | Not mapped | Unsupported |
| Native format-specific features | May be lost in conversion | May be lost during VRCast conversion | Unsupported |

`VrmLoader` stops UniVRM runtime animation/physics and converts supported data with
`VrmMetadataBuilder`. The application then runs its own expression, lip-sync and
`PhysBoneSimulator` components. VRM 0.x and 1.0 have different source schemas;
the converter is the compatibility boundary. A successful import does not mean
that extensions and all source semantics survived.

## Hair physics expectations

Hair will follow animated or tracked head movement **when the model contains an
appropriate VRM spring chain**, the relevant bone transforms exist, and avatar
physics is enabled. VRCast does not generate spring rigs for hair that has none.
Chains with two or more consecutive joints are converted, while branches and
disconnected joints are not faithfully reconstructed. A single-joint spring is
now accepted only when its sole child has a valid nonzero offset, which supplies
a virtual tip; ambiguous multiple-child tips remain unsupported. The child's
transform stays excluded from that physics chain. VRCast approximates VRM drag,
stiffness, gravity and collider behavior; it is not an exact VRM/VRChat physics
solver. VRChat's PhysBone components are a different source system used by the
`.vrcaster` exporter.

In **Performance → Avatar physics**, toggle secondary motion and choose 15–120
physics updates/second. The diagnostic shows chain count, particle count and
steps per rendered frame. Low render FPS can still reduce the effective rate;
maximum catch-up is three substeps to avoid stalls. Changing the rate can change
motion because the solver has timestep-dependent spring approximations.

For a visual acceptance test, load a licensed VRM with distinct hair chains,
turn head tracking on, move the head left/right and accelerate/stop, then verify
the hair lags and settles without clipping. Compare physics at 15/30/60/120 Hz;
inspect hair tips, collider behavior, and unloading/reloading the avatar. This
requires a real avatar and live Unity rendering and has not been certified by
headless unit tests.

## Source and shader limitations

The `.vrcaster` bundle can contain Unity renderers and pre-exported assets, but
shaders packaged for VRChat may depend on renderer features or texture data not
available in the standalone VRCast player. See [audio-reactive shaders](audio-reactive-shaders.md)
for the limited AudioLink-compatible data provided inside VRCast.

VSFAvatar/VSF SDK packaging is distinct from `.vrm` and `.vrcaster`. Users need
an explicit, safely validated VSFAvatar loader or conversion workflow before
that format can be considered supported; renaming an extension is insufficient.
