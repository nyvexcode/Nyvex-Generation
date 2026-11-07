using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using NyvexGeneration.Core;
using NyvexGeneration.Models;
using NyvexGeneration.Services;
using Xunit;

namespace NyvexGeneration.Tests;

public class RobloxApiServiceTests
{
    private const string FoundJson =
        """{"data":[{"requestedUsername":"abcd","hasVerifiedBadge":false,"id":123,"name":"abcd","displayName":"Abcd"}]}""";

    private static (RobloxApiService Service, FakeHandler Handler, List<TimeSpan> Delays) Create(
        Func<HttpRequestMessage, string?, CancellationToken, Task<HttpResponseMessage>> responder,
        AppSettings? settings = null)
    {
        var handler = new FakeHandler(responder);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://users.roblox.com/") };
        var state = new SettingsState();
        state.Update(settings ?? new AppSettings { BatchSize = 50, DelayBetweenRequestsMs = 500, MaxRetriesOnRateLimit = 2 });
        var delays = new List<TimeSpan>();

        var service = new RobloxApiService(
            http, new TestLog(), state,
            (t, _) =>
            {
                delays.Add(t);
                return Task.CompletedTask;
            },
            () => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        return (service, handler, delays);
    }

    private static Task<HttpResponseMessage> Respond(HttpStatusCode code, string json) =>
        Task.FromResult(FakeHandler.Json(code, json));

    // ---------- Desserialização ----------

    [Fact]
    public async Task ParsesFoundAndNotFoundFromJson()
    {
        var (service, _, _) = Create((_, _, _) => Respond(HttpStatusCode.OK, FoundJson));

        var results = await service.CheckBatchAsync(new[] { "abcd", "zzzz" }, CancellationToken.None);

        var found = results.Single(r => r.Username == "abcd");
        Assert.Equal(LookupStatus.AccountFound, found.Status);
        Assert.Equal(123, found.RobloxUserId);
        Assert.Equal("abcd", found.MatchedName);

        var missing = results.Single(r => r.Username == "zzzz");
        Assert.Equal(LookupStatus.NoPublicMatch, missing.Status);
        Assert.Contains("NÃO confirmada", missing.Details);
        Assert.False(missing.RegistrationAvailabilityConfirmed);
    }

    [Fact]
    public async Task MatchingIsCaseInsensitive()
    {
        var (service, _, _) = Create((_, _, _) => Respond(HttpStatusCode.OK,
            """{"data":[{"requestedUsername":"ABCD","hasVerifiedBadge":false,"id":5,"name":"AbCd","displayName":"x"}]}"""));

        var result = await service.CheckUsernameAsync("abcd", CancellationToken.None);

        Assert.Equal(LookupStatus.AccountFound, result.Status);
    }

    [Fact]
    public async Task EmptyDataArrayMeansNoPublicMatchNeverAvailable()
    {
        var (service, _, _) = Create((_, _, _) => Respond(HttpStatusCode.OK, """{"data":[]}"""));

        var result = await service.CheckUsernameAsync("abcd", CancellationToken.None);

        Assert.Equal(LookupStatus.NoPublicMatch, result.Status);
        Assert.False(result.RegistrationAvailabilityConfirmed);
    }

    [Fact]
    public void DtoRoundTripsUsingDocumentedFieldNames()
    {
        var json = JsonSerializer.Serialize(new UsernameLookupRequestDto { Usernames = new[] { "a", "b" }, ExcludeBannedUsers = true });

        Assert.Contains("\"usernames\":[\"a\",\"b\"]", json);
        Assert.Contains("\"excludeBannedUsers\":true", json);

        var response = JsonSerializer.Deserialize<UsernameLookupResponseDto>(FoundJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(123, response!.Data![0]!.Id);
        Assert.Equal("Abcd", response.Data[0]!.DisplayName);
    }

    // ---------- Requisição ----------

    [Fact]
    public async Task SendsDocumentedBodyWithoutCredentials()
    {
        var (service, handler, _) = Create((_, _, _) => Respond(HttpStatusCode.OK, """{"data":[]}"""));

        await service.CheckBatchAsync(new[] { "abcd", "efgh" }, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://users.roblox.com/v1/usernames/users", request.RequestUri!.ToString());
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.Null(request.Headers.Authorization);

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal(2, doc.RootElement.GetProperty("usernames").GetArrayLength());
        Assert.False(doc.RootElement.GetProperty("excludeBannedUsers").GetBoolean());
    }

    [Fact]
    public async Task RemovesDuplicatesAndBlanksBeforeSending()
    {
        var (service, handler, _) = Create((_, _, _) => Respond(HttpStatusCode.OK, """{"data":[]}"""));

        var results = await service.CheckBatchAsync(new[] { "abcd", "ABCD", " ", "", "abcd " }, CancellationToken.None);

        Assert.Single(results);
        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal(1, doc.RootElement.GetProperty("usernames").GetArrayLength());
    }

    [Fact]
    public async Task SplitsLargeBatchesAndWaitsBetweenRequests()
    {
        var settings = new AppSettings { BatchSize = 2, DelayBetweenRequestsMs = 700, MaxRetriesOnRateLimit = 0 };
        var (service, handler, delays) = Create((_, _, _) => Respond(HttpStatusCode.OK, """{"data":[]}"""), settings);

        var results = await service.CheckBatchAsync(new[] { "aaa", "bbb", "ccc", "ddd", "eee" }, CancellationToken.None);

        Assert.Equal(5, results.Count);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(2, delays.Count);
        Assert.All(delays, d => Assert.Equal(TimeSpan.FromMilliseconds(700), d));
        Assert.Equal(new[] { "aaa", "bbb", "ccc", "ddd", "eee" }, results.Select(r => r.Username));
    }

    // ---------- Erros HTTP ----------

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task HttpErrorsBecomeInconclusive(int code)
    {
        var (service, _, _) = Create((_, _, _) => Respond((HttpStatusCode)code, "{}"));

        var result = await service.CheckUsernameAsync("abcd", CancellationToken.None);

        Assert.Equal(LookupStatus.Inconclusive, result.Status);
        Assert.Equal(LookupFailureReason.HttpError, result.FailureReason);
        Assert.Equal(code, result.HttpStatusCode);
        Assert.False(result.RegistrationAvailabilityConfirmed);
    }

    [Fact]
    public async Task NetworkFailureBecomesConnectionError()
    {
        var (service, _, _) = Create((_, _, _) => throw new HttpRequestException("sem rede"));

        var result = await service.CheckUsernameAsync("abcd", CancellationToken.None);

        Assert.Equal(LookupStatus.ConnectionError, result.Status);
        Assert.Equal(LookupFailureReason.Network, result.FailureReason);
    }

    [Fact]
    public async Task InvalidJsonBecomesInvalidResponse()
    {
        var (service, _, _) = Create((_, _, _) => Respond(HttpStatusCode.OK, "isto não é json"));

        var result = await service.CheckUsernameAsync("abcd", CancellationToken.None);

        Assert.Equal(LookupStatus.Inconclusive, result.Status);
        Assert.Equal(LookupFailureReason.InvalidResponse, result.FailureReason);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"data":null}""")]
    [InlineData("""{"data":[{"requestedUsername":"abcd","id":0,"name":"abcd"}]}""")]
    [InlineData("""{"data":[null]}""")]
    public async Task StructurallyInvalidResponsesAreNeverTrusted(string json)
    {
        var (service, _, _) = Create((_, _, _) => Respond(HttpStatusCode.OK, json));

        var result = await service.CheckUsernameAsync("abcd", CancellationToken.None);

        Assert.Equal(LookupStatus.Inconclusive, result.Status);
        Assert.Equal(LookupFailureReason.InvalidResponse, result.FailureReason);
    }

    [Fact]
    public async Task InternalTimeoutBecomesInconclusiveTimeout()
    {
        var (service, _, _) = Create((_, _, _) => throw new TaskCanceledException("timeout"));

        var result = await service.CheckUsernameAsync("abcd", CancellationToken.None);

        Assert.Equal(LookupStatus.Inconclusive, result.Status);
        Assert.Equal(LookupFailureReason.Timeout, result.FailureReason);
    }

    // ---------- Rate limit (429) ----------

    [Fact]
    public async Task RetriesAfter429HonoringRetryAfterThenSucceeds()
    {
        var calls = 0;
        var (service, handler, delays) = Create((_, _, _) =>
        {
            calls++;
            if (calls == 1)
            {
                var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                limited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
                return Task.FromResult(limited);
            }

            return Respond(HttpStatusCode.OK, FoundJson);
        });

        var result = await service.CheckUsernameAsync("abcd", CancellationToken.None);

        Assert.Equal(LookupStatus.AccountFound, result.Status);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(TimeSpan.FromSeconds(7), Assert.Single(delays));
    }

    [Fact]
    public async Task Persistent429BecomesInconclusiveRateLimited()
    {
        var (service, handler, delays) = Create((_, _, _) => Respond((HttpStatusCode)429, "{}"));

        var result = await service.CheckUsernameAsync("abcd", CancellationToken.None);

        Assert.Equal(LookupStatus.Inconclusive, result.Status);
        Assert.Equal(LookupFailureReason.RateLimited, result.FailureReason);
        Assert.Equal(429, result.HttpStatusCode);
        Assert.Equal(3, handler.Requests.Count); // 1 inicial + 2 tentativas
        Assert.Equal(2, delays.Count);
    }

    [Fact]
    public async Task RetryWaitFallsBackToExponentialAndIsCapped()
    {
        var settings = new AppSettings { MaxRetriesOnRateLimit = 3 };
        var (service, _, delays) = Create((_, _, _) => Respond((HttpStatusCode)429, "{}"), settings);

        await service.CheckUsernameAsync("abcd", CancellationToken.None);

        Assert.Equal(new[] { 1.0, 2.0, 4.0 }, delays.Select(d => d.TotalSeconds));

        var huge = Create((_, _, _) =>
        {
            var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            limited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(2));
            return Task.FromResult(limited);
        }, new AppSettings { MaxRetriesOnRateLimit = 1 });

        await huge.Service.CheckUsernameAsync("abcd", CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(60), Assert.Single(huge.Delays));
    }

    [Fact]
    public async Task ZeroRetriesReturnsRateLimitedImmediately()
    {
        var (service, handler, delays) = Create(
            (_, _, _) => Respond((HttpStatusCode)429, "{}"),
            new AppSettings { MaxRetriesOnRateLimit = 0 });

        var result = await service.CheckUsernameAsync("abcd", CancellationToken.None);

        Assert.Equal(LookupFailureReason.RateLimited, result.FailureReason);
        Assert.Single(handler.Requests);
        Assert.Empty(delays);
    }

    // ---------- Cancelamento ----------

    [Fact]
    public async Task AlreadyCancelledTokenThrowsAndSendsNothing()
    {
        var (service, handler, _) = Create((_, _, _) => Respond(HttpStatusCode.OK, "{}"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CheckBatchAsync(new[] { "abcd" }, cts.Token));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CancellingDuringRequestThrowsInsteadOfReportingTimeout()
    {
        using var cts = new CancellationTokenSource();
        var (service, _, _) = Create(async (_, _, ct) =>
        {
            cts.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CheckUsernameAsync("abcd", cts.Token));
    }

    [Fact]
    public async Task CancellingBetweenChunksStopsFurtherRequests()
    {
        using var cts = new CancellationTokenSource();
        var settings = new AppSettings { BatchSize = 1, DelayBetweenRequestsMs = 500, MaxRetriesOnRateLimit = 0 };
        var (service, handler, _) = Create((_, _, _) =>
        {
            cts.Cancel(); // cancela após a primeira resposta
            return Respond(HttpStatusCode.OK, """{"data":[]}""");
        }, settings);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CheckBatchAsync(new[] { "aaa", "bbb", "ccc" }, cts.Token));

        Assert.Single(handler.Requests);
    }
}
