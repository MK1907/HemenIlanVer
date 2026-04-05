namespace HemenIlanVer.Contracts.Ai;

public sealed record ListingCategoryDetectRequest(string Prompt);
public sealed record SubCategoryOptionDto(Guid Id, string Name, string Slug);
public sealed record ListingCategoryDetectResponse(
    Guid TraceId,
    Guid RootCategoryId,
    string RootName,
    IReadOnlyList<SubCategoryOptionDto> SubCategories,
    Guid? SuggestedLeafCategoryId,
    double Confidence,
    bool UsedMockProvider,
    string? SuggestedTitle = null,
    string? SuggestedDescription = null,
    decimal? SuggestedPrice = null,
    IReadOnlyDictionary<string, string>? SuggestedAttributeValues = null);

public sealed record ListingPartialSuggestRequest(string PartialText);
public sealed record ListingPartialSuggestResponse(Guid TraceId, IReadOnlyList<string> Suggestions);

public sealed record SearchExtractRequest(string Prompt, Guid? CategoryId);
public sealed record SearchExtractResponse(
    Guid TraceId,
    Guid? CategoryId,
    IReadOnlyDictionary<string, string?> Filters,
    Guid? CityId,
    string? CityName,
    decimal? MinPrice,
    decimal? MaxPrice,
    string? SortPreference,
    double Confidence,
    bool UsedMockProvider);
