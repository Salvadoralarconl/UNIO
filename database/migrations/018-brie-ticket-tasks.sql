-- Brie-only durable workflow state. No existing company records are changed.
CREATE TABLE brie_ticket_tasks (
 company_id uuid NOT NULL, user_id uuid NOT NULL, id uuid NOT NULL,
 state jsonb NOT NULL, updated_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(company_id,user_id,id),
 FOREIGN KEY(company_id,user_id) REFERENCES memberships(company_id,user_id) ON DELETE CASCADE
);
CREATE TABLE brie_ticket_receipts (
 company_id uuid NOT NULL, user_id uuid NOT NULL, request_id uuid NOT NULL,
 fingerprint text NOT NULL, response jsonb NOT NULL, created_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(company_id,user_id,request_id),
 FOREIGN KEY(company_id,user_id) REFERENCES memberships(company_id,user_id) ON DELETE CASCADE
);
