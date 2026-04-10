namespace HemenIlanVer.Contracts.Listings;

public sealed record PhotoAnalysisDto(
    // Genel skor
    int OverallConditionScore,
    string ConditionLabel,

    // Fotoğraftan tespit edilen araç kimliği
    string? DetectedBrand,
    string? DetectedModel,
    string? DetectedYear,
    string? DetectedColor,
    string? DetectedBodyType,
    string? DetectedFuelType,
    string? DetectedTransmission,
    string? DetectedEngineSize,
    string? DetectedKmApprox,

    // Dış durum
    bool HasScratchOrDent,
    bool HasPaintDifference,
    bool HasGlassDamage,
    bool HasWheelOrTireDamage,
    bool HasRustOrCorrosion,
    bool HasBodyDeformation,

    // İç mekan
    bool InteriorDamage,
    bool HasSeatWear,
    bool HasDashboardDamage,
    bool HasCeilingStain,

    // Şüpheli durumlar
    bool SuspectedTaxiOrRental,
    bool SuspectedAccidentHistory,
    bool SuspectedKmTampering,
    bool HasHiddenAreas,

    // Fotoğraf kalitesi
    int PhotoQualityScore,
    bool IsProfessionalPhoto,

    // Marka/model uyumsuzluğu
    bool BrandMismatch,
    string? BrandMismatchDetail,

    // Tespitler ve uyarılar
    List<string> Findings,
    List<string> Warnings,
    string Summary
);
