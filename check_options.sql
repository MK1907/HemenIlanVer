-- Her kategoride model.ParentAttributeId = brand.Id yap (brand ve model ikisi de olan kategorilerde)
UPDATE category_attributes AS m
SET "ParentAttributeId" = b."Id"
FROM category_attributes AS b
WHERE b."AttributeKey" = 'brand'
  AND m."AttributeKey" = 'model'
  AND b."CategoryId" = m."CategoryId"
  AND m."ParentAttributeId" IS NULL;

-- Mevcut bağlantısız (ParentOptionId=NULL) model seçeneklerini sil (yeniden doldurulacak)
DELETE FROM category_attribute_options
WHERE "CategoryAttributeId" IN (
    SELECT "Id" FROM category_attributes WHERE "AttributeKey" = 'model'
)
AND "ParentOptionId" IS NULL;

-- Sonuç
SELECT c."Name", a."AttributeKey", a."ParentAttributeId", COUNT(o."Id") as opt_count
FROM category_attributes a
JOIN categories c ON c."Id" = a."CategoryId"
LEFT JOIN category_attribute_options o ON o."CategoryAttributeId" = a."Id"
WHERE a."AttributeKey" IN ('brand','model')
GROUP BY c."Name", a."AttributeKey", a."ParentAttributeId", a."Id"
ORDER BY c."Name", a."AttributeKey";
