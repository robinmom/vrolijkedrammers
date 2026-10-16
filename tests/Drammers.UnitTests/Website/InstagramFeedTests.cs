using System.Net;
using System.Text;
using Drammers.Website.Content;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Drammers.UnitTests.Website;

/// <summary>Instagram op de homepage (2026-10-16): via de Facebookpagina het gekoppelde account, dan de laatste posts.</summary>
public class InstagramFeedTests
{
    private sealed class Graph(Func<string, (HttpStatusCode, string)> answer) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Urls.Add(url);
            var (status, body) = answer(url);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static InstagramFeed Feed(Graph graph, string? token = "token") =>
        new(new HttpClient(graph), Options.Create(new InstagramOptions { Token = token, PageId = "page-1", PostCount = 2 }),
            new ServiceCollection().BuildServiceProvider(), new MemoryCache(new MemoryCacheOptions()), NullLogger<InstagramFeed>.Instance);

    [Fact]
    public async Task Laatste_posts_via_het_gekoppelde_account_video_met_voorbeeldafbeelding()
    {
        var graph = new Graph(url => url.Contains("/page-1?", StringComparison.Ordinal)
            ? (HttpStatusCode.OK, """{"instagram_business_account":{"id":"ig-9"},"id":"page-1"}""")
            : (HttpStatusCode.OK, """
                {"data":[
                  {"caption":"Elfde van de elfde!","media_type":"IMAGE","media_url":"https://scontent.cdninstagram.com/a.jpg","permalink":"https://www.instagram.com/p/A/","timestamp":"2026-11-11T11:11:00+0000"},
                  {"caption":null,"media_type":"VIDEO","media_url":"https://x/v.mp4","thumbnail_url":"https://scontent.cdninstagram.com/t.jpg","permalink":"https://www.instagram.com/reel/B/","timestamp":"2026-11-10T20:00:00+0000"}
                ]}
                """));
        var feed = Feed(graph);

        var posts = await feed.LatestAsync(CancellationToken.None);
        Assert.Equal(2, posts.Count);
        Assert.Equal(("https://scontent.cdninstagram.com/a.jpg", "Elfde van de elfde!", false), (posts[0].ImageUrl, posts[0].Caption, posts[0].Video));
        Assert.Equal(new DateTime(2026, 11, 11, 11, 11, 0, DateTimeKind.Utc), posts[0].CreatedAt);
        Assert.Equal(("https://scontent.cdninstagram.com/t.jpg", true), (posts[1].ImageUrl, posts[1].Video));
        Assert.Contains(graph.Urls, u => u.Contains("ig-9/media?fields=caption,media_type,media_url,thumbnail_url,permalink,timestamp&limit=2", StringComparison.Ordinal));

        // Een half uur in het geheugen.
        await feed.LatestAsync(CancellationToken.None);
        Assert.Equal(2, graph.Urls.Count);
    }

    [Fact]
    public async Task Zonder_gekoppeld_account_of_bij_een_fout_een_lege_lijst()
    {
        Assert.Empty(await Feed(new Graph(_ => (HttpStatusCode.OK, """{"id":"page-1"}"""))).LatestAsync(CancellationToken.None));
        Assert.Empty(await Feed(new Graph(_ => (HttpStatusCode.BadRequest, """{"error":{"message":"Invalid token"}}"""))).LatestAsync(CancellationToken.None));
        Assert.Empty(await Feed(new Graph(_ => (HttpStatusCode.OK, "{}")), token: null).LatestAsync(CancellationToken.None));
    }
}
