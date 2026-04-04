namespace HemenIlanVer.Contracts.Categories;

public sealed record CategoryTreeDto(Guid Id, Guid? ParentId, string Name, string Slug, int SortOrder, string? DefaultListingType, IReadOnlyList<CategoryTreeDto> Children);
public sealed record CategoryAttributeOptionDto(string ValueKey, string Label, int SortOrder);
public sealed record CategoryAttributeDto(Guid Id, string AttributeKey, string DisplayName, string DataType, bool IsRequired, int SortOrder, IReadOnlyList<CategoryAttributeOptionDto> Options);
public sealed record CategoryAttributesResponse(Guid CategoryId, IReadOnlyList<CategoryAttributeDto> Attributes);
