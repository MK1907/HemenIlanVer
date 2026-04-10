namespace HemenIlanVer.Infrastructure.Options;

public sealed class CloudflareR2Options
{
    public const string SectionName = "CloudflareR2";

    public string AccountId { get; init; } = string.Empty;
    public string AccessKeyId { get; init; } = string.Empty;
    public string SecretAccessKey { get; init; } = string.Empty;
    public string BucketName { get; init; } = string.Empty;
    /// <summary>
    /// Yüklenen dosyaların public erişim URL'i (R2 public bucket veya custom domain).
    /// Örn: https://pub-xxx.r2.dev  veya  https://images.siteadiniz.com
    /// </summary>
    public string PublicBaseUrl { get; init; } = string.Empty;
}
