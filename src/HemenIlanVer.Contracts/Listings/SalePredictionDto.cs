namespace HemenIlanVer.Contracts.Listings;

public sealed record SalePredictionDto(
    int Score,                    // 0-100 satılma ihtimali skoru
    string ScoreLabel,            // "Yüksek" | "Orta" | "Düşük"
    int EstimatedDays,            // tahmini kaç günde satılır
    int EstimatedViews7d,         // 7 günde kaç görüntüleme
    int EstimatedMessages7d,      // 7 günde kaç mesaj
    string? PriceTip,             // "Fiyatı 20.000 TL düşürürsen 2x hızlı satılır"
    decimal? PriceDelta,          // öneri fiyat değişimi (negatif = indir)
    double? SpeedFactor,          // hız çarpanı (2 → 2x hızlı)
    string Reasoning              // kısa Türkçe açıklama
);
