using System.Net;
using Drammers.Api.Authorization;

namespace Drammers.UnitTests.Identity;

/// <summary>Rate limit per IP: IPv6 per /64 (één aansluiting), IPv4 per adres.</summary>
public class RateLimitPartitionTests
{
    [Fact]
    public void IPv6_adressen_in_hetzelfde_64_prefix_delen_een_limiet()
    {
        var a = AuthorizationSetup.ClientKey(IPAddress.Parse("2001:db8:1234:5678:aaaa:bbbb:cccc:dddd"));
        var b = AuthorizationSetup.ClientKey(IPAddress.Parse("2001:db8:1234:5678::1"));
        var other = AuthorizationSetup.ClientKey(IPAddress.Parse("2001:db8:1234:5679::1"));
        Assert.Equal("2001:db8:1234:5678::/64", a);
        Assert.Equal(a, b);
        Assert.NotEqual(a, other);
    }

    [Fact]
    public void IPv4_ook_als_IPv6_geschreven_per_volledig_adres()
    {
        Assert.Equal("203.0.113.7", AuthorizationSetup.ClientKey(IPAddress.Parse("203.0.113.7")));
        Assert.Equal("203.0.113.7", AuthorizationSetup.ClientKey(IPAddress.Parse("::ffff:203.0.113.7")));
        Assert.NotEqual(AuthorizationSetup.ClientKey(IPAddress.Parse("203.0.113.7")), AuthorizationSetup.ClientKey(IPAddress.Parse("203.0.113.8")));
        Assert.Equal("onbekend", AuthorizationSetup.ClientKey(null));
    }
}
