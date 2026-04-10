namespace HemenIlanVer.Contracts.Listings;

public sealed record PhotoAnalysisDto(
    int OverallConditionScore,
    string ConditionLabel,
    bool HasScratchOrDent,
    bool HasPaintDifference,
    bool SuspectedTaxiOrRental,
    bool InteriorDamage,
    List<string> Findings,
    List<string> Warnings,
    string Summary
);
