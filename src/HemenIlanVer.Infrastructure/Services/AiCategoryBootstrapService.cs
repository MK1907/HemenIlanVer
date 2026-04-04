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
        if (!b.TryGetProperty("needed", out var needed) || needed.ValueKind != JsonValueKind.True)
            return;

        var rootName = GetString(b, "rootName") ?? "Yeni ana kategori";
        var rootSlug = CategorySlugHelper.SanitizeSlug(GetString(b, "rootSlug") ?? rootName);
        var childName = GetString(b, "childName") ?? "Genel";
        var childSlug = CategorySlugHelper.SanitizeSlug(GetString(b, "childSlug") ?? childName);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var root = await _db.Categories
            .FirstOrDefaultAsync(x => x.ParentId == null && x.Slug == rootSlug, cancellationToken);
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

        var child = await _db.Categories
            .FirstOrDefaultAsync(x => x.ParentId == root.Id && x.Slug == childSlug, cancellationToken);
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

        var hasAttrs = await _db.CategoryAttributes.AnyAsync(a => a.CategoryId == child.Id, cancellationToken);
        if (hasAttrs)
        {
            await tx.CommitAsync(cancellationToken);
            return;
        }

        var filterCount = 0;
        if (b.TryGetProperty("filters", out var filtersArr) && filtersArr.ValueKind == JsonValueKind.Array)
        {
            var sortOrder = 0;
            foreach (var el in filtersArr.EnumerateArray())
            {
                sortOrder++;
                var keyRaw = GetString(el, "key") ?? GetString(el, "attributeKey");
                if (string.IsNullOrWhiteSpace(keyRaw)) continue;
                var key = CategorySlugHelper.SanitizeAttributeKey(keyRaw);
                var display = GetString(el, "displayName") ?? key;
                var dtStr = GetString(el, "dataType") ?? "String";
                var req = el.TryGetProperty("required", out var rq) && rq.ValueKind == JsonValueKind.True;
                if (!Enum.TryParse<AttributeDataType>(dtStr, true, out var dt))
                    dt = AttributeDataType.String;

                if (dt == AttributeDataType.Enum)
                {
                    if (!el.TryGetProperty("options", out var opts) || opts.ValueKind != JsonValueKind.Array || opts.GetArrayLength() < 2)
                        dt = AttributeDataType.String;
                }

                var attrId = Guid.NewGuid();
                _db.CategoryAttributes.Add(new CategoryAttribute
                {
                    Id = attrId,
                    CategoryId = child.Id,
                    AttributeKey = key,
                    DisplayName = display,
                    DataType = dt,
                    IsRequired = req,
                    SortOrder = sortOrder,
                    CreatedAt = DateTimeOffset.UtcNow
                });

                if (dt == AttributeDataType.Enum && el.TryGetProperty("options", out var optArr) && optArr.ValueKind == JsonValueKind.Array)
                {
                    var oi = 0;
                    foreach (var opt in optArr.EnumerateArray())
                    {
                        var vk = GetString(opt, "valueKey") ?? GetString(opt, "key");
                        var lbl = GetString(opt, "label") ?? vk;
                        if (string.IsNullOrWhiteSpace(vk)) continue;
                        _db.CategoryAttributeOptions.Add(new CategoryAttributeOption
                        {
                            Id = Guid.NewGuid(),
                            CategoryAttributeId = attrId,
                            ValueKey = vk,
                            Label = lbl ?? vk,
                            SortOrder = ++oi,
                            CreatedAt = DateTimeOffset.UtcNow
                        });
                    }
                }

                filterCount++;
            }
        }

        if (filterCount == 0)
        {
            var attrId = Guid.NewGuid();
            _db.CategoryAttributes.Add(new CategoryAttribute
            {
                Id = attrId,
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

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
