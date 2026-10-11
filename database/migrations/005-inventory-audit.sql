CREATE TABLE inventory_audit (
 id uuid PRIMARY KEY,
 company_id uuid NOT NULL REFERENCES companies(id),
 actor_user_id uuid NOT NULL REFERENCES users(id),
 action text NOT NULL,
 resource_type text NOT NULL,
 resource_id uuid NOT NULL,
 before_snapshot jsonb,
 after_snapshot jsonb,
 occurred_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX inventory_audit_resource_history ON inventory_audit(company_id,resource_type,resource_id,occurred_at DESC,id DESC);
CREATE FUNCTION reject_inventory_audit_change() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Audit events are immutable'; END $$;
CREATE TRIGGER inventory_audit_immutable BEFORE UPDATE OR DELETE ON inventory_audit FOR EACH ROW EXECUTE FUNCTION reject_inventory_audit_change();
