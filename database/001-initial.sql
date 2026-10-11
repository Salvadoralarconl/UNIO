-- Legacy bootstrap only. Apply numbered migrations explicitly with --migrate
-- after all company-scoped repositories and authentication are deployed.
CREATE EXTENSION IF NOT EXISTS vector;
CREATE TABLE IF NOT EXISTS documents (
 id uuid PRIMARY KEY, title text NOT NULL, version text NOT NULL,
 approval text NOT NULL CHECK (approval IN ('draft','approved','revoked')),
 indexing text NOT NULL CHECK (indexing IN ('pending','indexing','ready','failed')),
 filename text NOT NULL, content_hash text NOT NULL, canonical_text text NOT NULL,
 conflict_group text, provider text, model text, dimensions integer, chunking_version text,
 error text
);
CREATE TABLE IF NOT EXISTS passages (
 id uuid PRIMARY KEY, revision_id uuid NOT NULL REFERENCES documents(id),
 text text NOT NULL, location text NOT NULL, start_offset integer NOT NULL,
 embedding vector NOT NULL
);
CREATE INDEX IF NOT EXISTS passages_revision ON passages(revision_id);
ALTER TABLE documents ADD COLUMN IF NOT EXISTS supported_models text[] NOT NULL DEFAULT '{}';
CREATE TABLE IF NOT EXISTS conversations (
 id uuid PRIMARY KEY, title text NOT NULL, turns jsonb NOT NULL,
 updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS library_folders (
 id uuid PRIMARY KEY, name text NOT NULL CHECK(length(name) BETWEEN 1 AND 100),
 parent_id uuid REFERENCES library_folders(id)
);
CREATE TABLE IF NOT EXISTS document_folders (
 document_id uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
 folder_id uuid NOT NULL REFERENCES library_folders(id)
);

ALTER TABLE documents ADD COLUMN IF NOT EXISTS rich_content text;
ALTER TABLE documents ADD COLUMN IF NOT EXISTS archived boolean NOT NULL DEFAULT false;

ALTER TABLE documents ADD COLUMN IF NOT EXISTS replaced_by uuid;

CREATE TABLE IF NOT EXISTS chat_projects(id uuid PRIMARY KEY,name text NOT NULL CHECK(length(name) BETWEEN 1 AND 100));
CREATE TABLE IF NOT EXISTS pinned_chat_folders(id uuid PRIMARY KEY,name text NOT NULL CHECK(length(name) BETWEEN 1 AND 100));
ALTER TABLE conversations ADD COLUMN IF NOT EXISTS pinned_folder_id uuid REFERENCES pinned_chat_folders(id) ON DELETE SET NULL;
ALTER TABLE conversations ADD COLUMN IF NOT EXISTS pinned bool NOT NULL DEFAULT false;
ALTER TABLE conversations ADD COLUMN IF NOT EXISTS archived bool NOT NULL DEFAULT false;
ALTER TABLE conversations ADD COLUMN IF NOT EXISTS project_id uuid REFERENCES chat_projects(id) ON DELETE SET NULL;
CREATE TABLE IF NOT EXISTS conversation_shares(id uuid PRIMARY KEY,title text NOT NULL,turns jsonb NOT NULL,created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS inventory_assets(
 id uuid PRIMARY KEY,asset_tag text NOT NULL,normalized_tag text NOT NULL UNIQUE,
 computer text NOT NULL,serial_number text,ip_address text,assignee text,department text,location text,
 status text NOT NULL CHECK(status IN ('Available','Assigned','In repair','Retired')),
 revision int NOT NULL DEFAULT 1,updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS inventory_activity(id uuid PRIMARY KEY,asset_id uuid NOT NULL REFERENCES inventory_assets(id) ON DELETE CASCADE,computer text NOT NULL,kind text NOT NULL,created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS inventory_documentation(category text PRIMARY KEY,document_id uuid NOT NULL REFERENCES documents(id) ON DELETE CASCADE);

ALTER TABLE inventory_assets ADD COLUMN IF NOT EXISTS category text NOT NULL DEFAULT 'Computer' CHECK(category IN ('Computer','Printer','Monitor','Network','Accessory'));

ALTER TABLE inventory_assets ADD COLUMN IF NOT EXISTS custom_category text;

CREATE TABLE IF NOT EXISTS inventory_departments(normalized_name text PRIMARY KEY,name text NOT NULL,color text NOT NULL CHECK(color ~ '^#[0-9a-fA-F]{6}$'));
INSERT INTO inventory_departments(normalized_name,name,color) SELECT lower(trim(department)),min(trim(department)),'#3b82f6' FROM inventory_assets WHERE department IS NOT NULL AND trim(department)<>'' GROUP BY lower(trim(department)) ON CONFLICT DO NOTHING;
