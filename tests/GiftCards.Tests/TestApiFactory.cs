using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GiftCards.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace GiftCards.Tests;

/// <summary>Levanta la API real con una base SQLite temporal y una API key solo para tests.</summary>
public class TestApiFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "api-key-solo-para-tests-123456";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"giftcards-tests-{Guid.NewGuid():N}.db");

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = $"Data Source={_dbPath};Default Timeout=30",
            ["Security:ApiKey"] = ApiKey
        }));
    }

    public HttpClient CreatePosClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return client;
    }

    public async Task<string> IssueCardAsync(HttpClient client, decimal? activateWith = null)
    {
        var issued = await client.PostAsync("/api/giftcards", null);
        issued.EnsureSuccessStatusCode();
        var card = (await issued.Content.ReadFromJsonAsync<GiftCardResponse>(Json))!;

        if (activateWith is not null)
        {
            var activated = await Post(client, $"/api/giftcards/{card.Code}/activate",
                new ActivateRequest(activateWith.Value), Guid.NewGuid().ToString());
            activated.EnsureSuccessStatusCode();
        }

        return card.Code;
    }

    public static Task<HttpResponseMessage> Post<T>(HttpClient client, string url, T body, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }

    public static async Task<GiftCardResponse> GetCardAsync(HttpClient client, string code)
        => (await client.GetFromJsonAsync<GiftCardResponse>($"/api/giftcards/{code}", Json))!;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + suffix); } catch (IOException) { /* limpieza best-effort */ }
        }
    }
}
