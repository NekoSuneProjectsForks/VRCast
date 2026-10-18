# Building VRCast from source

This guide describes the **VRCast standalone Unity application** and the separate **avatar converter package** in this fork. See [README.md](README.md) for features and [TODO.md](TODO.md) for implementation status.

## Required Unity versions

| Component | Unity Editor | Reason |
| --- | --- | --- |
| VRCast standalone runtime | **2022.3.62f3** (recommended exact version) | Pinned in [VRCast/ProjectSettings/ProjectVersion.txt](VRCast/ProjectSettings/ProjectVersion.txt). Use Unity 2022.3 LTS, not Unity 6. |
| VRChat avatar project for exporting `.vrcaster` | **2022.3.22f1** | Version documented for the original VRChat/VCC avatar-export workflow. |

**Do not open the standalone project using 2022.3.22f1 merely because your VRChat avatar project uses it.** The standalone runtime and converter have different roles. AssetBundle compatibility is the reason to stay within Unity 2022.3.

## Prerequisites (Windows)

1. Install **Unity Hub** and Unity Editor **2022.3.62f3** with **Windows Build Support (IL2CPP)**. Use a licensed Unity installation appropriate to your usage.
2. Install **Visual Studio 2022** with the **Desktop development with C++** workload (MSVC and Windows SDK) for IL2CPP.
3. Install **Git for Windows** and make sure `git --version` works. The Unity Package Manager fetches UniVRM from GitHub.
4. Ensure internet access for the first package import. UniVRM is pinned to **v0.131.3** through `VRCast/Packages/manifest.json`.
5. Windows x64 is the project's intended build target. The existing README describes Windows 11; other Windows versions have not been verified by this guide.

## Clone and open the Unity project

```powershell
git clone https://github.com/NekoSuneProjectsForks/VRCast.git
cd VRCast
git checkout feature/vnyan-audio-mmd-performance
```

In Unity Hub select **Add project from disk**, then select the **`VRCast` subdirectory inside the repository**, **not** the repository root. Example: `C:\dev\VRCast\VRCast`.

Open with **2022.3.62f3**. Allow the Package Manager to import and compile all dependencies before building. This project uses Unity's **Built-in Render Pipeline**, not URP/HDRP.

The runtime project has a local converter dependency:
```json
"com.vrcast.converter": "file:../../Packages/com.vrcast.converter"
```
Keep the repository's `Packages/com.vrcast.converter` folder next to the `VRCast` Unity project folder; copying only the Unity subfolder will break this relative path.

## Build Windows x64 from Unity Editor

1. Open the runtime project in Unity and wait for Console compilation to finish.
2. Use **VRCast > Build > Windows x64**.
3. The script `VRCast/Assets/VRCast/Editor/Build/VRCastBuild.cs` ensures `Assets/VRCast/Scenes/Main.unity` exists and is included, sets **Windows x64**, **IL2CPP Release**, **Linear color space**, and includes required shaders.
4. On success, look for `VRCast/Builds/Windows/VRCast.exe` (relative to the repository root).
5. Distribute the **whole `Builds/Windows` output folder**, not just the EXE; Unity builds include `VRCast_Data` and supporting files.

**Important:** the build script removes/replaces the prior `Builds/Windows` output. Close a running VRCast instance, OBS, Discord or other programs holding its camera DLLs if cleanup fails.

## Command-line build (Windows)

With Unity Editor installed at the path below, run this from the **repository root**:

```powershell
& "C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe" `
  -batchmode -nographics -quit `
  -projectPath "$PWD\VRCast" `
  -executeMethod VRCast.Editor.Build.VRCastBuild.BuildWindows `
  -logFile "$PWD\vrcast-build.log"
```

Adjust the Unity executable path if installed elsewhere. Inspect `vrcast-build.log` and the process exit code for errors. Do not run a second Unity Editor instance against the same project concurrently.

## Run EditMode tests

In Unity: **Window > General > Test Runner > EditMode > Run All**. The project includes the Unity Test Framework **1.4.6** and EditMode tests under `VRCast/Assets/VRCast/Tests/EditMode`.

Optional command line:

```powershell
& "C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe" `
  -batchmode -nographics -projectPath "$PWD\VRCast" `
  -runTests -testPlatform EditMode `
  -testResults "$PWD\editmode-results.xml" `
  -logFile "$PWD\editmode-tests.log" -quit
```

These instructions are based on project configuration; **the tests and full Windows build have not been executed as part of preparing this guide**.

## Optional native components

The core build script may **warn and continue** if optional native helpers are absent. These features then may not work in the resulting app:

| Feature | Repository helper |
| --- | --- |
| UnityCapture virtual camera | `VRCast/Tools/UnityCapture/fetch.ps1` |
| Spout2 GPU output | `VRCast/Tools/Spout/fetch.ps1` |
| Windows 11 Media Foundation virtual camera | `VRCast/Tools/VirtualCamera/build.bat` |
| Updater executable | `VRCast/Tools/Updater/build.bat` |
| Bundled tracking executables | Place the corresponding executables in the `Trackers` locations expected by the project (check build warnings and README). |

Run third-party download/build scripts only after inspecting their contents and dependencies. Native drivers and virtual cameras may need separate installation/registration steps. Optional feature support is not implied by a successful Unity build.

## Export a VRChat avatar as .vrcaster

1. Open your **separate VRChat avatar/VCC project** in **Unity 2022.3.22f1**.
2. Install the converter by **Window > Package Manager > + > Add package from disk**, selecting `Packages/com.vrcast.converter/package.json` from this clone. Alternatively use **Add package from git URL**:
   `https://github.com/NekoSuneProjectsForks/VRCast.git?path=/Packages/com.vrcast.converter#feature/vnyan-audio-mmd-performance`
3. **Use only one converter installation method**. Do not also import a duplicate `.unitypackage` because duplicate assemblies can fail compilation.
4. Open **VRCast > Avatar Exporter**, choose the avatar root with an Animator, and export a `.vrcaster` file.
5. Start the built `VRCast.exe` and use its Avatar panel to load the exported file. **VRM 0.x/1.0** models can be loaded directly without converter export.

VRChat SDK, Modular Avatar, VRCFury and other avatar tooling are optional to the standalone runtime but may be necessary for your avatar's own project/export processing.

## Troubleshooting

- **Missing converter package:** open the repository's `VRCast` subfolder and keep `Packages/com.vrcast.converter` in the repository root.
- **UniVRM package import fails:** check Git installation, network access and the exact Git URLs in `VRCast/Packages/manifest.json`.
- **IL2CPP build fails:** verify Windows Build Support (IL2CPP), Visual Studio C++ workload and Windows SDK are installed.
- **Missing Spout/virtual camera/tracker:** read the warnings from `VRCastBuild` and fetch/build the optional component you need.
- **AssetBundle import/version mismatch:** use the documented Unity 2022.3 editor versions; don't switch the runtime to Unity 6.
- **New tracking/OSC feature not working:** consult [TODO.md](TODO.md). Authored source/tests do not establish live VRChat/VRCFT or full-body integration compatibility.

## Source of truth

- Runtime Editor version: `VRCast/ProjectSettings/ProjectVersion.txt`.
- Dependencies: `VRCast/Packages/manifest.json`.
- Build method and output path: `VRCast/Assets/VRCast/Editor/Build/VRCastBuild.cs`.
- Original usage, exporter and runtime feature details: [README.md](README.md).
