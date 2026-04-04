using System.Globalization;
using System.Text;

namespace HemenIlanVer.Infrastructure.Services;

internal static class CategorySlugHelper
{
    public static string SanitizeSlug(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "kategori";
        var s = input.Trim().ToLower(new CultureInfo("tr-TR"));
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
}
