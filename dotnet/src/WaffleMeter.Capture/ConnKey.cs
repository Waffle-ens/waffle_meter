namespace WaffleMeter.Capture;

/// <summary>
/// A directional IPv4 TCP connection key: src/dst IP as a network-order <see cref="uint"/> plus the
/// ports. Used by the P2P/streaming noise guard to tell the elevated capture helper which connections
/// to drop AT THE SOURCE. The numeric form matches the bytes the helper reads straight out of the
/// IPv4+TCP header, so the app and helper agree on a key without any string parsing on the capture hot
/// path. The app side builds it from a <see cref="CapturedSegment"/>'s dotted-quad strings (off the hot
/// path, only when a connection is excluded).
/// </summary>
public readonly record struct ConnKey(uint SrcIp, ushort SrcPort, uint DstIp, ushort DstPort)
{
    /// <summary>Build a key from a captured segment's 4-tuple. Returns false for non-IPv4 / malformed
    /// addresses (those connections simply aren't excluded — safe default).</summary>
    public static bool TryFrom(in CapturedSegment seg, out ConnKey key)
    {
        key = default;
        if (!TryParseIpv4(seg.SrcIp, out uint src) || !TryParseIpv4(seg.DstIp, out uint dst))
        {
            return false;
        }

        if (seg.SrcPort is < 0 or > 65535 || seg.DstPort is < 0 or > 65535)
        {
            return false;
        }

        key = new ConnKey(src, (ushort)seg.SrcPort, dst, (ushort)seg.DstPort);
        return true;
    }

    /// <summary>Dotted-quad "a.b.c.d" -> network-order uint (a&lt;&lt;24 | b&lt;&lt;16 | c&lt;&lt;8 | d),
    /// matching the on-wire IPv4 header byte order the helper reads.</summary>
    private static bool TryParseIpv4(string ip, out uint value)
    {
        value = 0;
        if (string.IsNullOrEmpty(ip))
        {
            return false;
        }

        uint acc = 0;
        int octet = 0;
        int dots = 0;
        bool digits = false;
        foreach (char c in ip)
        {
            if (c == '.')
            {
                if (!digits || ++dots > 3)
                {
                    return false;
                }

                acc = (acc << 8) | (uint)octet;
                octet = 0;
                digits = false;
            }
            else if (c is >= '0' and <= '9')
            {
                octet = (octet * 10) + (c - '0');
                if (octet > 255)
                {
                    return false;
                }

                digits = true;
            }
            else
            {
                return false; // not a dotted-quad IPv4 (e.g. an IPv6 literal) — caller treats as "not excludable"
            }
        }

        if (dots != 3 || !digits)
        {
            return false;
        }

        value = (acc << 8) | (uint)octet;
        return true;
    }
}
