-- All legacy records move together; no IDs, prose, file paths or relations are rewritten.
CREATE TABLE companies (
 id uuid PRIMARY KEY, name text NOT NULL, active boolean NOT NULL DEFAULT true, revision integer NOT NULL DEFAULT 1,
 created_at timestamptz NOT NULL DEFAULT now()
);
INSERT INTO companies(id,name) VALUES('00000000-0000-0000-0000-000000000001','Legacy company');
CREATE TABLE users (
 id uuid PRIMARY KEY, issuer text NOT NULL, identity_subject text NOT NULL, display_name text NOT NULL,
 email text, active boolean NOT NULL DEFAULT true, UNIQUE(issuer,identity_subject)
);
CREATE TABLE memberships (
 company_id uuid NOT NULL REFERENCES companies(id), user_id uuid NOT NULL REFERENCES users(id),
 role text NOT NULL CHECK(role IN ('Admin','Technician','Manager','Employee')),
 employee_id uuid, department_id uuid, active boolean NOT NULL DEFAULT true, revision integer NOT NULL DEFAULT 1,
 PRIMARY KEY(company_id,user_id)
);

DO $$
DECLARE owned text; edge record;
BEGIN
 FOREACH owned IN ARRAY ARRAY['documents','passages','conversations','library_folders','document_folders','chat_projects','pinned_chat_folders','conversation_shares','inventory_assets','inventory_activity','inventory_documentation','inventory_departments'] LOOP
  EXECUTE format('ALTER TABLE %I ADD COLUMN company_id uuid',owned);
  EXECUTE format('UPDATE %I SET company_id=''00000000-0000-0000-0000-000000000001''',owned);
  EXECUTE format('ALTER TABLE %I ALTER COLUMN company_id SET NOT NULL',owned);
  EXECUTE format('ALTER TABLE %I ADD FOREIGN KEY(company_id) REFERENCES companies(id)',owned);
  IF owned NOT IN ('document_folders','inventory_documentation','inventory_departments') THEN
   EXECUTE format('ALTER TABLE %I ADD UNIQUE(company_id,id)',owned);
  END IF;
 END LOOP;
 -- Replace legacy single-column child references with company-scoped references.
 FOR edge IN SELECT c.conrelid::regclass AS tab,c.conname FROM pg_constraint c
  JOIN pg_class t ON t.oid=c.conrelid JOIN pg_namespace n ON n.oid=t.relnamespace
  WHERE n.nspname=current_schema() AND c.contype='f'
  AND t.relname IN ('passages','library_folders','document_folders','conversations','inventory_activity','inventory_documentation')
  AND c.confrelid <> 'companies'::regclass
 LOOP
  EXECUTE format('ALTER TABLE %s DROP CONSTRAINT %I',edge.tab,edge.conname);
 END LOOP;
END $$;
ALTER TABLE passages ADD FOREIGN KEY(company_id,revision_id) REFERENCES documents(company_id,id);
ALTER TABLE library_folders ADD FOREIGN KEY(company_id,parent_id) REFERENCES library_folders(company_id,id);
ALTER TABLE document_folders ADD FOREIGN KEY(company_id,document_id) REFERENCES documents(company_id,id) ON DELETE CASCADE;
ALTER TABLE document_folders ADD FOREIGN KEY(company_id,folder_id) REFERENCES library_folders(company_id,id);
ALTER TABLE conversations ADD FOREIGN KEY(company_id,project_id) REFERENCES chat_projects(company_id,id) ON DELETE SET NULL (project_id);
ALTER TABLE conversations ADD FOREIGN KEY(company_id,pinned_folder_id) REFERENCES pinned_chat_folders(company_id,id) ON DELETE SET NULL (pinned_folder_id);
ALTER TABLE inventory_activity ADD FOREIGN KEY(company_id,asset_id) REFERENCES inventory_assets(company_id,id) ON DELETE CASCADE;
ALTER TABLE inventory_documentation ADD FOREIGN KEY(company_id,document_id) REFERENCES documents(company_id,id) ON DELETE CASCADE;
ALTER TABLE documents ADD FOREIGN KEY(company_id,replaced_by) REFERENCES documents(company_id,id);

ALTER TABLE inventory_assets DROP CONSTRAINT inventory_assets_normalized_tag_key;
ALTER TABLE inventory_assets ADD UNIQUE(company_id,normalized_tag);
ALTER TABLE inventory_departments DROP CONSTRAINT inventory_departments_pkey;
ALTER TABLE inventory_departments ADD PRIMARY KEY(company_id,normalized_name);
ALTER TABLE inventory_documentation DROP CONSTRAINT inventory_documentation_pkey;
ALTER TABLE inventory_documentation ADD PRIMARY KEY(company_id,category);

-- Storage was discovered in UploadService and Program: uploads/{id}.{ext} and
-- uploads/images/{id}.bin. Metadata is an allowlist; unmapped disk files remain
-- untouched and are inaccessible to the authenticated file reader.
CREATE TABLE owned_files (
 id uuid PRIMARY KEY, company_id uuid NOT NULL REFERENCES companies(id),
 storage_path text NOT NULL UNIQUE, kind text NOT NULL CHECK(kind IN ('document','wiki_image')),
 quarantined boolean NOT NULL DEFAULT false, UNIQUE(company_id,id)
);
CREATE TABLE document_files (
 company_id uuid NOT NULL REFERENCES companies(id), document_id uuid NOT NULL, file_id uuid NOT NULL,
 PRIMARY KEY(company_id,document_id,file_id),
 FOREIGN KEY(company_id,document_id) REFERENCES documents(company_id,id) ON DELETE CASCADE,
 FOREIGN KEY(company_id,file_id) REFERENCES owned_files(company_id,id)
);
INSERT INTO owned_files(id,company_id,storage_path,kind)
 SELECT (array_agg(id ORDER BY id))[1],company_id,filename,'document' FROM documents
 WHERE filename LIKE 'uploads/%' AND filename NOT LIKE 'uploads/images/%'
 AND filename !~ '(^|/)\.\.(/|$)' AND filename !~ E'[\\\\]' GROUP BY company_id,filename;
INSERT INTO document_files(company_id,document_id,file_id)
 SELECT d.company_id,d.id,f.id FROM documents d JOIN owned_files f ON f.company_id=d.company_id AND f.storage_path=d.filename;

WITH images AS (
 SELECT DISTINCT company_id,(matches[1])::uuid AS id FROM documents,
 LATERAL regexp_matches(coalesce(rich_content,'')||' '||canonical_text,
 '/api/library/images/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})','g') matches
)
INSERT INTO owned_files(id,company_id,storage_path,kind)
 -- A conflicting image/document ID aborts rather than guessing ownership.
 SELECT id,company_id,'uploads/images/'||id||'.bin','wiki_image' FROM images;
INSERT INTO document_files(company_id,document_id,file_id)
 SELECT DISTINCT d.company_id,d.id,(matches[1])::uuid FROM documents d,
 LATERAL regexp_matches(coalesce(d.rich_content,'')||' '||d.canonical_text,
 '/api/library/images/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})','g') matches
 JOIN owned_files f ON f.id=(matches[1])::uuid AND f.kind='wiki_image'
 WHERE f.company_id=d.company_id ON CONFLICT DO NOTHING;
