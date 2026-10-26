using System;
using System.Collections.Generic;

namespace FanControl.NzxtHub;

internal sealed class DutyCommand
{
    internal int Port { get; }
    internal float Duty { get; }
    internal long Revision { get; }
    internal bool Release { get; }

    internal DutyCommand(int port, float duty, long revision, bool release)
    {
        Port = port;
        Duty = duty;
        Revision = revision;
        Release = release;
    }
}

// Pure state shared by host callbacks and the USB worker. No USB calls hold this
// lock, so a slow/unplugged hub cannot block the host's fan-control update loop.
internal sealed class HubState
{
    internal static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan HostTimeout = TimeSpan.FromSeconds(5);
    private readonly object gate = new object();
    private readonly HubModel model;
    private readonly Channel[] channels;
    private FanReading[]? readings;
    private TimeSpan lastReading;
    private TimeSpan heartbeat;
    private bool authenticated;

    private sealed class Channel
    {
        internal bool Owned;
        internal bool Release;
        internal float Target;
        internal long Revision;
        internal float? Sent;
        internal TimeSpan LastSent;
    }

    internal HubState(HubModel model)
    {
        this.model = model;
        channels = new Channel[model.PortCount];
        for (var i = 0; i < channels.Length; i++) channels[i] = new Channel();
    }

    internal void Heartbeat(TimeSpan now)
    {
        lock (gate) heartbeat = now;
    }

    internal void Set(int port, float duty)
    {
        // Validate before changing any state.
        _ = Protocol.SetDuty(model, port, duty);
        lock (gate)
        {
            var channel = channels[port];
            channel.Target = duty;
            channel.Owned = true;
            channel.Release = false;
            channel.Revision++;
        }
    }

    internal void Reset(int port)
    {
        lock (gate)
        {
            var channel = channels[port];
            if (!channel.Owned) return;
            channel.Release = true;
            channel.Revision++;
        }
    }

    internal bool Accept(byte[] bytes, int length, TimeSpan now)
    {
        var parsed = Protocol.ParseStatus(model, bytes, length);
        if (parsed == null) return false;
        lock (gate)
        {
            readings = parsed;
            lastReading = now;
            authenticated = true;
        }
        return true;
    }

    internal FanReading? Read(int port, TimeSpan now)
    {
        lock (gate)
        {
            if (readings == null || now - lastReading >= StaleAfter || !readings[port].Connected)
                return null;
            return readings[port];
        }
    }

    internal void Disconnected()
    {
        lock (gate)
        {
            readings = null;
            authenticated = false;
            foreach (var channel in channels) channel.Sent = null;
        }
    }

    internal IReadOnlyList<DutyCommand> Pending(TimeSpan now, bool stopping = false)
    {
        lock (gate)
        {
            var result = new List<DutyCommand>();
            // A known PID alone is insufficient: require a valid status report
            // from this connection before sending a speed command.
            if (!authenticated) return result;
            var failsafe = stopping || now - heartbeat >= HostTimeout || now - lastReading >= StaleAfter;
            for (var port = 0; port < channels.Length; port++)
            {
                var channel = channels[port];
                if (!channel.Owned) continue;
                var duty = failsafe || channel.Release ? 100 : channel.Target;
                if (!failsafe && !channel.Release && !readings![port].Connected) continue;
                if (!stopping && !channel.Release && channel.Sent == duty && now - channel.LastSent < TimeSpan.FromSeconds(1))
                    continue;
                result.Add(new DutyCommand(port, duty, channel.Revision, channel.Release));
            }
            return result;
        }
    }

    internal void Sent(DutyCommand command, TimeSpan now)
    {
        lock (gate)
        {
            var channel = channels[command.Port];
            channel.Sent = command.Duty;
            channel.LastSent = now;
            // A Set that raced with a reset must not lose ownership.
            if (command.Release && command.Revision == channel.Revision)
            {
                channel.Owned = false;
                channel.Release = false;
            }
        }
    }
}
