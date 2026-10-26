namespace FanControl.NzxtHub.Tests;

public sealed class ProtocolTests
{
    internal static readonly HubModel Model = HubModel.Find(0x2022)!;

    internal static byte[] Status()
    {
        // Synthetic 64-byte 67/02 report: five populated ports with distinct RPM
        // and duty readings, to catch off-by-one and byte-order errors.
        var bytes = new byte[64];
        Convert.FromHexString("6702").CopyTo(bytes, 0);
        Convert.FromHexString("0201020201").CopyTo(bytes, 16);
        Convert.FromHexString("B00420038403E803DC05").CopyTo(bytes, 24);
        Convert.FromHexString("281E32465A").CopyTo(bytes, 40);
        return bytes;
    }

    [Fact]
    public void RecognizesOnlyTheUsersHub()
    {
        Assert.Equal(5, Model.PortCount);
        foreach (var other in new[] { 0x2012, 0x2021, 0x3012, 0x1711, 0x2015, 0x2020, 0 })
            Assert.Null(HubModel.Find(other));
    }

    [Fact]
    public void InitializesFanReportingWithoutSpeedOrLightingCommands()
    {
        var reports = Protocol.Initialize();
        Assert.Equal(2, reports.Length);
        Assert.Equal(Convert.FromHexString("600201E80101E801"), reports[0][..8]);
        Assert.Equal(Convert.FromHexString("6601"), reports[1][..2]);
        Assert.All(reports, report => Assert.Equal(64, report.Length));
        Assert.All(reports[0][8..], value => Assert.Equal(0, value));
        Assert.All(reports[1][2..], value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData(64, 64, true)]
    [InlineData(64, 512, true)]
    [InlineData(64, 63, false)]
    [InlineData(0, 512, false)]
    [InlineData(65, 512, false)]
    public void AcceptsLargerOutputDescriptorsWithoutAcceptingOtherInputLayouts(int input, int output, bool expected)
    {
        Assert.Equal(expected, Protocol.CanUseReports(input, output));
    }

    [Theory]
    [InlineData(0, "6201013700000000")]
    [InlineData(1, "6201020037000000")]
    [InlineData(2, "6201040000370000")]
    [InlineData(3, "6201080000003700")]
    [InlineData(4, "6201100000000037")]
    public void DutyWritesTouchOnlyTheSelectedPort(int port, string expected)
    {
        var report = Protocol.SetDuty(Model, port, 55);
        Assert.Equal(Convert.FromHexString(expected), report[..8]);
        Assert.All(report[8..], value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData(-1)] [InlineData(101)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)]
    public void RejectsInvalidDuties(float duty) => Assert.Throws<ArgumentOutOfRangeException>(() => Protocol.SetDuty(Model, 0, duty));

    [Theory]
    [InlineData(-1)] [InlineData(5)]
    public void RejectsUnknownPorts(int port) => Assert.Throws<ArgumentOutOfRangeException>(() => Protocol.SetDuty(Model, port, 55));

    [Fact]
    public void ReadsAllFiveIndependentPorts()
    {
        var result = Protocol.ParseStatus(Model, Status(), 64)!;
        Assert.Equal(new[] { 1200, 800, 900, 1000, 1500 }, result.Select(f => f.Rpm));
        Assert.Equal(new byte[] { 40, 30, 50, 70, 90 }, result.Select(f => f.Duty));
        Assert.All(result, reading => Assert.True(reading.Connected));
    }

    [Fact]
    public void ParsesTheSharedReportFormatFromAnUpstreamCapture()
    {
        // liquidctl tests/test_smart_device2.py, shared 67/02 format. USB identity
        // bytes are redacted; this is NOT a hardware verification of a 2022 hub.
        var sample = Convert.FromHexString("670200000000000000000000000003000200000000000000FC030000000000000000000000000000322828000000000032282800000000003000000000000000");
        Assert.Equal(64, sample.Length);
        var result = Protocol.ParseStatus(Model, sample, sample.Length)!;
        Assert.Equal(1020, result[0].Rpm);
        Assert.Equal(50, result[0].Duty);
        Assert.False(result[1].Connected);
    }

    [Fact]
    public void RejectsTruncatedUnrelatedAndMalformedReports()
    {
        Assert.Null(Protocol.ParseStatus(Model, Status(), 63));
        Assert.Null(Protocol.ParseStatus(Model, Status(), 65));
        var status = Status();
        status[1] = 1; // 67/01 has a different layout; never guess its offsets.
        Assert.Null(Protocol.ParseStatus(Model, status, 64));
        status = Status();
        status[16] = 3;
        Assert.Null(Protocol.ParseStatus(Model, status, 64));
        status = Status();
        status[44] = 101;
        Assert.Null(Protocol.ParseStatus(Model, status, 64));
    }
}
