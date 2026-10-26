using System.Collections.Concurrent;
using FanControl.Plugins;

namespace FanControl.NzxtHub.Tests;

public sealed class WorkerTests
{
    private sealed class FakeTransport : IHubTransport
    {
        internal readonly ConcurrentQueue<byte[]> Writes = new();
        internal readonly BlockingCollection<byte[]> Reads = new();
        internal volatile bool Disposed;
        internal volatile bool FailReads;

        public int Read(byte[] buffer)
        {
            if (FailReads) throw new IOException("Disconnected test hub");
            if (!Reads.TryTake(out var bytes, 20)) return 0;
            bytes.CopyTo(buffer, 0);
            return bytes.Length;
        }

        public void Write(byte[] report)
        {
            if (Disposed || FailReads) throw new IOException("Disconnected test hub");
            Writes.Enqueue((byte[])report.Clone());
        }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void WorkerPairsSensorsWritesDutyAndLeavesManagedFanAtFullSpeedOnClose()
    {
        var transport = new FakeTransport();
        transport.Reads.Add(ProtocolTests.Status());
        var worker = new HubWorker("test", ProtocolTests.Model, () => transport, () => TimeSpan.Zero, _ => { });
        var rpm = new RpmSensor(worker, 2, () => TimeSpan.Zero);
        var control = new DutySensor(worker, 2, rpm.Id, () => TimeSpan.Zero, _ => { });
        Assert.IsAssignableFrom<IPluginControlSensor2>(control);
        Assert.Equal(rpm.Id, control.PairedFanSensorId);
        worker.Start();
        worker.Start(); // A repeated host Load must not start another USB thread.
        try
        {
            control.Set(45);
            Assert.True(SpinWait.SpinUntil(() => transport.Writes.Any(r => r[0] == 0x62 && r[5] == 45), 2000));
            Assert.Equal(900f, rpm.Value);
            Assert.Equal(50f, control.Value); // Observed duty, not the requested value.
        }
        finally { worker.RequestStop(); worker.Join(); }
        worker.RequestStop();
        worker.Join();
        Assert.True(transport.Disposed);
        var duties = transport.Writes.Where(r => r[0] == 0x62).ToArray();
        Assert.All(duties, r => Assert.Equal(4, r[2]));
        Assert.Equal(100, duties[^1][5]);
    }

    [Fact]
    public void CloseBeforeLoadIsSafeAndIdempotent()
    {
        var worker = new HubWorker("test", ProtocolTests.Model,
            () => throw new InvalidOperationException("Must not open"), () => TimeSpan.Zero, _ => { });
        worker.RequestStop();
        worker.Join();
        worker.RequestStop();
        worker.Join();
        worker.Start();
    }

    [Fact]
    public void ReconnectReinitializesAndReplaysOnlyTheManagedChannel()
    {
        var first = new FakeTransport();
        var second = new FakeTransport();
        first.Reads.Add(ProtocolTests.Status());
        second.Reads.Add(ProtocolTests.Status());
        var opens = 0;
        var worker = new HubWorker("test", ProtocolTests.Model,
            () => Interlocked.Increment(ref opens) == 1 ? first : second, () => TimeSpan.Zero, _ => { });
        worker.State.Set(4, 65);
        worker.Start();
        try
        {
            Assert.True(SpinWait.SpinUntil(() => first.Writes.Any(r => r[0] == 0x62), 2000));
            first.FailReads = true;
            Assert.True(SpinWait.SpinUntil(() => first.Disposed, 2000));
            Assert.Null(worker.State.Read(4, TimeSpan.Zero));
            Assert.True(SpinWait.SpinUntil(() => second.Writes.Any(r => r[0] == 0x62 && r[7] == 65), 7000));
            Assert.Equal(0x60, second.Writes.ElementAt(0)[0]);
            Assert.Equal(0x66, second.Writes.ElementAt(1)[0]);
            Assert.All(second.Writes.Where(r => r[0] == 0x62), r => Assert.Equal(16, r[2]));
        }
        finally { worker.RequestStop(); worker.Join(); }
    }

    [Fact]
    public void StableIdsDoNotDependOnEnumerationOrderOrSerialCase()
    {
        Assert.Equal(NzxtHubPlugin.DeviceId("ABC", "path1"), NzxtHubPlugin.DeviceId("abc", "path2"));
        Assert.NotEqual(NzxtHubPlugin.DeviceId("ABC", "path1"), NzxtHubPlugin.DeviceId("DEF", "path1"));
        Assert.NotEqual(NzxtHubPlugin.DeviceId(null, "path1"), NzxtHubPlugin.DeviceId(null, "path2"));
    }

    [Fact]
    public void PublicAssemblySurfaceIsOnlyThePluginEntryPoint()
    {
        Assert.Equal(typeof(NzxtHubPlugin), Assert.Single(typeof(NzxtHubPlugin).Assembly.GetExportedTypes()));
        Assert.True(typeof(IPlugin2).IsAssignableFrom(typeof(NzxtHubPlugin)));
    }
}
