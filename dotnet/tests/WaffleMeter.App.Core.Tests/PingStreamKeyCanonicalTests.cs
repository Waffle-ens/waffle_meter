using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Spec for the direction-independent game-stream match used to accept a passive ping. The RTT can resolve
/// on either direction of the connection (always inbound on a normal link, but the loopback/booster path can
/// resolve on the client→server direction), so the match must treat the two directions of one connection as
/// equal — while still rejecting a different connection.
/// </summary>
public sealed class PingStreamKeyCanonicalTests
{
    [Fact]
    public void The_two_directions_of_one_connection_canonicalize_equal()
    {
        string inbound = "1.2.3.4:7022-10.0.0.2:53114";  // server → client (the elected game stream)
        string outbound = "10.0.0.2:53114-1.2.3.4:7022";  // client → server (loopback path resolves here)

        Assert.Equal(MeterServices.Canonicalize(inbound), MeterServices.Canonicalize(outbound));
    }

    [Fact]
    public void A_different_connection_does_not_match()
    {
        string gameStream = "1.2.3.4:7022-10.0.0.2:53114";
        string other = "1.2.3.4:7022-10.0.0.2:53115"; // same server, different local port = a different connection

        Assert.NotEqual(MeterServices.Canonicalize(gameStream), MeterServices.Canonicalize(other));
    }
}
