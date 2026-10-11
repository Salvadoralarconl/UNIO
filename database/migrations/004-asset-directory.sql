-- Add references without rewriting the original text snapshots, rows, or colors.
ALTER TABLE inventory_departments ADD COLUMN id uuid NOT NULL DEFAULT gen_random_uuid(), ADD COLUMN active boolean NOT NULL DEFAULT true, ADD COLUMN revision integer NOT NULL DEFAULT 1;
ALTER TABLE inventory_departments ADD UNIQUE(company_id,id);
CREATE TABLE employees (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), company_id uuid NOT NULL REFERENCES companies(id),
 display_name text NOT NULL CHECK(length(trim(display_name)) BETWEEN 1 AND 100), email text CHECK(length(email)<=254), department_id uuid,
 active boolean NOT NULL DEFAULT true, revision integer NOT NULL DEFAULT 1, UNIQUE(company_id,id),
 FOREIGN KEY(company_id,department_id) REFERENCES inventory_departments(company_id,id)
);
CREATE TABLE locations (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), company_id uuid NOT NULL REFERENCES companies(id),
 name text NOT NULL CHECK(length(trim(name)) BETWEEN 1 AND 100), parent_id uuid,
 active boolean NOT NULL DEFAULT true, revision integer NOT NULL DEFAULT 1, UNIQUE(company_id,id),
 CHECK(parent_id IS DISTINCT FROM id), FOREIGN KEY(company_id,parent_id) REFERENCES locations(company_id,id)
);
ALTER TABLE inventory_assets ADD COLUMN department_id uuid, ADD COLUMN location_id uuid;
ALTER TABLE inventory_assets ADD FOREIGN KEY(company_id,department_id) REFERENCES inventory_departments(company_id,id),
 ADD FOREIGN KEY(company_id,location_id) REFERENCES locations(company_id,id),
 ADD FOREIGN KEY(company_id,employee_id) REFERENCES employees(company_id,id);
ALTER TABLE memberships ADD FOREIGN KEY(company_id,employee_id) REFERENCES employees(company_id,id),
 ADD FOREIGN KEY(company_id,department_id) REFERENCES inventory_departments(company_id,id);
CREATE TABLE directory_mapping_review (
 company_id uuid NOT NULL, asset_id uuid NOT NULL, kind text NOT NULL CHECK(kind IN ('employee','department','location')),
 legacy_text text NOT NULL, reason text NOT NULL, PRIMARY KEY(company_id,asset_id,kind),
 FOREIGN KEY(company_id,asset_id) REFERENCES inventory_assets(company_id,id)
);
-- Departments already have an authoritative company-scoped name/color directory.
WITH matches AS (
 SELECT a.id,a.company_id,min(d.id::text)::uuid AS directory_id FROM inventory_assets a
 JOIN inventory_departments d ON d.company_id=a.company_id AND lower(trim(d.name))=lower(trim(a.department))
 GROUP BY a.company_id,a.id HAVING count(*)=1
)
UPDATE inventory_assets a SET department_id=m.directory_id FROM matches m WHERE a.company_id=m.company_id AND a.id=m.id;
-- Location labels are directory entries, not people; preserve each distinct exact trimmed label.
INSERT INTO locations(company_id,name) SELECT DISTINCT company_id,trim(location) FROM inventory_assets WHERE length(trim(location)) BETWEEN 1 AND 100;
WITH matches AS (
 SELECT a.id,a.company_id,min(l.id::text)::uuid AS directory_id FROM inventory_assets a JOIN locations l
 ON l.company_id=a.company_id AND lower(l.name)=lower(trim(a.location)) GROUP BY a.company_id,a.id HAVING count(*)=1
)
UPDATE inventory_assets a SET location_id=m.directory_id FROM matches m WHERE a.company_id=m.company_id AND a.id=m.id;
-- Seed only names whose department context resolves to one stable directory entry.
INSERT INTO employees(company_id,display_name,department_id)
 SELECT company_id,min(trim(assignee)),min(department_id::text)::uuid FROM inventory_assets
 WHERE nullif(trim(assignee),'') IS NOT NULL AND length(trim(assignee))<=100
 GROUP BY company_id,lower(trim(assignee))
 HAVING count(DISTINCT department_id)=1 AND count(*) FILTER(WHERE department_id IS NULL)=0;
UPDATE inventory_assets a SET employee_id=e.id FROM employees e
 WHERE a.company_id=e.company_id AND lower(trim(a.assignee))=lower(e.display_name) AND a.department_id=e.department_id;
INSERT INTO directory_mapping_review(company_id,asset_id,kind,legacy_text,reason)
 SELECT company_id,id,'employee',assignee,'Unresolved or conflicting legacy assignee department' FROM inventory_assets WHERE nullif(trim(assignee),'') IS NOT NULL AND employee_id IS NULL;
INSERT INTO directory_mapping_review(company_id,asset_id,kind,legacy_text,reason)
 SELECT company_id,id,'department',department,'Unresolved or ambiguous department' FROM inventory_assets WHERE nullif(trim(department),'') IS NOT NULL AND department_id IS NULL;
INSERT INTO directory_mapping_review(company_id,asset_id,kind,legacy_text,reason)
 SELECT company_id,id,'location',location,'Unresolved or ambiguous location' FROM inventory_assets WHERE nullif(trim(location),'') IS NOT NULL AND location_id IS NULL;
CREATE INDEX employees_company_name ON employees(company_id,display_name,id);
CREATE INDEX locations_company_name ON locations(company_id,name,id);
