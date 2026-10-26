namespace FanControl.NzxtHub.Tests;

public sealed class StateTests
{
    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);
    private static HubState Ready()
    {
        var state = new HubState(ProtocolTests.Model);
        state.Accept(ProtocolTests.Status(), 64, At(0));
        state.Heartbeat(At(0));
        return state;
    }

    [Fact]
    public void OpeningAndReadingDoesNotChangeAnyFan()
    {
        var state = Ready();
        Assert.Empty(state.Pending(At(0)));
        Assert.Empty(state.Pending(At(10), stopping: true));
        Assert.Equal(1500, state.Read(4, At(1))!.Rpm);
    }

    [Fact]
    public void RequiresAValidReportOnEveryConnectionBeforeWriting()
    {
        var state = new HubState(ProtocolTests.Model);
        state.Set(2, 45);
        Assert.Empty(state.Pending(At(0)));
        state.Accept(ProtocolTests.Status(), 64, At(0));
        Assert.Equal(45, Assert.Single(state.Pending(At(0))).Duty);
        state.Disconnected();
        Assert.Null(state.Read(0, At(0)));
        Assert.Empty(state.Pending(At(0)));
        state.Accept(ProtocolTests.Status(), 64, At(0));
        Assert.Equal(45, Assert.Single(state.Pending(At(0))).Duty);
    }

    [Fact]
    public void DoesNotTreatAnEmptyPortAsAStoppedConnectedFan()
    {
        var state = Ready();
        var bytes = ProtocolTests.Status();
        bytes[18] = 0;
        state.Accept(bytes, 64, At(0));
        state.Set(2, 30);
        Assert.Null(state.Read(2, At(0)));
        Assert.Empty(state.Pending(At(0)));
    }

    [Fact]
    public void StaleTelemetryBecomesUnknownAndForcesManagedFansHigh()
    {
        var state = Ready();
        state.Set(4, 20);
        state.Heartbeat(At(3));
        Assert.Null(state.Read(4, At(3)));
        var command = Assert.Single(state.Pending(At(3)));
        Assert.Equal(4, command.Port);
        Assert.Equal(100, command.Duty);
    }

    [Fact]
    public void HostStallForcesFullSpeedEvenWithFreshUsbData()
    {
        var state = Ready();
        state.Set(0, 25);
        state.Accept(ProtocolTests.Status(), 64, At(5));
        Assert.Equal(100, Assert.Single(state.Pending(At(5))).Duty);
        state.Heartbeat(At(5));
        Assert.Equal(25, Assert.Single(state.Pending(At(5))).Duty);
    }

    [Fact]
    public void ResetDoesNothingToAnUnmanagedChannel()
    {
        var state = Ready();
        state.Reset(0);
        Assert.Empty(state.Pending(At(0)));
    }

    [Fact]
    public void ResetUsesFullSpeedUntilTheWriteSucceedsThenRelinquishesOwnership()
    {
        var state = Ready();
        state.Set(1, 30);
        state.Reset(1);
        var command = Assert.Single(state.Pending(At(0)));
        Assert.Equal(100, command.Duty);
        Assert.True(command.Release);
        Assert.Single(state.Pending(At(0))); // Failed write: keep the reset pending.
        state.Sent(command, At(0));
        Assert.Empty(state.Pending(At(0)));
    }

    [Fact]
    public void AConcurrentSetIsNotLostWhenAnOlderResetCompletes()
    {
        var state = Ready();
        state.Set(1, 30);
        state.Reset(1);
        var oldReset = Assert.Single(state.Pending(At(0)));
        state.Set(1, 60);
        state.Sent(oldReset, At(0));
        Assert.Equal(60, Assert.Single(state.Pending(At(0))).Duty);
    }

    [Fact]
    public void ShutdownTouchesOnlyClaimedPorts()
    {
        var state = Ready();
        state.Set(0, 0);
        state.Set(4, 60);
        var commands = state.Pending(At(0), stopping: true);
        Assert.Equal(new[] { 0, 4 }, commands.Select(c => c.Port));
        Assert.All(commands, c => Assert.Equal(100, c.Duty));
    }

    [Fact]
    public void UnchangedDutiesAreBoundedToOneWritePerSecondButChangesAreImmediate()
    {
        var state = Ready();
        state.Set(0, 40);
        state.Sent(Assert.Single(state.Pending(At(0))), At(0));
        Assert.Empty(state.Pending(At(.5)));
        state.Set(0, 50);
        Assert.Equal(50, Assert.Single(state.Pending(At(.5))).Duty);
        state.Set(0, 40);
        Assert.Single(state.Pending(At(1)));
    }

    [Fact]
    public void InvalidRequestsDoNotClaimTheChannel()
    {
        var state = Ready();
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Set(0, float.NaN));
        Assert.Empty(state.Pending(At(0)));
    }
}
