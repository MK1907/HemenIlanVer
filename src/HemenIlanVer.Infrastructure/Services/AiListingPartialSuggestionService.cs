using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HemenIlanVer.Application.Abstractions;
using HemenIlanVer.Contracts.Ai;
using HemenIlanVer.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace HemenIlanVer.Infrastructure.Services;

public sealed class AiListingPartialSuggestionService : IAiListingPartialSuggestionService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly OpenAiOptions _openAi;

    public AiListingPartialSuggestionService(IHttpClientFactory httpFactory, IOptions<OpenAiOptions> openAi)
    {
        _httpFactory = httpFactory;
        _openAi = openAi.Value;
    }

    public async Task<ListingPartialSuggestResponse> SuggestAsync(ListingPartialSuggestRequest request, CancellationToken cancellationToken = default)
    {
        var traceId = Guid.NewGuid();
        var text = request.PartialText?.Trim() ?? string.Empty;
        if (text.Length < 2)
            return new ListingPartialSuggestResponse(traceId, Array.Empty<string>());

        if (text.Length > 400)
            text = text[..400];

        OpenAiGuard.RequireApiKey(_openAi.ApiKey, "Yazarken ilan önerileri");

        var client = _httpFactory.CreateClient("OpenAI");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAi.ApiKey);

        const string system =
            "Sen Türkiye ilan siteleri için yardımcısın. Kullanıcı yeni ilan metnini yazarken kısa, yarım veya belirsiz bir parça gönderiyor.\n" +
            "Görev: Bu metne göre kullanıcının ne satmak veya kiralamak isteyebileceğine dair KISA Türkçe olasılıklar üret.\n" +
            "Örnekler: \"TOPTAN\" → toptan gıda, toptan tekstil, horeca malzemesi toptan; \"2012\" → 2012 model otomobil, 2012 yapım konut, ikinci el telefon (2012 civarı model).\n" +
            "Çeşitli ve somut ol; tekrar etme. SADECE geçerli JSON: {\"suggestions\":[\"...\",\"...\"]} — 4 ile 8 arası öğe, her biri kısa tek satır.";

        var body = new
        {
            model = _openAi.Model,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = text }
            }
        };

        using var resp = await client.PostAsJsonAsync("chat/completions", body, cancellationToken);
        var raw = await resp.Content.ReadAsStringAsync(cancellationToken);
        OpenAiErrorMapper.EnsureSuccess(resp, raw);

        var root = JsonDocument.Parse(raw).RootElement;
        var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
            ?? "{}";
        var doc = JsonDocument.Parse(content).RootElement;
        var list = new List<string>();
        if (doc.TryGetProperty("suggestions", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.String) continue;
                var s = el.GetString()?.Trim();
                if (string.IsNullOrEmpty(s)) continue;
                if (s.Length > 160) s = s[..160];
                list.Add(s);
                if (list.Count >= 8) break;
            }
        }

        return new ListingPartialSuggestResponse(traceId, list);
    }
}
