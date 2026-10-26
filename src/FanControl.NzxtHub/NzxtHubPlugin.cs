using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FanControl.Plugins;
using HidSharp;

namespace FanControl.NzxtHub;

public sealed class NzxtHubPlugin : IPlugin2
{
    private readonly IPluginLogger logger;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly List<HubWorker> workers = new List<HubWorker>();
    private bool loaded;

    public string Name => "NZXT Control Hub";

    public NzxtHubPlugin(IPluginLogger logger)
    {
        this.logger = logger;
    }

    public void Initialize()
    {
        Close();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Log("NZXT Control Hub requires Windows; no hardware was opened.");
            return;
        }
        try
        {
            var ids = new HashSet<string>();
            foreach (var device in DeviceList.Local.GetHidDevices(HubModel.VendorId, 0x2022).OrderBy(d => d.DevicePath, StringComparer.Ordinal))
            {
                try
                {
                    var inputLength = device.GetMaxInputReportLength();
                    var outputLength = device.GetMaxOutputReportLength();
                    if (!Protocol.CanUseReports(inputLength, outputLength))
                    {
                        Log("Skipped an NZXT 2022 HID collection: input=" + inputLength + ", output=" + outputLength + " bytes.");
                        continue;
                    }
                    string? serial = null;
                    try { serial = device.GetSerialNumber(); }
                    catch (Exception) { /* Device path is a stable fallback on the same USB port. */ }
                    var id = DeviceId(serial, device.DevicePath);
                    if (!ids.Add(id)) continue;
                    var model = HubModel.Find(device.ProductID)!;
                    workers.Add(new HubWorker(id, model, () => new HidTransport(device), () => clock.Elapsed, Log));
                }
                catch (Exception error) { Log("Could not inspect an NZXT hub: " + error.Message); }
            }
            Log("Found " + workers.Count + " NZXT Control Hub(s). Fan speeds change only when Fan Control assigns a control.");
        }
        catch (Exception error) { Log("NZXT discovery failed: " + error.Message); }
    }

    public void Load(IPluginSensorsContainer container)
    {
        if (loaded) return;
        loaded = true;
        foreach (var worker in workers)
        {
            for (var port = 0; port < worker.Model.PortCount; port++)
            {
                var rpm = new RpmSensor(worker, port, () => clock.Elapsed);
                container.FanSensors.Add(rpm);
                container.ControlSensors.Add(new DutySensor(worker, port, rpm.Id, () => clock.Elapsed, Log));
            }
            // Start only after registering sensors, so a host that drops an empty
            // plugin without Close cannot leave an invisible worker behind.
            worker.Start();
        }
    }

    public void Update()
    {
        var now = clock.Elapsed;
        foreach (var worker in workers) worker.State.Heartbeat(now);
    }

    public void Close()
    {
        foreach (var worker in workers) worker.RequestStop();
        foreach (var worker in workers) worker.Join();
        workers.Clear();
        loaded = false;
    }

    internal static string DeviceId(string? serial, string path)
    {
        var identity = string.IsNullOrWhiteSpace(serial) ? "path:" + path : "serial:" + serial;
        using (var sha = SHA256.Create())
        {
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(identity.ToUpperInvariant()));
            return "nzxt/2022/" + BitConverter.ToString(hash, 0, 12).Replace("-", "").ToLowerInvariant();
        }
    }

    private void Log(string message)
    {
        try { logger.Log("[NzxtHub] " + message); }
        catch (Exception) { /* Host logging must not interrupt cooling or cleanup. */ }
    }
}

internal abstract class HubSensor : IPluginSensor
{
    protected readonly HubWorker Worker;
    protected readonly int Port;
    protected readonly Func<TimeSpan> Clock;
    public string Id { get; }
    public string Name { get; }
    public abstract float? Value { get; }

    protected HubSensor(HubWorker worker, int port, string kind, Func<TimeSpan> clock)
    {
        Worker = worker;
        Port = port;
        Clock = clock;
        Id = worker.Id + "/fan" + (port + 1) + "/" + kind;
        Name = "NZXT Hub Fan " + (port + 1) + " " + kind;
    }

    public void Update() { }
}

internal sealed class RpmSensor : HubSensor
{
    internal RpmSensor(HubWorker worker, int port, Func<TimeSpan> clock) : base(worker, port, "RPM", clock) { }
    public override float? Value => Worker.State.Read(Port, Clock())?.Rpm;
}

internal sealed class DutySensor : HubSensor, IPluginControlSensor2
{
    private readonly Action<string> log;
    public string PairedFanSensorId { get; }
    public override float? Value => Worker.State.Read(Port, Clock())?.Duty;

    internal DutySensor(HubWorker worker, int port, string rpmId, Func<TimeSpan> clock, Action<string> log)
        : base(worker, port, "Control", clock)
    {
        PairedFanSensorId = rpmId;
        this.log = log;
    }

    public void Set(float value)
    {
        try { Worker.State.Set(Port, value); }
        catch (ArgumentOutOfRangeException) { log("Ignored invalid duty for fan " + (Port + 1) + ": " + value); }
    }

    public void Reset() => Worker.State.Reset(Port);
}
