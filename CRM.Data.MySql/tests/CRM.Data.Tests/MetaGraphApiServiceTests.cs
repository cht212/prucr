using CRM.Data.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;

namespace CRM.Data.Tests;

public sealed class MetaGraphApiServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "crm-meta-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FacebookFeed_UsesCurrentReactionSummaryInsteadOfHistoricalMaximum()
    {
        var service = CreateService(request =>
        {
            var uri = Assert.IsType<Uri>(request.RequestUri);
            if (uri.AbsolutePath.EndsWith("/me", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, """{"id":"page-1","name":"Página"}""");

            if (uri.AbsolutePath.EndsWith("/published_posts", StringComparison.Ordinal))
            {
                var fields = Uri.UnescapeDataString(uri.Query);
                Assert.Contains("reactions.limit(0).summary(total_count)", fields, StringComparison.Ordinal);
                return Json(HttpStatusCode.OK, """
                    {"data":[{
                      "id":"page-1_post-1","message":"Publicación","created_time":"2026-10-01T12:00:00+0000",
                      "reactions":{"data":[],"summary":{"total_count":2}},
                      "reaction_like":{"data":[],"summary":{"total_count":1}},
                      "reaction_love":{"data":[],"summary":{"total_count":1}},
                      "comments":{"data":[],"summary":{"total_count":0}}
                    }]}
                    """);
            }

            Assert.EndsWith("/page-1_post-1/insights", uri.AbsolutePath, StringComparison.Ordinal);
            return Json(HttpStatusCode.OK, """
                {"data":[{"name":"post_reactions_by_type_total","values":[{"value":{"like":3}}]}]}
                """);
        }, new Dictionary<string, string?>
        {
            ["Meta:Facebook:PageId"] = "page-1",
            ["Meta:Facebook:AccessToken"] = "page-token"
        });

        var result = await service.ObtenerFacebookFeedAsync(
            new DateTime(2026, 9, 1),
            new DateTime(2026, 10, 2),
            25);

        var post = Assert.Single(result.Posts);
        Assert.Equal(2, post.Likes);
        Assert.Equal(1, post.ReactionsByType["like"]);
        Assert.Equal(1, post.ReactionsByType["love"]);
    }

    [Fact]
    public async Task FacebookFeed_KeepsPostWhenInsightsRequestTimesOut()
    {
        var service = CreateService(request =>
        {
            var uri = Assert.IsType<Uri>(request.RequestUri);
            if (uri.AbsolutePath.EndsWith("/me", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, """{"id":"page-1","name":"Página"}""");

            if (uri.AbsolutePath.EndsWith("/published_posts", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """
                    {"data":[{
                      "id":"page-1_post-1","message":"Publicación","created_time":"2026-10-01T12:00:00+0000",
                      "reactions":{"data":[],"summary":{"total_count":2}},
                      "comments":{"data":[],"summary":{"total_count":1}}
                    }]}
                    """);
            }

            Assert.EndsWith("/page-1_post-1/insights", uri.AbsolutePath, StringComparison.Ordinal);
            throw new OperationCanceledException("The request timed out.");
        }, new Dictionary<string, string?>
        {
            ["Meta:Facebook:PageId"] = "page-1",
            ["Meta:Facebook:AccessToken"] = "page-token"
        });

        var result = await service.ObtenerFacebookFeedAsync(
            new DateTime(2026, 9, 1),
            new DateTime(2026, 10, 2),
            25);

        var post = Assert.Single(result.Posts);
        Assert.Equal("page-1_post-1", post.Id);
        Assert.Equal(2, post.Likes);
        Assert.Equal(1, post.Comments);
    }

    [Fact]
    public async Task InstagramComments_FallsBackToFacebookLoginConnection()
    {
        var hosts = new List<string>();
        var service = CreateService(request =>
        {
            var uri = Assert.IsType<Uri>(request.RequestUri);
            hosts.Add(uri.Host);
            if (uri.Host == "graph.instagram.com")
                return Json(HttpStatusCode.Forbidden,
                    """{"error":{"message":"Missing instagram_business_manage_comments"}}""");

            return Json(HttpStatusCode.OK, """
                {"data":[{"id":"comment-1","text":"Consulta desde Instagram","timestamp":"2026-10-02T12:00:00+0000","username":"cliente","from":{"id":"user-1","username":"cliente"}}]}
                """);
        }, new Dictionary<string, string?>
        {
            ["Meta:Instagram:LoginAccessToken"] = "instagram-login-token",
            ["Meta:Instagram:AccessToken"] = "facebook-login-token"
        });

        var result = await service.ObtenerComentariosPublicacionAsync("INSTAGRAM", "media-1");

        Assert.True(result.Success);
        Assert.Equal(new[] { "graph.instagram.com", "graph.facebook.com" }, hosts);
        var comment = Assert.Single(result.Comments);
        Assert.Equal("Consulta desde Instagram", comment.Text);
        Assert.Equal("media-1", comment.PublicationId);
    }

    [Fact]
    public async Task DashboardStartsBothNetworksBeforeEitherFinishes()
    {
        var started = 0;
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateService(new AsyncHandler(async (request, ct) =>
        {
            if (Interlocked.Increment(ref started) >= 2) bothStarted.TrySetResult();
            await bothStarted.Task.WaitAsync(ct);
            return Json(HttpStatusCode.OK, request.RequestUri!.AbsolutePath.EndsWith("/me")
                ? """{"id":"page-1"}""" : """{"data":[]}""");
        }), NetworkSettings());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await service.ObtenerDashboardAsync(DateTime.Today.AddDays(-7), DateTime.Today, timeout.Token);
        Assert.True(bothStarted.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DashboardPropagatesCancellationToNetworkRequests()
    {
        var cancelled = 0;
        var service = CreateService(new AsyncHandler(async (_, ct) =>
        {
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { Interlocked.Increment(ref cancelled); throw; }
            return Json(HttpStatusCode.OK, "{}");
        }), NetworkSettings());
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ObtenerDashboardAsync(DateTime.Today.AddDays(-7), DateTime.Today, timeout.Token));
        Assert.True(cancelled >= 2);
    }

    private static Dictionary<string, string?> NetworkSettings() => new()
    {
        ["Meta:Facebook:PageId"] = "page-1", ["Meta:Facebook:AccessToken"] = "test-token",
        ["Meta:Instagram:InstagramBusinessAccountId"] = "ig-1", ["Meta:Instagram:AccessToken"] = "test-token"
    };

    private MetaGraphApiService CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        IReadOnlyDictionary<string, string?> values) => CreateService(new DelegateHandler(responder), values);

    private MetaGraphApiService CreateService(HttpMessageHandler handler, IReadOnlyDictionary<string, string?> values)
    {
        Directory.CreateDirectory(_root);
        var configurationValues = new Dictionary<string, string?>(values)
        {
            ["Meta:ApiVersion"] = "v26.0"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configurationValues).Build();
        var dataProtection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_root, "keys")));
        var settings = new SocialIntegrationService(
            configuration,
            new TestEnvironment(_root),
            dataProtection,
            NullLogger<SocialIntegrationService>.Instance);
        return new MetaGraphApiService(
            new HttpClient(handler),
            settings,
            configuration,
            NullLogger<MetaGraphApiService>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }

    private sealed class AsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }

    private sealed class TestEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "CRM.Data.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(contentRoot, "wwwroot");
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRoot);
    }
}
