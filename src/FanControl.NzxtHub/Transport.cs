using System;
using System.IO;
using HidSharp;

namespace FanControl.NzxtHub;

internal interface IHubTransport : IDisposable
{
    int Read(byte[] buffer);
    void Write(byte[] report);
}

internal sealed class HidTransport : IHubTransport
{
    private readonly HidStream stream;

    internal HidTransport(HidDevice device)
    {
        var configuration = new OpenConfiguration();
        configuration.SetOption(OpenOption.Exclusive, true);
        configuration.SetOption(OpenOption.TimeoutIfInterruptible, 0);
        configuration.SetOption(OpenOption.TimeoutIfTransient, 0);
        if (!device.TryOpen(configuration, out stream))
            throw new IOException("Cannot open the NZXT Control Hub. Exit NZXT CAM and any other tool using this hub.");
        stream.ReadTimeout = 150;
        stream.WriteTimeout = 500;
    }

    public int Read(byte[] buffer)
    {
        try { return stream.Read(buffer, 0, buffer.Length); }
        catch (TimeoutException) { return 0; }
    }

    public void Write(byte[] report) => stream.Write(report);
    public void Dispose() => stream.Dispose();
}
