# FanControl.NzxtHub

By [Yi Min Yang](https://www.yiminyang.dev/) · [GitHub: @sceiler](https://github.com/sceiler)

A native Fan Control plugin for the **NZXT Control Hub, USB `1E71:2022`**. It exposes the hub's five fan ports as speed controls paired with measured RPM sensors. Fan Control handles the curves, temperature sources, calibration and profiles.

**Status: hardware-test prerelease. Windows Fan Control V284 invoked the plugin and reported the hub's HID descriptors; physical RPM/control operation remains unverified.**

Download **FanControl.NzxtHub-0.1.1.zip** or the standalone DLL from the [v0.1.1 release assets](https://github.com/sceiler/fancontrol-nzxt/releases/tag/v0.1.1). GitHub's automatic source archives do not contain the compiled plugin.

Version 0.1.1 fixes the `input=512, output=512` discovery rejection in 0.1.0 and sizes USB read buffers to the input descriptor.

This independent community project is **not affiliated with, endorsed by, sponsored by, or supported by NZXT**. NZXT, CAM and Kraken are trademarks of NZXT, Inc. This is also not an official Fan Control or Camless project.

> [!WARNING]
> This plugin controls cooling hardware. Incorrect curves, software faults or communication failures can cause overheating, hardware damage or data loss. Use at your own risk and monitor temperatures while testing. Full-speed fallback is best effort: a crash or broken USB connection can leave the hub at its last duty. The software is provided **as is, without warranty**, to the extent permitted by law; see [LICENSE](LICENSE).

## Origins and credits

[Camless by @kleju13 and its contributors](https://github.com/kleju13/camless) inspired this project. It demonstrated that third-party software could communicate with NZXT hardware and control fans connected to an NZXT Control Hub—the hub normally managed through NZXT CAM. That prompted the idea of bringing this capability to Fan Control as a plugin.

The plugin was developed with OpenAI Codex, consulting Camless's Control Hub implementation, [liquidctl](https://github.com/liquidctl/liquidctl), and the [Fan Control plugin documentation](https://github.com/Rem0o/FanControl.Releases/wiki/Plugins). Thanks to those projects and their contributors for their protocol research, documentation and examples.

This is a C# Fan Control plugin with no Camless runtime dependency. Protocol references and test capture attribution are listed in [docs/protocol.md](docs/protocol.md).

## License

This project's own code and documentation are licensed under the **MIT License**; see [LICENSE](LICENSE). You may use, modify and redistribute them, including commercially, provided you retain the required copyright and permission notice.

Third-party material retains its applicable terms. This license does not relicense Camless, liquidctl, Fan Control or HidSharp.

## Install

1. Use **x64 Fan Control**. This build uses the V284 public plugin API and targets .NET Standard 2.0; both V284 host variants provide that API. Hardware operation still needs verification.
2. Exit **NZXT CAM** from its tray icon and disable its automatic startup. Also stop any liquidctl, Camless or RGB tool that opens this same hub. Do not run competing controllers together.
3. Extract `FanControl.NzxtHub.dll` from the ZIP. If Windows shows an **Unblock** checkbox under the file's **Properties**, select it.
4. In Fan Control, choose **Settings → Install plugin** and select the DLL. Alternatively, close Fan Control (and stop its service if installed), put the DLL in its `Plugins` folder, and start it again.
5. Find **NZXT Hub Fan 1–5**. Each control is automatically paired with its RPM sensor. Empty ports have an unavailable reading.
6. On an idle PC, check one populated port at **70%**, then **100%**. Confirm both the physical fan response and measured RPM before assigning a curve. Repeat for the other populated ports.

Fans on a splitter or combined fan assembly attached to one hub port share that port's speed setting. There is no separate control for each physical fan behind a shared port.

The plugin changes fan speeds only. It sends no lighting commands. The last lighting state may remain, but lighting after a restart is not managed by this plugin.

## Update or remove

1. Exit Fan Control completely from its tray icon. If service mode is enabled, press **Win + R**, run `services.msc`, and stop the **FanControl** service.
2. In the installation folder's `Plugins` directory (including any subfolders), find `FanControl.NzxtHub.dll`.
3. To update, replace that DLL with the new release's DLL and unblock it in **Properties** if needed. Keep one installed copy. To uninstall, remove the DLL.
4. Restart the service if used, then Fan Control. Keep your configuration files when updating.

After updating, the log should include `[NzxtHub] Starting NZXT Hub plugin 0.1.1.0.` For the reported 512-byte hub, expect `Accepted NZXT 2022 HID collection: input=512, output=512 bytes.` followed by `Found 1 NZXT Control Hub(s).` A `Received first valid fan status` message confirms that telemetry parsing has started; discovery alone does not confirm working speed control.

## Runtime behavior

- Discovers only `VID_1E71&PID_2022`; leaves Kraken coolers and other controller models to their existing drivers.
- Initializes fan reporting and waits for a valid `67 02` status report before sending any speed command.
- Uses the reported PWM/DC mode and RPM; does not guess whether a zero-RPM port has a fan.
- Runs USB reads and writes on a background thread. Host callbacks read cached values.
- Keeps channel IDs stable using the controller serial number, or its USB path if no serial is available. Moving a serial-less controller to another USB port can change its IDs.
- Reinitializes and reconnects an already-discovered hub after a communication failure, retrying every five seconds. After attaching an additional hub, use Fan Control's **Refresh sensors detection**.
- A missing/invalid status becomes unavailable after three seconds. A running worker attempts **100% on managed ports** when status is stale or the host has not called `Update()` for five seconds.
- Disabling/resetting a previously managed port sets it to **100%** before releasing it. Ports never assigned by Fan Control are left alone. Closing the plugin also attempts 100% on managed ports.

There is no documented command to restore an autonomous fan curve on this controller. **A killed or crashed Fan Control process cannot run cleanup; the hub may retain its last duty.** The software watchdog only works while the plugin's worker is alive, and no software can send a fallback through a broken USB connection.

## First hardware verification

Record the hub firmware version shown in CAM before closing CAM, then check:

- All populated ports appear with plausible RPM and correctly match the physical port numbers.
- Changing one port leaves the others' speeds unchanged; 70% and 100% produce an appropriate response.
- A saved curve survives a Fan Control restart without being assigned to a different port.
- Disabling a managed control raises that port to full speed, and exiting the hardware-controlling service raises managed ports to full speed.
- Sleep/resume restores readings and curve control.

If no readings appear, inspect `log.txt` next to `FanControl.exe`; in service mode, also inspect `service_log.txt`. Messages prefixed `[NzxtHub]` include plugin version, discovery count, accepted/rejected report sizes, initialization progress, the first valid status, open failures, stale status and reconnection. For an initialization failure, share the full error block and stack trace, including lines without the prefix, rather than repeatedly calibrating an unresponsive control.

## Build and test

Requirements: **.NET SDK 10.0.403** (or a later patch in that feature band), Python 3 to download build references, and an x64 .NET runtime for tests. On an Apple Silicon development machine, tests need the x64 SDK under Rosetta because Fan Control's public SDK assembly is x64.

```sh
python3 scripts/prepare_references.py
dotnet build -c Release
dotnet test -c Release
python3 scripts/package.py
```

The reference script downloads an official, pinned V284 archive and checks its SHA-256 and both DLL hashes. The host's proprietary `FanControl.Plugins.dll` and its `HidSharp.dll` are build references only: neither is committed nor included in the output package. Python, Camless and liquidctl are not runtime dependencies.

### How the DLL is created

Codex assisted with development; the DLL is compiled by the .NET SDK's C# compiler through MSBuild when you run `dotnet build -c Release`. It is a managed .NET Standard 2.0 class library loaded by Fan Control, not an NZXT driver or firmware image. The project references the pinned Fan Control API and HidSharp assemblies with `Private=false`, so the host supplies them at runtime. `scripts/package.py` packages the already-built DLL, documentation into `dist/FanControl.NzxtHub-0.1.1.zip` and writes a SHA-256 checksum; it does not compile code.

These commands explain the build process in this repository. The inspected history contains no build attestation establishing the exact environment or commands used for the existing release DLL, and no byte-for-byte rebuild comparison was performed. For future releases, record the source commit, SDK version, build log and artifact hash, and provide the matching source alongside the binary. A checksum verifies integrity against a trusted checksum, not authorship or build provenance.

Build output: `src/FanControl.NzxtHub/bin/Release/netstandard2.0/FanControl.NzxtHub.dll`.

Tests cover packet encoding, 64/512-byte HID descriptor capacities and read buffers, padded status packets, a redacted upstream status fixture for the shared packet format, all five ports, invalid reports, stale data, host stalls, shutdown, reset races, reconnection through a fake USB transport, sensor pairing, stable identities and the real Fan Control SDK contract. They do not exercise the Windows HID API or actual hardware.

See [protocol sources](docs/protocol.md) for the commands and evidence used.
