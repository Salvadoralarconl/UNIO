CREATE TABLE equipment_kits (
 id uuid PRIMARY KEY, company_id uuid NOT NULL REFERENCES companies(id), name text NOT NULL CHECK(length(btrim(name)) BETWEEN 1 AND 100),
 department_id uuid, active boolean NOT NULL DEFAULT true, revision integer NOT NULL DEFAULT 1 CHECK(revision>0), lines jsonb NOT NULL CHECK(jsonb_typeof(lines)='array'),
 UNIQUE(company_id,id), FOREIGN KEY(company_id,department_id) REFERENCES inventory_departments(company_id,id)
);
CREATE TABLE equipment_tickets (
 id uuid PRIMARY KEY, company_id uuid NOT NULL REFERENCES companies(id), request_id uuid NOT NULL, title text NOT NULL,
 state text NOT NULL CHECK(state IN ('Pending','Approved','Ready','Completed','Cancelled')), revision integer NOT NULL DEFAULT 1 CHECK(revision>0),
 created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(), UNIQUE(company_id,id), UNIQUE(company_id,request_id)
);
CREATE TABLE equipment_requests (
 id uuid PRIMARY KEY, company_id uuid NOT NULL REFERENCES companies(id), title text NOT NULL CHECK(length(btrim(title)) BETWEEN 1 AND 200),
 employee_id uuid NOT NULL, department_id uuid, kit_id uuid NOT NULL, kit_name text NOT NULL, needed_date date, notes text CHECK(length(notes)<=4000),
 state text NOT NULL CHECK(state IN ('Pending','Approved','Ready','Completed','Cancelled')), ticket_id uuid NOT NULL,
 revision integer NOT NULL DEFAULT 1 CHECK(revision>0), created_at timestamptz NOT NULL DEFAULT now(), lines jsonb NOT NULL CHECK(jsonb_typeof(lines)='array'),
 UNIQUE(company_id,id), UNIQUE(company_id,ticket_id), FOREIGN KEY(company_id,employee_id) REFERENCES employees(company_id,id),
 FOREIGN KEY(company_id,department_id) REFERENCES inventory_departments(company_id,id), FOREIGN KEY(company_id,kit_id) REFERENCES equipment_kits(company_id,id),
 FOREIGN KEY(company_id,ticket_id) REFERENCES equipment_tickets(company_id,id) DEFERRABLE INITIALLY DEFERRED
);
ALTER TABLE equipment_tickets ADD FOREIGN KEY(company_id,request_id) REFERENCES equipment_requests(company_id,id) DEFERRABLE INITIALLY DEFERRED;
CREATE TABLE equipment_request_assets (
 company_id uuid NOT NULL, request_id uuid NOT NULL, asset_id uuid NOT NULL, PRIMARY KEY(company_id,request_id,asset_id),
 FOREIGN KEY(company_id,request_id) REFERENCES equipment_requests(company_id,id), FOREIGN KEY(company_id,asset_id) REFERENCES inventory_assets(company_id,id)
);
CREATE INDEX equipment_requests_company_created ON equipment_requests(company_id,created_at DESC,id);
CREATE INDEX equipment_kits_company_name ON equipment_kits(company_id,name,id);
