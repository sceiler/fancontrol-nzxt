# FanControl.NzxtHub

Native C# Fan Control plugin for documented NZXT USB fan controllers. Fan Control owns curves; this plugin exposes RPM and duty controls.

- Read `docs/protocol.md` before changing device communication. Use only commands with a primary source. Never write firmware or flash.
- Keep hardware-specific code behind `IHubTransport`. Protocol and session tests must run without Windows or attached hardware.
- Preserve stable sensor IDs and the `netstandard2.0` plugin target. The host provides `FanControl.Plugins.dll` and `HidSharp.dll`; never redistribute them.
- Never claim hardware support is verified from a passing build or fake transport test. Record the actual model, firmware and observations after a hardware test.
- Work on a feature branch. Do not publish a repository or release without an explicit destination from the user.

Build: `python3 scripts/prepare_references.py`, then `dotnet build -c Release`.
Tests: `dotnet test -c Release`.

There is no runtime dependency on Camless, Python, liquidctl or NZXT CAM.
