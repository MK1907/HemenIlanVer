using HemenIlanVer.Application.Abstractions;
using HemenIlanVer.Contracts.Categories;
using HemenIlanVer.Domain.Enums;
using HemenIlanVer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HemenIlanVer.Infrastructure.Services;

public sealed class CategoryService : ICategoryService
{
    private readonly AppDbContext _db;

    public CategoryService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<CategoryTreeDto>> GetTreeAsync(CancellationToken cancellationToken = default)
    {
        var all = await _db.Categories.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .Select(x => new { x.Id, x.ParentId, x.Name, x.Slug, x.SortOrder, x.DefaultListingType })
            .ToListAsync(cancellationToken);

        IReadOnlyList<CategoryTreeDto> Build(Guid? parentId) =>
            all.Where(x => x.ParentId == parentId)
                .Select(x => new CategoryTreeDto(
                    x.Id,
                    x.ParentId,
                    x.Name,
                    x.Slug,
                    x.SortOrder,
                    x.DefaultListingType?.ToString(),
                    Build(x.Id)))
                .ToList();

        return Build(null);
    }

    public async Task<CategoryAttributesResponse> GetAttributesAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        var attrs = await _db.CategoryAttributes.AsNoTracking()
            .Include(x => x.Options)
            .Where(x => x.CategoryId == categoryId)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);

        var dtos = attrs.Select(a => new CategoryAttributeDto(
            a.Id,
            a.AttributeKey,
            a.DisplayName,
            a.DataType.ToString(),
            a.IsRequired,
            a.SortOrder,
            a.Options.OrderBy(o => o.SortOrder).Select(o => new CategoryAttributeOptionDto(o.ValueKey, o.Label, o.SortOrder)).ToList()
        )).ToList();

        return new CategoryAttributesResponse(categoryId, dtos);
    }
}
