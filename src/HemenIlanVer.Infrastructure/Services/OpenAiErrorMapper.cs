using System.Net;

namespace HemenIlanVer.Infrastructure.Services;

internal static class OpenAiErrorMapper
{
    public static void EnsureSuccess(HttpResponseMessage response, string responseBody)
    {
        if (response.IsSuccessStatusCode)
            return;

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new InvalidOperationException(
                "OpenAI API anahtarı kabul edilmedi (401). Proje kökündeki .env dosyasında OPENAI_API_KEY=sk-... doğru ve güncel olmalı. " +
                "Docker kullanıyorsanız anahtarı kaydettikten sonra şunu çalıştırın: docker compose up -d --force-recreate api"),
            HttpStatusCode.Forbidden => new InvalidOperationException(
                "OpenAI erişimi reddedildi (403)."),
            HttpStatusCode.PaymentRequired => new InvalidOperationException(
                "OpenAI hesabında ödeme / kota sorunu olabilir (402)."),
            HttpStatusCode.TooManyRequests => new InvalidOperationException(
                "OpenAI istek limiti aşıldı (429). Bir süre sonra tekrar deneyin."),
            _ => new InvalidOperationException(
                $"OpenAI yanıtı: {(int)response.StatusCode}. {Truncate(responseBody)}")
        };
    }

    private static string Truncate(string s, int max = 500) =>
        s.Length <= max ? s : s[..max] + "…";
}
