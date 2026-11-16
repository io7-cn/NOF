-- Run once against an old receiver database after stopping consumers and backing up the file.
-- Existing duplicate business keys deliberately fail the unique-index creation; resolve them explicitly.
BEGIN IMMEDIATE;
ALTER TABLE "Receipts" ADD COLUMN "IdempotencyKey" TEXT NOT NULL DEFAULT '';
UPDATE "Receipts"
SET "IdempotencyKey" = CASE
    WHEN "OrderId" IS NOT NULL THEN 'order:' || lower("OrderId")
    ELSE 'legacy:' || lower("Id")
END;
CREATE UNIQUE INDEX "IX_Receipts_IdempotencyKey_Kind_TenantId"
    ON "Receipts" ("IdempotencyKey", "Kind", "TenantId");
COMMIT;
