namespace WaffleMeter.Capture.Live;

/// <summary>
/// Passive round-trip-time estimator for one TCP connection. It never sends a probe: for each outbound
/// data segment it remembers the acknowledgment the peer will send (seq + payload length); when a matching
/// inbound ack arrives it measures the elapsed time and smooths it (EWMA). Pure and clock-injected so it is
/// unit-testable; the capture backend feeds it header fields it already parses. Wrap-safe on the 32-bit
/// sequence space.
/// </summary>
public sealed class PassiveRttEstimator
{
    private const int MaxPending = 64;
    private const double Alpha = 0.1; // EWMA weight on the newest sample

    private readonly struct Pending
    {
        public readonly long Ts;
        public readonly uint ExpectedAck;
        public Pending(long ts, uint expectedAck) { Ts = ts; ExpectedAck = expectedAck; }
    }

    private readonly Queue<Pending> _pending = new();
    private readonly long _ticksPerSecond;
    private readonly long _expiryTicks;
    private double _smoothedMs;

    /// <param name="ticksPerSecond">Clock frequency of the timestamps (e.g. Stopwatch.Frequency).</param>
    public PassiveRttEstimator(long ticksPerSecond)
    {
        _ticksPerSecond = ticksPerSecond <= 0 ? 1 : ticksPerSecond;
        // 2 s. A high-latency player (overseas / congested, real RTT 0.5–1.5 s) sends data and acts, but the
        // ack arrives after the old 500 ms window and every pending sample was evicted before it could match —
        // so ping stayed '--ms' forever no matter how much they did. Widened so those samples still resolve
        // (the 10 s display-staleness gate still hides a genuinely dead measurement).
        _expiryTicks = _ticksPerSecond * 2;
    }

    /// <summary>The latest smoothed RTT (ms), or 0 if none yet.</summary>
    public double SmoothedMs => _smoothedMs;

    /// <summary>Record an outbound data segment. Only data segments (payload &gt; 0) elicit an ack.</summary>
    public void TrackOutbound(uint seq, int payloadLength, long ts)
    {
        if (payloadLength <= 0)
        {
            return;
        }

        EvictExpired(ts);
        if (_pending.Count >= MaxPending)
        {
            _pending.Dequeue();
        }

        _pending.Enqueue(new Pending(ts, seq + (uint)payloadLength));
    }

    /// <summary>Try to match an inbound ack to a pending outbound segment; on a match, update the smoothed
    /// RTT and return true.</summary>
    public bool TryResolveInbound(uint ackNumber, long ts, out double smoothedMs)
    {
        EvictExpired(ts);
        while (_pending.Count > 0)
        {
            Pending head = _pending.Peek();
            if (ackNumber == head.ExpectedAck)
            {
                _pending.Dequeue();
                Commit(head.Ts, ts);
                smoothedMs = _smoothedMs;
                return true;
            }

            // The ack is past this pending segment (its ack was lost/coalesced) — drop it and keep looking.
            if (SequenceLessThan(head.ExpectedAck, ackNumber))
            {
                _pending.Dequeue();
                continue;
            }

            break; // the ack is for a segment we haven't tracked yet
        }

        smoothedMs = _smoothedMs;
        return false;
    }

    private void Commit(long sentTs, long recvTs)
    {
        double rawMs = Math.Max(0, (recvTs - sentTs) * 1000.0 / _ticksPerSecond);
        _smoothedMs = _smoothedMs <= 0 ? rawMs : _smoothedMs * (1 - Alpha) + rawMs * Alpha;
    }

    private void EvictExpired(long now)
    {
        while (_pending.Count > 0 && now - _pending.Peek().Ts > _expiryTicks)
        {
            _pending.Dequeue();
        }
    }

    // Wrap-safe "a is before b" on the 32-bit sequence space (RFC 1982 style).
    private static bool SequenceLessThan(uint a, uint b) => (int)(a - b) < 0;
}
