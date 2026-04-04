namespace HemenIlanVer.Contracts.Listings;

public sealed record ListingAttributeValueInputDto(Guid CategoryAttributeId, string? ValueText, int? ValueInt, decimal? ValueDecimal, bool? ValueBool);
public sealed record CreateListingRequest(
    Guid CategoryId,
    string Title,
    string Description,
    decimal? Price,
    string Currency,
    string ListingType,
    Guid? CityId,
    Guid? DistrictId,
    IReadOnlyList<ListingAttributeValueInputDto> Attributes,
    bool Publish);
public sealed record ListingSummaryDto(Guid Id, string Title, decimal? Price, string Currency, string CityName, string? DistrictName, string CategoryName, DateTimeOffset CreatedAt, string? PrimaryImageUrl, int ViewCount);
public sealed record ListingDetailDto(
    Guid Id,
    Guid UserId,
    string Title,
    string Description,
    decimal? Price,
    string Currency,
    string ListingType,
    string Status,
    Guid CategoryId,
    string CategoryName,
    Guid? CityId,
    string? CityName,
    Guid? DistrictId,
    string? DistrictName,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyList<string> ImageUrls,
    DateTimeOffset CreatedAt,
    int ViewCount);
public sealed record PagedListingsDto(IReadOnlyList<ListingSummaryDto> Items, int Page, int PageSize, int TotalCount);
