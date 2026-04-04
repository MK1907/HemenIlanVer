namespace HemenIlanVer.Infrastructure.Services;

internal static class OpenAiGuard
{
    public static void RequireApiKey(string? apiKey, string featureDescription)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                $"OpenAI API anahtarı yapılandırılmadı (OpenAi:ApiKey). {featureDescription} yalnızca OpenAI ile çalışır.");
    }
}
