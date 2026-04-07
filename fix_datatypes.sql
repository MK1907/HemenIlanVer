-- String (0) olan brand/model attribute'larını Enum (4) yap
UPDATE category_attributes
SET "DataType" = 4
WHERE "AttributeKey" IN ('brand', 'model', 'marka', 'modelAdi')
  AND "DataType" = 0;

-- Güncelleme sonucunu göster
SELECT "AttributeKey", "DataType", "DisplayName"
FROM category_attributes
WHERE "AttributeKey" IN ('brand', 'model', 'marka', 'modelAdi');
