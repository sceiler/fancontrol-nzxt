# NZXT Control Hub protocol references

Target: USB vendor `1e71`, product `2022`, five fan ports. Other NZXT devices use other initialization or status formats and are deliberately excluded.

Primary implementation references inspected:

- [liquidctl Control Hub driver, commit 292dc561](https://github.com/liquidctl/liquidctl/blob/292dc561361cfbc0cdee155c26662131fe160989/liquidctl/driver/control_hub.py)
- [liquidctl Control Hub tests and capture notes](https://github.com/liquidctl/liquidctl/blob/292dc561361cfbc0cdee155c26662131fe160989/tests/test_control_hub.py)
- [liquidctl shared SmartDevice2 report tests](https://github.com/liquidctl/liquidctl/blob/292dc561361cfbc0cdee155c26662131fe160989/tests/test_smart_device2.py)
- [Camless Control Hub driver, commit 3fb88e81](https://github.com/kleju13/camless/blob/3fb88e81a20df862d52a2becb8eae888e94f524d/src/drivers/control_hub.rs), which reports hardware testing of fan duties and lighting and cites CAM 4.76.6 USB captures.
- [Fan Control public plugin contract](https://github.com/Rem0o/FanControl.Releases/wiki/Plugins), verified against the public types of the actual V284 `FanControl.Plugins.dll`.
- [HIDSharp upstream](https://github.com/SeekHisKingdom/HIDSharp). The V284 modern host ships version 2.6.4 targeting .NET Standard 2.0.

This is an independent C# implementation of the documented packet fields. It does not embed or run those upstream controller drivers. The status test fixture reuses hardware report data from liquidctl's shared-format test, with identity bytes redacted; this fixture is not a Control Hub hardware test. The capture originates from [liquidctl issue #292](https://github.com/liquidctl/liquidctl/issues/292#issuecomment-786876335); upstream set the connected fan mode to PWM, and this project's test redacts identity bytes. Credit to the liquidctl authors and contributors; their repository's [GPLv3 license](https://github.com/liquidctl/liquidctl/blob/292dc561361cfbc0cdee155c26662131fe160989/LICENSE.txt) remains available upstream.

## Transport and initialization

Fan commands and the documented fan-status layout use 64 bytes, with the numbered report ID at byte zero. Do not prepend an additional zero byte. HID descriptor capacities describe the **maximum** report length, including the report ID, rather than the fan payload length ([Microsoft HIDP_CAPS documentation](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/ns-hidpi-_hidp_caps)).

The user's Windows V284 log on 2026-10-26 reports **input=512, output=512** for `1e71:2022`. Version 0.1.0 incorrectly required input capacity to equal 64, rejecting the hub before opening it. Version 0.1.1 accepts matching collections with input and output capacities of at least 64 bytes and reads into a buffer sized to `GetMaxInputReportLength()`, as HidSharp's own `Read()` helper does. Status parsing uses the actual received length, requires at least 64 bytes and the `67 02` prefix, and reads only documented fan fields. Trailing bytes are ignored. HidSharp handles Windows output padding; commands retain their 64-byte encoding.

| Operation | Prefix (hex), zero padded to 64 bytes |
| --- | --- |
| Enable periodic fan reports at 0.5 s | `60 02 01 e8 01 01 e8 01` |
| Detect fans / enable hub fan reporting | `66 01` |
| Set one port's duty | `62 01 MASK D1 D2 D3 D4 D5` |

For zero-based port `i`, `MASK = 1 << i` and duty `Di` is written at offset `3 + i`. Other duty bytes are zero but their mask bits are clear, so those ports are not targeted. Duty is an integer percentage from 0 to 100.

No firmware upload, flash, lighting or accessory-configuration commands are sent. Firmware/LED queries are unnecessary for fan control and omitted.

## Status

Accept only a complete `67 02` report:

| Field | Offset for zero-based port i |
| --- | --- |
| Connection/control mode | `16 + i`: 0 absent, 1 DC, 2 PWM |
| RPM | little-endian uint16 at `24 + 2*i` |
| Observed duty | `40 + i` |

The liquidctl Control Hub test includes a captured **`67 01`** packet but rewrites its prefix to `67 02` in its test. That does not prove the two layouts are interchangeable. This implementation **rejects `67 01`** and waits for the periodic `67 02` report used by both upstream drivers. A firmware that never provides it requires actual report evidence before extending the parser.

## Fallback limits

No source inspected documents an automatic/BIOS hand-back command or a stored thermal curve for this hub. Reset and clean shutdown therefore use 100% on previously claimed ports. A software watchdog cannot protect against process termination, power loss or an unusable USB connection. The hub's own behavior in those scenarios must be checked on hardware; no autonomous recovery is claimed.

## Hardware verification record

- Maintainer: Yi Min Yang (@sceiler).
- Hardware: NZXT Control Hub, Windows hardware ID `VID_1E71&PID_2022`, five fan ports.
- The maintainer confirmed on 2026-10-27 that the plugin works and controls fans on their hardware.
- Fan Control V284 previously reported input/output HID capacities of 512 bytes. Version 0.1.1 fixes the earlier discovery rejection and undersized read buffer.
- Build references are pinned to Fan Control V284. The firmware version and current host version were not supplied with the working-hardware confirmation.
- This records the maintainer's hardware confirmation, rather than inferring operation from automated tests. Separate results for fan-port isolation, reset, shutdown and sleep/resume were not supplied.
