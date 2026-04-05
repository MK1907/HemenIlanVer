using System.Globalization;
using System.Text;

namespace HemenIlanVer.Infrastructure.Services;

internal static class CategorySlugHelper
{
    public static string SanitizeSlug(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "kategori";
        var s = NormalizeToAscii(input.Trim());
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
            else if (c is ' ' or '-' or '_' or '.')
                sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        if (string.IsNullOrEmpty(slug)) return "kategori";
        return slug.Length > 120 ? slug[..120] : slug;
    }

    public static string SanitizeAttributeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "alan";
        var s = new string(key.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
        return string.IsNullOrEmpty(s) ? "alan" : s.ToLowerInvariant();
    }

    public static string NormalizeToAscii(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var s = input.ToLowerInvariant()
            .Replace("ç", "c").Replace("ğ", "g").Replace("ı", "i")
            .Replace("ö", "o").Replace("ş", "s").Replace("ü", "u")
            .Replace("Ç", "c").Replace("Ğ", "g").Replace("İ", "i")
            .Replace("Ö", "o").Replace("Ş", "s").Replace("Ü", "u");
        return s;
    }

    public static bool SlugEquals(string? a, string? b)
    {
        if (a == b) return true;
        if (a is null || b is null) return false;
        return NormalizeToAscii(a) == NormalizeToAscii(b);
    }
}
