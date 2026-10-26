using System;

namespace FanControl.NzxtHub;

internal sealed class HubModel
{
    internal const int VendorId = 0x1e71;
    internal int ProductId { get; }
    internal string Name { get; }
    internal int PortCount { get; }

    private HubModel(int productId, string name, int portCount)
    {
        ProductId = productId;
        Name = name;
        PortCount = portCount;
    }

    internal static HubModel? Find(int productId)
    {
        switch (productId)
        {
            case 0x2022:
                return new HubModel(productId, "NZXT Control Hub", 5);
            default:
                return null;
        }
    }
}

internal sealed class FanReading
{
    internal byte Mode { get; }
    internal int Rpm { get; }
    internal byte Duty { get; }
    internal bool Connected => Mode == 1 || Mode == 2;

    internal FanReading(byte mode, int rpm, byte duty)
    {
        Mode = mode;
        Rpm = rpm;
        Duty = duty;
    }
}

internal static class Protocol
{
    internal const int ReportLength = 64;

    // These are maximum HID report capacities, not the fan payload's length.
    // Windows reports 512-byte input and output capacities on the user's hub.
    // Validate the actual fan payload separately in ParseStatus.
    internal static bool CanUseReports(int inputLength, int outputLength) =>
        inputLength >= ReportLength && outputLength >= ReportLength;

    // These buffers begin with the report ID. Do not prepend another zero when
    // passing them to HidSharp; the NZXT reports use numbered report IDs.
    internal static byte[][] Initialize() => new[]
    {
        Report(0x60, 0x02, 0x01, 0xe8, 0x01, 0x01, 0xe8, 0x01),
        Report(0x66, 0x01)
    };

    internal static byte[] SetDuty(HubModel model, int port, float percent)
    {
        if (port < 0 || port >= model.PortCount)
            throw new ArgumentOutOfRangeException(nameof(port));
        if (float.IsNaN(percent) || float.IsInfinity(percent) || percent < 0 || percent > 100)
            throw new ArgumentOutOfRangeException(nameof(percent));
        var report = Report(0x62, 0x01, (byte)(1 << port));
        report[3 + port] = (byte)Math.Round(percent, MidpointRounding.AwayFromZero);
        return report;
    }

    internal static FanReading[]? ParseStatus(HubModel model, byte[] report, int length)
    {
        if (length < ReportLength || length > report.Length || report[0] != 0x67 || report[1] != 0x02)
            return null;
        var fans = new FanReading[model.PortCount];
        for (var port = 0; port < fans.Length; port++)
        {
            var mode = report[16 + port];
            var duty = report[40 + port];
            if (mode > 2 || duty > 100) return null;
            fans[port] = new FanReading(mode, report[24 + 2 * port] | (report[25 + 2 * port] << 8), duty);
        }
        return fans;
    }

    private static byte[] Report(params byte[] prefix)
    {
        var result = new byte[ReportLength];
        Array.Copy(prefix, result, prefix.Length);
        return result;
    }
}
