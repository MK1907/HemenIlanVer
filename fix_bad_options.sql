-- Otomobil kategorisinde yakıt tipinden metanol, hidrojen vb. temizle
DELETE FROM "CategoryAttributeOptions"
WHERE "CategoryAttributeId" IN (
    SELECT ca."Id" FROM "CategoryAttributes" ca
    JOIN "Categories" c ON c."Id" = ca."CategoryId"
    WHERE ca."AttributeKey" ILIKE 'fuel'
       OR ca."AttributeKey" ILIKE 'yakit%'
       OR ca."AttributeKey" ILIKE 'yakıt%'
)
AND "ValueKey" IN (
    'metanol', 'methanol', 'hidrojen', 'hydrogen', 'biyodiziel', 'biodizel',
    'etanol', 'ethanol', 'biyodizel', 'biofuel', 'lh2', 'cng-lng'
);

-- Genel: beklenen anahtar eşleşmelerine uymayan garip yakıt değerleri
-- (Sadece izin verilen yakıt türlerini tut)
DELETE FROM "CategoryAttributeOptions"
WHERE "CategoryAttributeId" IN (
    SELECT ca."Id" FROM "CategoryAttributes" ca
    JOIN "Categories" c ON c."Id" = ca."CategoryId"
    WHERE ca."AttributeKey" ILIKE 'fuel'
       OR ca."AttributeKey" ILIKE 'yakit%'
       OR ca."AttributeKey" ILIKE 'yakıt%'
)
AND "ValueKey" NOT IN (
    'benzin', 'dizel', 'lpg', 'elektrik', 'hibrit', 'plug-in-hibrit', 'plug-in hibrit',
    'dogalgaz', 'doğalgaz', 'cng', 'hybrid', 'electric', 'diesel', 'gasoline',
    'plugin-hibrit', 'plugin-hybrid', 'benzin-lpg', 'benzin-dogalgaz'
);
