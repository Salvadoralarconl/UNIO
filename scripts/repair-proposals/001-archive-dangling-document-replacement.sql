-- PROPOSAL ONLY. Actual Lab execution requires explicit approval after whole-change review.
-- The rehearsal runner executes this only inside its generated isolated copy.
BEGIN;
LOCK TABLE documents IN SHARE ROW EXCLUSIVE MODE;
DO $$ BEGIN
 IF (SELECT count(*) FROM documents d LEFT JOIN documents target ON target.id=d.replaced_by WHERE d.replaced_by IS NOT NULL AND target.id IS NULL) <> 1
 OR NOT EXISTS(SELECT 1 FROM documents reviewed WHERE reviewed.id='0b1d819b-8ba0-4de2-8f2f-6a82217e6326' AND reviewed.replaced_by='11e84017-1a5b-4b14-a807-78aa19ff2d1c'
 AND NOT EXISTS(SELECT 1 FROM documents target WHERE target.id=reviewed.replaced_by))
 THEN RAISE EXCEPTION 'Repair proposal no longer matches reviewed legacy conflict; stop and review again'; END IF;
END $$;
CREATE TABLE legacy_document_replacement_archive (
 document_id uuid NOT NULL, original_replaced_by uuid NOT NULL,
 reason text NOT NULL CHECK(reason='missing_legacy_target'), archived_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(document_id,original_replaced_by)
);
INSERT INTO legacy_document_replacement_archive(document_id,original_replaced_by,reason)
 SELECT id,replaced_by,'missing_legacy_target' FROM documents
 WHERE id='0b1d819b-8ba0-4de2-8f2f-6a82217e6326' AND replaced_by='11e84017-1a5b-4b14-a807-78aa19ff2d1c';
UPDATE documents SET replaced_by=NULL
 WHERE id='0b1d819b-8ba0-4de2-8f2f-6a82217e6326' AND replaced_by='11e84017-1a5b-4b14-a807-78aa19ff2d1c';
COMMIT;
