# FanControl.NzxtHub

A native Fan Control plugin for the **NZXT Control Hub, USB `1E71:2022`**. It exposes the hub's five fan ports as speed controls paired with measured RPM sensors. Fan Control handles the curves, temperature sources, calibration and profiles.

**Status: initial build, automated tests only. Not yet tested in Windows Fan Control or on a physical hub.**

## Install

1. Use the current **x64 Fan Control** release. This build uses the V284 public plugin API and targets .NET Standard 2.0; both V284 host variants provide that API. Windows host loading still needs verification.
2. Exit **NZXT CAM** from its tray icon and disable its automatic startup. Also stop any liquidctl, Camless or RGB tool that opens this same hub. Do not run competing controllers together.
3. Extract `FanControl.NzxtHub.dll` from the ZIP. If Windows shows an **Unblock** checkbox under the file's **Properties**, select it.
4. In Fan Control, choose **Settings → Install plugin** and select the DLL. Alternatively, close Fan Control (and stop its service if installed), put the DLL in its `Plugins` folder, and start it again.
5. Find **NZXT Hub Fan 1–5**. Each control is automatically paired with its RPM sensor. Empty ports have an unavailable reading.
6. On an idle PC, check one populated port at **70%**, then **100%**. Confirm both the physical fan response and measured RPM before assigning a curve. Repeat for the other populated ports.

Fans on a splitter or combined fan assembly attached to one hub port share that port's speed setting. There is no separate control for each physical fan behind a shared port.

The plugin changes fan speeds only. It sends no lighting commands. The last lighting state may remain, but lighting after a restart is not managed by this plugin.

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

If no readings appear, inspect Fan Control's log for `[NzxtHub]`. In service mode, inspect the service log. Messages include discovery count, unexpected report sizes, open failures, stale status and reconnection. Share those messages rather than repeatedly calibrating an unresponsive control.

## Build and test

Requirements: **.NET SDK 10.0.403** (or a later patch in that feature band), Python 3 to download build references, and an x64 .NET runtime for tests. On an Apple Silicon development machine, tests need the x64 SDK under Rosetta because Fan Control's public SDK assembly is x64.

```sh
python3 scripts/prepare_references.py
dotnet build -c Release
dotnet test -c Release
python3 scripts/package.py
```

The reference script downloads an official, pinned V284 archive and checks its SHA-256 and both DLL hashes. The host's proprietary `FanControl.Plugins.dll` and its `HidSharp.dll` are build references only: neither is committed nor included in the output package. Python, Camless and liquidctl are not runtime dependencies.

Build output: `src/FanControl.NzxtHub/bin/Release/netstandard2.0/FanControl.NzxtHub.dll`.

Tests cover packet encoding, a redacted upstream status fixture for the shared packet format, all five ports, invalid reports, stale data, host stalls, shutdown, reset races, reconnection through a fake USB transport, sensor pairing, stable identities and the real Fan Control SDK contract. They do not exercise the Windows HID API or actual hardware.

See [protocol sources](docs/protocol.md) for the commands and evidence used.
