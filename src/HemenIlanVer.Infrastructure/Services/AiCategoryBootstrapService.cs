using System.Text.Json;
using HemenIlanVer.Application.Abstractions;
using HemenIlanVer.Domain.Entities;
using HemenIlanVer.Domain.Enums;
using HemenIlanVer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HemenIlanVer.Infrastructure.Services;

public sealed class AiCategoryBootstrapService : IAiCategoryBootstrapService
{
    private readonly AppDbContext _db;

    public AiCategoryBootstrapService(AppDbContext db) => _db = db;

    public async Task ApplyFromDetectDocumentAsync(JsonElement doc, CancellationToken cancellationToken = default)
    {
        if (!doc.TryGetProperty("bootstrap", out var b) || b.ValueKind != JsonValueKind.Object)
            return;
        if (!IsTruthy(b, "needed"))
            return;

        var rootName = GetString(b, "rootName") ?? "Yeni ana kategori";
        var rootSlug = CategorySlugHelper.SanitizeSlug(GetString(b, "rootSlug") ?? rootName);
        var childName = GetString(b, "childName") ?? "Genel";
        var childSlug = CategorySlugHelper.SanitizeSlug(GetString(b, "childSlug") ?? childName);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var allRoots = await _db.Categories.Where(x => x.ParentId == null).ToListAsync(cancellationToken);
        var root = allRoots.FirstOrDefault(x => x.Slug == rootSlug)
            ?? allRoots.FirstOrDefault(x => CategorySlugHelper.SlugEquals(x.Slug, rootSlug));
        if (root is null)
        {
            var maxSort = await _db.Categories.Where(x => x.ParentId == null).MaxAsync(x => (int?)x.SortOrder, cancellationToken) ?? 0;
            root = new Category
            {
                Id = Guid.NewGuid(),
                Name = rootName,
                Slug = rootSlug,
                ParentId = null,
                SortOrder = maxSort + 1,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.Categories.Add(root);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var allChildren = await _db.Categories.Where(x => x.ParentId == root.Id).ToListAsync(cancellationToken);
        var child = allChildren.FirstOrDefault(x => x.Slug == childSlug)
            ?? allChildren.FirstOrDefault(x => CategorySlugHelper.SlugEquals(x.Slug, childSlug));
        if (child is null)
        {
            var maxChild = await _db.Categories.Where(x => x.ParentId == root.Id).MaxAsync(x => (int?)x.SortOrder, cancellationToken) ?? 0;
            child = new Category
            {
                Id = Guid.NewGuid(),
                Name = childName,
                Slug = childSlug,
                ParentId = root.Id,
                SortOrder = maxChild + 1,
                IsActive = true,
                DefaultListingType = ListingType.Satilik,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.Categories.Add(child);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var existingAttrCount = await _db.CategoryAttributes
            .Where(a => a.CategoryId == child.Id)
            .CountAsync(cancellationToken);

        var onlyFallback = existingAttrCount == 1 && await _db.CategoryAttributes
            .AnyAsync(a => a.CategoryId == child.Id && a.AttributeKey == "aciklama", cancellationToken);

        if (existingAttrCount > 0 && !onlyFallback)
        {
            await tx.CommitAsync(cancellationToken);
            return;
        }

        if (onlyFallback)
        {
            await _db.CategoryAttributeOptions
                .Where(o => _db.CategoryAttributes
                    .Where(a => a.CategoryId == child.Id && a.AttributeKey == "aciklama")
                    .Select(a => a.Id)
                    .Contains(o.CategoryAttributeId))
                .ExecuteDeleteAsync(cancellationToken);
            await _db.CategoryAttributes
                .Where(a => a.CategoryId == child.Id && a.AttributeKey == "aciklama")
                .ExecuteDeleteAsync(cancellationToken);
        }

        var filterCount = 0;
        var keyToAttrId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        if (b.TryGetProperty("filters", out var filtersArr) && filtersArr.ValueKind == JsonValueKind.Array)
        {
            var sortOrder = 0;
            foreach (var el in filtersArr.EnumerateArray())
            {
                sortOrder++;
                var keyRaw = GetString(el, "key") ?? GetString(el, "attributeKey") ?? GetString(el, "name");
                if (string.IsNullOrWhiteSpace(keyRaw)) continue;
                var key = CategorySlugHelper.SanitizeAttributeKey(keyRaw);
                var display = GetString(el, "displayName") ?? GetString(el, "display_name") ?? GetString(el, "label") ?? key;
                var dtStr = GetString(el, "dataType") ?? GetString(el, "data_type") ?? GetString(el, "type") ?? "String";
                var req = IsTruthy(el, "required") || IsTruthy(el, "isRequired");
                if (!Enum.TryParse<AttributeDataType>(dtStr, true, out var dt))
                    dt = AttributeDataType.String;

                // Enum tipini koruyoruz; başlangıçta seçeneği olmasa bile
                // CategoryEnrichmentWorker arka planda doldurur.

                var parentKeyRaw = GetString(el, "parentKey") ?? GetString(el, "parent_key");
                Guid? parentAttrId = null;
                if (!string.IsNullOrWhiteSpace(parentKeyRaw))
                {
                    var parentKeySanitized = CategorySlugHelper.SanitizeAttributeKey(parentKeyRaw);
                    keyToAttrId.TryGetValue(parentKeySanitized, out var pid);
                    if (pid != Guid.Empty) parentAttrId = pid;
                }

                var attrId = Guid.NewGuid();
                keyToAttrId[key] = attrId;

                _db.CategoryAttributes.Add(new CategoryAttribute
                {
                    Id = attrId,
                    CategoryId = child.Id,
                    AttributeKey = key,
                    DisplayName = display,
                    DataType = dt,
                    IsRequired = req,
                    SortOrder = sortOrder,
                    ParentAttributeId = parentAttrId,
                    CreatedAt = DateTimeOffset.UtcNow
                });

                filterCount++;
            }

            await _db.SaveChangesAsync(cancellationToken);

            sortOrder = 0;
            foreach (var el in filtersArr.EnumerateArray())
            {
                sortOrder++;
                var keyRaw = GetString(el, "key") ?? GetString(el, "attributeKey") ?? GetString(el, "name");
                if (string.IsNullOrWhiteSpace(keyRaw)) continue;
                var key = CategorySlugHelper.SanitizeAttributeKey(keyRaw);
                var dtStr = GetString(el, "dataType") ?? GetString(el, "data_type") ?? GetString(el, "type") ?? "String";
                if (!Enum.TryParse<AttributeDataType>(dtStr, true, out var dt))
                    dt = AttributeDataType.String;

                if (!keyToAttrId.TryGetValue(key, out var attrId)) continue;
                if (dt != AttributeDataType.Enum) continue;
                if (!el.TryGetProperty("options", out var optArr) || optArr.ValueKind != JsonValueKind.Array) continue;

                var parentKeyRaw = GetString(el, "parentKey") ?? GetString(el, "parent_key");
                Guid? parentAttrIdForLookup = null;
                if (!string.IsNullOrWhiteSpace(parentKeyRaw))
                {
                    var pk = CategorySlugHelper.SanitizeAttributeKey(parentKeyRaw);
                    keyToAttrId.TryGetValue(pk, out var pid);
                    if (pid != Guid.Empty) parentAttrIdForLookup = pid;
                }

                var parentOptions = parentAttrIdForLookup.HasValue
                    ? await _db.CategoryAttributeOptions
                        .Where(o => o.CategoryAttributeId == parentAttrIdForLookup.Value)
                        .ToListAsync(cancellationToken)
                    : new List<CategoryAttributeOption>();

                var oi = 0;
                foreach (var opt in optArr.EnumerateArray())
                {
                    var vk = GetString(opt, "valueKey") ?? GetString(opt, "key") ?? GetString(opt, "value");
                    var lbl = GetString(opt, "label") ?? GetString(opt, "name") ?? vk;
                    if (string.IsNullOrWhiteSpace(vk)) continue;

                    Guid? parentOptId = null;
                    var parentValue = GetString(opt, "parentValue") ?? GetString(opt, "parent_value");
                    if (!string.IsNullOrWhiteSpace(parentValue) && parentOptions.Count > 0)
                    {
                        var po = parentOptions.FirstOrDefault(p =>
                            string.Equals(p.ValueKey, parentValue, StringComparison.OrdinalIgnoreCase));
                        if (po is not null) parentOptId = po.Id;
                    }

                    _db.CategoryAttributeOptions.Add(new CategoryAttributeOption
                    {
                        Id = Guid.NewGuid(),
                        CategoryAttributeId = attrId,
                        ValueKey = vk,
                        Label = lbl ?? vk,
                        SortOrder = ++oi,
                        ParentOptionId = parentOptId,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                }
            }
        }

        if (filterCount == 0)
        {
            _db.CategoryAttributes.Add(new CategoryAttribute
            {
                Id = Guid.NewGuid(),
                CategoryId = child.Id,
                AttributeKey = "aciklama",
                DisplayName = "Açıklama",
                DataType = AttributeDataType.String,
                IsRequired = false,
                SortOrder = 1,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    private static bool IsTruthy(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p)) return false;
        return p.ValueKind == JsonValueKind.True
            || (p.ValueKind == JsonValueKind.String && string.Equals(p.GetString(), "true", StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
