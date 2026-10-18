# Repeatable VRCast performance evaluation

Do not claim an optimization from an unmeasured FPS change. The **Performance**
tab sets render FPS, physics rate and shows the physics chain/particle counts.
The footer displays application CPU, measured GPU frame milliseconds (if the
graphics API supports them), average/max frame time, managed heap size and
Gen0 collections per 1-second window, alongside tracking frequency. GPU % is
only the portion of a frame spent drawing in VRCast; it is not whole-card usage.

## Baseline and comparison procedure

1. Record CPU/GPU, Unity version, resolution/render scale, avatar and shader,
   graphics API, tracker source, session peer count and exact settings.
2. Warm the application up for 60 seconds with the same avatar. Then measure
   120 seconds per test. Capture per-second footer readings or record a Unity
   Profiler trace; note averages, p95/p99 frame time, peak frame time, managed
   heap, Gen0 collection spikes, GPU milliseconds and application CPU.
3. Repeat with physics disabled/enabled and 15/30/60/120 Hz, and with the
   audio-reactive feature off/on at 5/30/60 Hz, keeping other variables fixed.
4. Repeat idle and with head/hair motion, then with two session clients, induced
   100/250ms latency and 1/5% packet loss (via a controlled test proxy). Measure
   reconnect time, dropped/incoming queue metrics and remote interpolation.
5. Run a 1-hour soak with avatar reload and source unplug/replug events, then
   compare memory use and GC behavior against the 2-minute baseline.

| Metric | Before | After | Data source |
| --- | --- | --- | --- |
| CPU %, avg/p95 frame ms | *not measured* | *not measured* | VRCast footer / Unity Profiler |
| GPU frame ms, max frame spike | *not measured* | *not measured* | FrameTimingManager / Profiler |
| Managed memory, GC0/min | *not measured* | *not measured* | VRCast footer / Profiler |
| Network bytes/s, latency, packet loss | *not measured* | *not measured* | Packet capture / simulated network |
| Reconnect duration and 1h memory growth | *not measured* | *not measured* | Session fixture / soak |

The table intentionally contains no fabricated numbers. Unity GPU measurements
require a real graphics session; EditMode and the headless server suite cannot
substitute for visual, GPU, tracking, or sustained-load acceptance testing.
