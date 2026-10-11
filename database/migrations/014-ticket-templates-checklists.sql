CREATE TABLE ticket_templates (
 company_id uuid NOT NULL REFERENCES companies(id),id uuid NOT NULL,name text NOT NULL CHECK(length(btrim(name)) BETWEEN 1 AND 100),
 type text NOT NULL CHECK(type IN ('Support','Equipment','Onboarding','Repair','Retirement')),instructions text NOT NULL DEFAULT '' CHECK(length(instructions)<=8000),
 kit_id uuid,assignment_mode text NOT NULL CHECK(assignment_mode IN ('Users','All')),
 assignee_ids jsonb NOT NULL DEFAULT '[]' CHECK(jsonb_typeof(assignee_ids)='array'),document_ids jsonb NOT NULL DEFAULT '[]' CHECK(jsonb_typeof(document_ids)='array'),
 checklist jsonb NOT NULL DEFAULT '[]' CHECK(jsonb_typeof(checklist)='array'),revision integer NOT NULL DEFAULT 1 CHECK(revision>0),
 PRIMARY KEY(company_id,id),FOREIGN KEY(company_id,kit_id) REFERENCES equipment_kits(company_id,id)
);
CREATE UNIQUE INDEX ticket_templates_name ON ticket_templates(company_id,lower(btrim(name)));
ALTER TABLE equipment_tickets ADD COLUMN checklist jsonb NOT NULL DEFAULT '[]' CHECK(jsonb_typeof(checklist)='array');
