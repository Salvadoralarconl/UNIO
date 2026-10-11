ALTER TABLE equipment_tickets ALTER COLUMN request_id DROP NOT NULL;
ALTER TABLE equipment_tickets DROP CONSTRAINT equipment_tickets_state_check;
ALTER TABLE equipment_tickets ADD CONSTRAINT equipment_tickets_state_check CHECK(state IN ('Pending','Approved','Assigned','In Progress','Complete','Canceled','Ready','Completed','Cancelled'));
ALTER TABLE equipment_tickets ADD COLUMN type text NOT NULL DEFAULT 'Support' CHECK(type IN ('Support','Equipment','Onboarding','Repair','Retirement'));
ALTER TABLE equipment_tickets ADD COLUMN description text;
ALTER TABLE equipment_tickets ADD COLUMN fulfillment_lines jsonb NOT NULL DEFAULT '[]'::jsonb;
ALTER TABLE equipment_tickets ADD COLUMN due_date date;
ALTER TABLE equipment_tickets ADD COLUMN recipient_employee_id uuid;
ALTER TABLE equipment_tickets ADD COLUMN department_id uuid;
ALTER TABLE equipment_tickets ADD COLUMN location_id uuid;
ALTER TABLE equipment_tickets ADD COLUMN creator_user_id uuid REFERENCES users(id);
ALTER TABLE equipment_tickets ADD COLUMN creator_name text;
ALTER TABLE equipment_tickets ADD COLUMN approver_user_id uuid REFERENCES users(id);
ALTER TABLE equipment_tickets ADD COLUMN approver_name text;
ALTER TABLE equipment_tickets ADD COLUMN approved_at timestamptz;
ALTER TABLE equipment_tickets ADD COLUMN assignment_mode text NOT NULL DEFAULT 'Users' CHECK(assignment_mode IN ('Users','All'));
ALTER TABLE equipment_tickets ADD FOREIGN KEY(company_id,recipient_employee_id) REFERENCES employees(company_id,id);
ALTER TABLE equipment_tickets ADD FOREIGN KEY(company_id,department_id) REFERENCES inventory_departments(company_id,id);
ALTER TABLE equipment_tickets ADD FOREIGN KEY(company_id,location_id) REFERENCES locations(company_id,id);
CREATE INDEX tickets_company_created ON equipment_tickets(company_id,created_at DESC,id DESC);
UPDATE equipment_tickets t SET type=r.purpose,description=r.notes,due_date=r.needed_date,recipient_employee_id=r.employee_id,department_id=r.department_id,location_id=r.location_id FROM equipment_requests r WHERE t.company_id=r.company_id AND t.request_id=r.id;
UPDATE equipment_tickets SET state=CASE state WHEN 'Ready' THEN 'Assigned' WHEN 'Completed' THEN 'Complete' WHEN 'Cancelled' THEN 'Canceled' ELSE state END;
UPDATE equipment_tickets t SET creator_user_id=a.actor_user_id,creator_name=u.display_name FROM inventory_audit a JOIN users u ON u.id=a.actor_user_id WHERE a.company_id=t.company_id AND a.resource_id=t.id AND a.resource_type='equipment-ticket' AND a.action='Created';
UPDATE equipment_tickets t SET approver_user_id=a.actor_user_id,approver_name=u.display_name,approved_at=a.occurred_at FROM inventory_audit a JOIN users u ON u.id=a.actor_user_id WHERE a.company_id=t.company_id AND a.resource_id=t.id AND a.resource_type='equipment-ticket' AND a.action IN ('approve','approve-and-assign');
CREATE TABLE ticket_assignees(company_id uuid NOT NULL,ticket_id uuid NOT NULL,user_id uuid NOT NULL,PRIMARY KEY(company_id,ticket_id,user_id),FOREIGN KEY(company_id,ticket_id) REFERENCES equipment_tickets(company_id,id),FOREIGN KEY(company_id,user_id) REFERENCES memberships(company_id,user_id));
CREATE TABLE ticket_asset_links(
 company_id uuid NOT NULL,ticket_id uuid NOT NULL,asset_id uuid NOT NULL,outcome text NOT NULL CHECK(outcome IN ('Reference','Assign','Repair','Retire')),
 baseline_revision integer NOT NULL,baseline_status text NOT NULL,baseline_employee_id uuid,baseline_assignee text,baseline_location_id uuid,
 owned_revision integer,completed boolean NOT NULL DEFAULT false,
 PRIMARY KEY(company_id,ticket_id,asset_id),FOREIGN KEY(company_id,ticket_id) REFERENCES equipment_tickets(company_id,id),FOREIGN KEY(company_id,asset_id) REFERENCES inventory_assets(company_id,id)
);
INSERT INTO ticket_asset_links(company_id,ticket_id,asset_id,outcome,baseline_revision,baseline_status,baseline_employee_id,baseline_assignee,baseline_location_id,owned_revision,completed)
SELECT a.company_id,r.ticket_id,a.id,'Assign',a.revision,a.status,a.employee_id,a.assignee,a.location_id,a.revision,r.state='Completed' FROM equipment_request_assets x JOIN equipment_requests r ON r.company_id=x.company_id AND r.id=x.request_id JOIN inventory_assets a ON a.company_id=x.company_id AND a.id=x.asset_id;
CREATE TABLE ticket_asset_reservations(company_id uuid NOT NULL,asset_id uuid NOT NULL,ticket_id uuid NOT NULL,PRIMARY KEY(company_id,asset_id),FOREIGN KEY(company_id,ticket_id,asset_id) REFERENCES ticket_asset_links(company_id,ticket_id,asset_id));
CREATE TABLE ticket_document_links(company_id uuid NOT NULL,ticket_id uuid NOT NULL,id uuid NOT NULL,document_id uuid,document_title text NOT NULL,PRIMARY KEY(company_id,ticket_id,id),UNIQUE(company_id,ticket_id,document_id),FOREIGN KEY(company_id,ticket_id) REFERENCES equipment_tickets(company_id,id),FOREIGN KEY(company_id,document_id) REFERENCES documents(company_id,id) ON DELETE SET NULL (document_id));
CREATE TABLE ticket_comments(company_id uuid NOT NULL,ticket_id uuid NOT NULL,id uuid NOT NULL,author_user_id uuid NOT NULL REFERENCES users(id),author_name text NOT NULL,body text NOT NULL CHECK(length(btrim(body)) BETWEEN 1 AND 4000),created_at timestamptz NOT NULL DEFAULT clock_timestamp(),PRIMARY KEY(company_id,id),FOREIGN KEY(company_id,ticket_id) REFERENCES equipment_tickets(company_id,id));
CREATE TABLE ticket_activity(company_id uuid NOT NULL,ticket_id uuid NOT NULL,id uuid NOT NULL,actor_user_id uuid REFERENCES users(id),actor_name text NOT NULL,action text NOT NULL,detail text,created_at timestamptz NOT NULL DEFAULT clock_timestamp(),PRIMARY KEY(company_id,id),FOREIGN KEY(company_id,ticket_id) REFERENCES equipment_tickets(company_id,id));
INSERT INTO ticket_activity(company_id,ticket_id,id,actor_user_id,actor_name,action,created_at) SELECT a.company_id,t.id,a.id,a.actor_user_id,u.display_name,a.action,a.occurred_at FROM inventory_audit a JOIN equipment_tickets t ON t.company_id=a.company_id AND t.id=a.resource_id JOIN users u ON u.id=a.actor_user_id WHERE a.resource_type='equipment-ticket';
CREATE TABLE ticket_settings(company_id uuid PRIMARY KEY REFERENCES companies(id),onboarding_document_id uuid,FOREIGN KEY(company_id,onboarding_document_id) REFERENCES documents(company_id,id) ON DELETE SET NULL (onboarding_document_id));
CREATE INDEX ticket_comments_paging ON ticket_comments(company_id,ticket_id,created_at DESC,id DESC);
CREATE INDEX ticket_activity_paging ON ticket_activity(company_id,ticket_id,created_at DESC,id DESC);
ALTER TABLE inventory_audit ADD COLUMN actor_name_snapshot text;
ALTER TABLE inventory_audit ADD COLUMN ticket_id uuid;
ALTER TABLE inventory_audit ADD FOREIGN KEY(company_id,ticket_id) REFERENCES equipment_tickets(company_id,id);
