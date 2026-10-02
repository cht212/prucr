using CRM.Data.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;

namespace CRM.Data.Tests;

public sealed class SocialOAuthServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "crm-oauth-tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("instagram", "www.instagram.com")]
    [InlineData("facebook", "www.facebook.com")]
    [InlineData("tiktok", "www.tiktok.com")]
    public void CreateStart_IncludesProtectedStateAndProviderUrl(string channel, string expectedHost)
    {
        var context = CreateContext(_ => throw new InvalidOperationException("No HTTP call expected."));

        var result = context.OAuth.CreateStart(Request(), channel, "42");

        Assert.True(result.Ready);
        var uri = new Uri(Assert.IsType<string>(result.AuthorizationUrl));
        Assert.Equal(expectedHost, uri.Host);
        Assert.Contains("state=", uri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("client_secret", uri.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExchangeAsync_RejectsTamperedStateBeforeCallingProvider()
    {
        var requests = 0;
        var context = CreateContext(_ =>
        {
            requests++;
            return Json(HttpStatusCode.OK, "{}");
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.OAuth.ExchangeAsync(Request(), "facebook", "42", "code", "state-alterado"));

        Assert.Contains("estado de seguridad", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task ExchangeTikTok_SavesTokensEncrypted()
    {
        var context = CreateContext(request =>
        {
            Assert.Equal("https://open.tiktokapis.com/v2/oauth/token/", request.RequestUri?.ToString());
            Assert.Equal(HttpMethod.Post, request.Method);
            return Json(HttpStatusCode.OK,
                """{"access_token":"access-secret","refresh_token":"refresh-secret","open_id":"open-7","expires_in":86400,"refresh_expires_in":31536000}""");
        });
        var start = context.OAuth.CreateStart(Request(), "tiktok", "42");
        var state = QueryValue(start.AuthorizationUrl!, "state");

        var result = await context.OAuth.ExchangeAsync(Request(), "tiktok", "42", "valid-code", state);

        Assert.True(result.Success);
        var config = context.Settings.GetConfiguration("tiktok", revealSecrets: true);
        Assert.Equal("access-secret", Field(config, "TikTok:DisplayAccessToken"));
        Assert.Equal("refresh-secret", Field(config, "TikTok:RefreshToken"));
        Assert.Equal("open-7", Field(config, "TikTok:OpenId"));
        var persisted = File.ReadAllText(Path.Combine(_root, "App_Data", "social-integrations.json"));
        Assert.DoesNotContain("access-secret", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-secret", persisted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExchangeInstagram_ExchangesShortTokenForLongToken()
    {
        var requestNumber = 0;
        var context = CreateContext(request =>
        {
            requestNumber++;
            if (requestNumber == 1)
            {
                Assert.Equal("api.instagram.com", request.RequestUri?.Host);
                Assert.Equal(HttpMethod.Post, request.Method);
                return Json(HttpStatusCode.OK, """{"access_token":"short-token","user_id":12345}""");
            }

            Assert.Equal("graph.instagram.com", request.RequestUri?.Host);
            Assert.Contains("ig_exchange_token", request.RequestUri?.Query);
            return Json(HttpStatusCode.OK, """{"access_token":"long-token","token_type":"bearer","expires_in":5184000}""");
        });
        var start = context.OAuth.CreateStart(Request(), "instagram", "42");

        var result = await context.OAuth.ExchangeAsync(
            Request(), "instagram", "42", "valid-code", QueryValue(start.AuthorizationUrl!, "state"));

        Assert.True(result.Success);
        Assert.Equal(2, requestNumber);
        var config = context.Settings.GetConfiguration("instagram", revealSecrets: true);
        Assert.Equal("12345", Field(config, "Meta:Instagram:LoginUserId"));
        Assert.Equal("long-token", Field(config, "Meta:Instagram:LoginAccessToken"));
    }

    [Fact]
    public async Task ExchangeFacebook_SelectsConfiguredPageToken()
    {
        var requestNumber = 0;
        var context = CreateContext(request =>
        {
            requestNumber++;
            if (requestNumber == 1)
            {
                Assert.Contains("oauth/access_token", request.RequestUri?.AbsolutePath);
                return Json(HttpStatusCode.OK, """{"access_token":"short-user-token"}""");
            }
            if (requestNumber == 2)
            {
                Assert.Contains("fb_exchange_token", request.RequestUri?.Query);
                return Json(HttpStatusCode.OK, """{"access_token":"long-user-token"}""");
            }

            Assert.Contains("/me/accounts", request.RequestUri?.AbsolutePath);
            return Json(HttpStatusCode.OK,
                """{"data":[{"id":"page-1","name":"Página","access_token":"page-token"}]}""");
        }, new Dictionary<string, string?> { ["Meta:Facebook:PageId"] = "page-1" });
        var start = context.OAuth.CreateStart(Request(), "facebook", "42");

        var result = await context.OAuth.ExchangeAsync(
            Request(), "facebook", "42", "valid-code", QueryValue(start.AuthorizationUrl!, "state"));

        Assert.True(result.Success);
        var config = context.Settings.GetConfiguration("facebook", revealSecrets: true);
        Assert.Equal("page-1", Field(config, "Meta:Facebook:PageId"));
        Assert.Equal("page-token", Field(config, "Meta:Facebook:AccessToken"));
    }

    [Fact]
    public async Task RefreshTikTok_RotatesAccessAndRefreshTokensNearExpiry()
    {
        var context = CreateContext(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://open.tiktokapis.com/v2/oauth/token/", request.RequestUri?.ToString());
            return Json(HttpStatusCode.OK,
                """{"access_token":"access-new","refresh_token":"refresh-new","open_id":"open-7","expires_in":86400,"refresh_expires_in":31536000}""");
        }, new Dictionary<string, string?>
        {
            ["TikTok:RefreshToken"] = "refresh-old",
            ["TikTok:AccessTokenExpiresAtUtc"] = DateTimeOffset.UtcNow.AddMinutes(2).ToString("O")
        });

        var refreshed = await context.OAuth.RefreshTikTokIfNeededAsync();

        Assert.True(refreshed);
        var config = context.Settings.GetConfiguration("tiktok", revealSecrets: true);
        Assert.Equal("access-new", Field(config, "TikTok:DisplayAccessToken"));
        Assert.Equal("refresh-new", Field(config, "TikTok:RefreshToken"));
    }

    private TestContext CreateContext(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        IReadOnlyDictionary<string, string?>? extra = null)
    {
        Directory.CreateDirectory(_root);
        var values = new Dictionary<string, string?>
        {
            ["Meta:AppId"] = "meta-app",
            ["Meta:AppSecret"] = "meta-secret",
            ["Meta:ApiVersion"] = "v26.0",
            ["TikTok:ClientKey"] = "tiktok-key",
            ["TikTok:ClientSecret"] = "tiktok-secret"
        };
        if (extra != null)
            foreach (var item in extra) values[item.Key] = item.Value;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var dataProtection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_root, "keys")));
        var settings = new SocialIntegrationService(
            configuration,
            new TestEnvironment(_root),
            dataProtection,
            NullLogger<SocialIntegrationService>.Instance);
        var oauth = new SocialOAuthService(
            new HttpClient(new DelegateHandler(responder)),
            settings,
            dataProtection,
            NullLogger<SocialOAuthService>.Instance);
        return new TestContext(settings, oauth);
    }

    private static HttpRequest Request()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("crm.example.com");
        return context.Request;
    }

    private static string QueryValue(string url, string key)
    {
        var query = new Uri(url).Query.TrimStart('?').Split('&');
        var item = query.Single(value => value.StartsWith($"{key}=", StringComparison.Ordinal));
        return Uri.UnescapeDataString(item[(key.Length + 1)..]);
    }

    private static string? Field(SocialChannelConfiguration config, string key) =>
        config.Fields.Single(field => field.Key == key).Value;

    private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed record TestContext(SocialIntegrationService Settings, SocialOAuthService OAuth);

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responder(request));
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
