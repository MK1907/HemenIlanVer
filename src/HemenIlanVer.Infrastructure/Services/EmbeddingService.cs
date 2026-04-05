using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HemenIlanVer.Application.Abstractions;
using HemenIlanVer.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HemenIlanVer.Infrastructure.Services;

public sealed class EmbeddingService : IEmbeddingService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly OpenAiOptions _opts;
    private readonly ILogger<EmbeddingService> _log;
    private const string Model = "text-embedding-3-small";

    public EmbeddingService(IHttpClientFactory httpFactory, IOptions<OpenAiOptions> opts, ILogger<EmbeddingService> log)
    {
        _httpFactory = httpFactory;
        _opts = opts.Value;
        _log = log;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<float>();

        var client = _httpFactory.CreateClient("OpenAI");
        using var request = new HttpRequestMessage(HttpMethod.Post, "embeddings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiKey);

        var body = JsonSerializer.Serialize(new
        {
            model = Model,
            input = text
        });
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            _log.LogError("OpenAI Embeddings API error {Status}: {Body}", (int)response.StatusCode, err);
            throw new InvalidOperationException($"OpenAI Embeddings API returned {(int)response.StatusCode}");
        }

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var data = doc.RootElement.GetProperty("data")[0].GetProperty("embedding");
        var embedding = new float[data.GetArrayLength()];
        int i = 0;
        foreach (var el in data.EnumerateArray())
            embedding[i++] = el.GetSingle();

        return embedding;
    }
}
