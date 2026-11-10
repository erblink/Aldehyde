-- Move perfume embeddings from "EmbeddingJson" (float32 JSON text, ~16 KB per row) to "Embedding" (float16 bytea, 2 KB per row).
-- Postgres (Aiven). Run each step on its own with psql -f, and check Aiven disk usage between steps.
-- Local SQLite dev DB instead of step 1: ALTER TABLE "Perfumes" ADD COLUMN "Embedding" BLOB;

-- Step 1: add the column. Idempotent.
-- STORAGE MAIN keeps the 2 KB value inside the row, so once the JSON is gone the TOAST table is empty
-- and a plain VACUUM can give its space back without VACUUM FULL.
ALTER TABLE "Perfumes" ADD COLUMN IF NOT EXISTS "Embedding" bytea;
ALTER TABLE "Perfumes" ALTER COLUMN "Embedding" SET STORAGE MAIN;

-- Step 2 (CLI, not SQL): run convert-embeddings against Aiven. It fills "Embedding" in 2,000-row chunks with 20s pauses.
-- Check it worked:
--   SELECT count(*) FILTER (WHERE "Embedding" IS NULL AND "EmbeddingJson" IS NOT NULL) AS left_to_convert FROM "Perfumes";

-- Step 3: deploy the API and check the site and suggestions work.

-- Step 4: clear the old JSON in 2,000-row chunks with pauses, so the WAL never piles up. Safe to re-run.
-- DO $$
-- DECLARE n int;
-- BEGIN
--   LOOP
--     UPDATE "Perfumes" SET "EmbeddingJson" = NULL
--     WHERE "Id" IN (SELECT "Id" FROM "Perfumes" WHERE "EmbeddingJson" IS NOT NULL AND "Embedding" IS NOT NULL LIMIT 2000);
--     GET DIAGNOSTICS n = ROW_COUNT;
--     EXIT WHEN n = 0;
--     COMMIT;
--     PERFORM pg_sleep(20);
--   END LOOP;
-- END $$;

-- Step 5: return the space. Plain VACUUM truncates the now-empty TOAST table.
-- VACUUM (VERBOSE) "Perfumes";
-- SELECT pg_size_pretty(pg_total_relation_size('"Perfumes"'));

-- Step 6, after a few days without problems: drop the old column, then remove EmbeddingJson and convert-embeddings from the code.
-- ALTER TABLE "Perfumes" DROP COLUMN IF EXISTS "EmbeddingJson";
