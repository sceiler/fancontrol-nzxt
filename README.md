# FanControl.NzxtHub

A [Fan Control](https://github.com/Rem0o/FanControl.Releases) plugin for the **NZXT Control Hub**. Provides speed control and paired RPM readings for all five fan ports. Fan Control manages your curves, temperature sources and profiles.

## Compatibility

- **Controller:** NZXT Control Hub, USB ID `1E71:2022`. Other NZXT controllers and Kraken coolers are not supported.
- **Host:** Windows, x64 Fan Control. Built against the V284 plugin API, targeting .NET Standard 2.0.
- **Scope:** Fan speed control only; no RGB control. Fans sharing a port also share its speed setting.

Fan control has been confirmed on the maintainer's hub. See the [hardware verification record](docs/protocol.md#hardware-verification-record) for details and remaining checks. NZXT CAM, Camless, liquidctl and Python are not required to run the plugin.

### Identify your controller

Open **Windows PowerShell** and list connected NZXT devices with [Get-PnpDevice](https://learn.microsoft.com/en-us/powershell/module/pnpdevice/get-pnpdevice):

```powershell
Get-PnpDevice -PresentOnly |
    Where-Object { $_.InstanceId -like '*VID_1E71&PID_*' } |
    Format-List FriendlyName, InstanceId
```

Look for **`VID_1E71&PID_2022`** in `InstanceId`: `1E71` is the vendor ID and `2022` is the product ID. A different product ID identifies a different model. Multiple USB/HID entries for one hub are normal. Detection is automatic; you do not need to enter an ID in the plugin.

## Install

1. Download **FanControl.NzxtHub-0.1.1.zip** from the [release assets](https://github.com/sceiler/fancontrol-nzxt/releases/tag/v0.1.1) and extract it, or download the DLL directly. The **Source code** archives do not contain the compiled plugin.
2. Exit **NZXT CAM** from its tray icon and disable its automatic startup. Close any other software controlling the same hub, including Camless, liquidctl or RGB utilities.
3. Unblock the downloaded DLL. Open PowerShell in the folder containing `FanControl.NzxtHub.dll` and run:

   ```powershell
   Unblock-File -LiteralPath '.\FanControl.NzxtHub.dll'
   ```

   Alternatively, right-click the DLL → **Properties → Unblock → Apply**, if shown. [Unblock-File](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.utility/unblock-file) removes the same Windows download block.

4. In Fan Control, choose **Settings → Install plugin** and select the DLL. For manual installation, close Fan Control and stop its service if used, copy the DLL into its `Plugins` folder, then restart.
5. Find **NZXT Hub Fan 1–5**, with paired RPM sensors. On an idle PC, test each populated port at **70%**, then **100%**, checking RPM and physical fan response before assigning curves. Monitor temperatures while testing.

## Update or remove

1. Exit Fan Control from its tray icon. If using service mode, open `services.msc` and stop the **FanControl** service.
2. Locate `FanControl.NzxtHub.dll` under the installation's `Plugins` folder, including subfolders. Replace and [unblock](#install) it to update, or delete it to uninstall. Keep only one installed copy and retain your configuration files.
3. Restart the service if used, then Fan Control.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| Plugin does not appear | Unblock the DLL, check it is in `Plugins`, and restart Fan Control and its service. Use x64 Fan Control. |
| Hub is not detected | Confirm `VID_1E71&PID_2022`, check USB/power connections and close competing control software. After attaching a hub, use **Refresh sensors detection**. |
| RPM is unavailable or controls do not respond | Empty ports have no reading. For connected fans, check the logs below before calibrating or assigning curves. |
| Controls change after moving the hub | Without a serial number, sensor IDs use the USB path. Moving the hub to another USB port can change them. |

Logs are next to `FanControl.exe`: **`log.txt`**, plus **`service_log.txt`** in service mode. Search for `[NzxtHub]`. `Received first valid fan status` confirms telemetry is arriving; device discovery alone does not confirm speed control. When reporting a problem, include the hub model, firmware if known, Fan Control/plugin versions and the full error block with its stack trace.

## Cooling behavior

The plugin waits for valid fan status before sending speed commands. It attempts **100% speed on managed ports** when a control is disabled/reset, during clean shutdown, or when its running worker detects stale telemetry or a stalled host. Unmanaged ports are left alone.

**A process crash or broken USB connection can leave the hub at its last speed.** Full-speed fallback is best effort; the plugin does not restore an autonomous fan curve. See [protocol and fallback details](docs/protocol.md).

## Build and test

Requires **.NET SDK 10.0.403** (or a later patch in that feature band), Python 3 and an x64 .NET runtime for tests. On Apple Silicon, use the x64 SDK under Rosetta.

```sh
python3 scripts/prepare_references.py
dotnet build -c Release
dotnet test -c Release
python3 scripts/package.py
```

The .NET SDK compiles the C# plugin to `src/FanControl.NzxtHub/bin/Release/netstandard2.0/FanControl.NzxtHub.dll`. The packaging script creates `dist/FanControl.NzxtHub-0.1.1.zip` and its SHA-256 checksum.

Build references are downloaded from a pinned official Fan Control V284 archive and hash-checked. Fan Control supplies `FanControl.Plugins.dll` and `HidSharp.dll` at runtime; neither is redistributed. Tests cover protocol, session and SDK behavior without Windows or hardware; they do not establish hardware compatibility.

## Credits and license

Created by [Yi Min Yang](https://www.yiminyang.dev/) ([@sceiler](https://github.com/sceiler)), with OpenAI Codex assistance. Inspired by [Camless](https://github.com/kleju13/camless) by @kleju13 and its contributors, with protocol references from Camless and [liquidctl](https://github.com/liquidctl/liquidctl). See [protocol sources and attribution](docs/protocol.md).

[MIT License](LICENSE). Provided as is, without warranty. Third-party material retains its own terms. This is an independent community project, unaffiliated with NZXT, Fan Control or Camless. NZXT, CAM and Kraken are trademarks of NZXT, Inc.
