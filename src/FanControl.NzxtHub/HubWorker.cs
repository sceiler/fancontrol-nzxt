using System;
using System.IO;
using System.Threading;

namespace FanControl.NzxtHub;

internal sealed class HubWorker
{
    private readonly Func<IHubTransport> open;
    private readonly Func<TimeSpan> clock;
    private readonly Action<string> log;
    private readonly HubModel model;
    private readonly ManualResetEvent stop = new ManualResetEvent(false);
    private readonly Thread thread;
    private readonly object lifecycle = new object();
    private bool started;
    private bool stopped;
    private bool disposed;
    private IHubTransport? active;

    internal string Id { get; }
    internal HubState State { get; }
    internal HubModel Model => model;

    internal HubWorker(string id, HubModel model, Func<IHubTransport> open, Func<TimeSpan> clock, Action<string> log)
    {
        Id = id;
        this.model = model;
        this.open = open;
        this.clock = clock;
        this.log = log;
        State = new HubState(model);
        thread = new Thread(Run) { IsBackground = true, Name = "NZXT hub " + id };
    }

    internal void Start()
    {
        lock (lifecycle)
        {
            if (started || stopped) return;
            State.Heartbeat(clock());
            thread.Start();
            started = true;
        }
    }

    internal void RequestStop()
    {
        lock (lifecycle)
        {
            stopped = true;
            if (!disposed) stop.Set();
        }
    }

    internal void Join()
    {
        lock (lifecycle)
        {
            if (!started)
            {
                if (!disposed) stop.Dispose();
                disposed = true;
                return;
            }
        }
        if (!thread.Join(TimeSpan.FromSeconds(4)))
        {
            log("NZXT hub did not stop within 4 seconds; closing its USB handle.");
            try { Volatile.Read(ref active)?.Dispose(); }
            catch (Exception error) { log("USB close failed: " + error.Message); }
        }
        // The worker disposes its wait handle, including after a delayed exit.
    }

    private void Run()
    {
        string? lastError = null;
        try
        {
            while (!stop.WaitOne(0))
            {
                IHubTransport? transport = null;
                try
                {
                    transport = open();
                    Volatile.Write(ref active, transport);
                    State.Disconnected();
                    if (stop.WaitOne(0)) break;
                    foreach (var report in Protocol.Initialize()) transport.Write(report);
                    log("Fan-report initialization sent; waiting for 67 02 status.");
                    var lastValid = clock();
                    var receivedStatus = false;
                    // Read a complete HID report even when its advertised maximum
                    // is larger than the 64-byte fan-status payload.
                    var buffer = new byte[transport.InputReportLength];
                    while (!stop.WaitOne(0))
                    {
                        var length = transport.Read(buffer);
                        var now = clock();
                        if (length > 0 && State.Accept(buffer, length, now))
                        {
                            lastValid = now;
                            if (!receivedStatus)
                                log("Received first valid fan status (" + length + " bytes).");
                            receivedStatus = true;
                            if (lastError != null) log("NZXT Control Hub connection recovered.");
                            lastError = null;
                        }
                        WritePending(transport, now, false);
                        if (now - lastValid >= HubState.StaleAfter)
                            throw new IOException("No valid NZXT fan status for 3 seconds; reconnecting.");
                        // Bound malformed-report floods as well as a fake transport
                        // that returns immediately; normal HID reads already wait.
                        stop.WaitOne(10);
                    }
                }
                catch (Exception error)
                {
                    if (lastError != error.Message)
                    {
                        log(error.Message);
                        lastError = error.Message;
                    }
                }
                finally
                {
                    if (transport != null)
                    {
                        // Only channels previously claimed by Fan Control are
                        // touched. Each is attempted even if another write fails.
                        foreach (var command in State.Pending(clock(), stopping: true))
                        {
                            try { transport.Write(Protocol.SetDuty(model, command.Port, 100)); }
                            catch (Exception error) { log("Could not set fan " + (command.Port + 1) + " to 100%: " + error.Message); }
                        }
                        try { transport.Dispose(); }
                        catch (Exception error) { log("USB dispose failed: " + error.Message); }
                    }
                    Volatile.Write(ref active, null);
                    State.Disconnected();
                }
                if (stop.WaitOne(TimeSpan.FromSeconds(5))) break;
            }
        }
        finally
        {
            lock (lifecycle)
            {
                stop.Dispose();
                disposed = true;
            }
        }
    }

    private void WritePending(IHubTransport transport, TimeSpan now, bool stopping)
    {
        foreach (var command in State.Pending(now, stopping))
        {
            transport.Write(Protocol.SetDuty(model, command.Port, command.Duty));
            State.Sent(command, now);
        }
    }
}
