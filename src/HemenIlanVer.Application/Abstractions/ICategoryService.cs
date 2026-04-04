using HemenIlanVer.Contracts.Categories;

namespace HemenIlanVer.Application.Abstractions;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryTreeDto>> GetTreeAsync(CancellationToken cancellationToken = default);
    Task<CategoryAttributesResponse> GetAttributesAsync(Guid categoryId, CancellationToken cancellationToken = default);
}
