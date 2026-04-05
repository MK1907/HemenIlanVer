namespace HemenIlanVer.Contracts.Categories;

public sealed record CategoryTreeDto(Guid Id, Guid? ParentId, string Name, string Slug, int SortOrder, string? DefaultListingType, IReadOnlyList<CategoryTreeDto> Children);
public sealed record CategoryAttributeOptionDto(Guid Id, string ValueKey, string Label, int SortOrder, Guid? ParentOptionId);
public sealed record CategoryAttributeDto(Guid Id, string AttributeKey, string DisplayName, string DataType, bool IsRequired, int SortOrder, Guid? ParentAttributeId, IReadOnlyList<CategoryAttributeOptionDto> Options);
public sealed record CategoryAttributesResponse(Guid CategoryId, IReadOnlyList<CategoryAttributeDto> Attributes);
