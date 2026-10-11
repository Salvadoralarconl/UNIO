-- Abort ambiguous legacy state; never select an owner or company implicitly.
DO $$ BEGIN
 IF EXISTS(SELECT user_id FROM memberships GROUP BY user_id HAVING count(*)>1) THEN
  RAISE EXCEPTION 'Account migration conflict: multiple company memberships; resolve explicitly before retrying';
 END IF;
 IF EXISTS(SELECT m.company_id FROM memberships m JOIN users u ON u.id=m.user_id WHERE m.active AND u.active AND m.role='Admin' GROUP BY m.company_id HAVING count(*)>1) THEN
  RAISE EXCEPTION 'Account migration conflict: multiple enabled Administrators; resolve explicitly before retrying';
 END IF;
END $$;
ALTER TABLE memberships DROP CONSTRAINT memberships_role_check;
ALTER TABLE memberships ADD COLUMN visibility_scope text NOT NULL DEFAULT 'Company' CHECK(visibility_scope IN ('Company','AssignedOnly'));
UPDATE memberships SET visibility_scope='AssignedOnly' WHERE role='Employee';
UPDATE memberships SET role=CASE role WHEN 'Admin' THEN 'Administrator' WHEN 'Technician' THEN 'Editor' ELSE 'Viewer' END;
ALTER TABLE memberships ADD CHECK(role IN ('Administrator','Editor','Viewer'));
ALTER TABLE memberships ADD CHECK(visibility_scope='Company' OR role='Viewer');
ALTER TABLE memberships ADD UNIQUE(user_id);
ALTER TABLE companies ADD COLUMN plan_kind text NOT NULL DEFAULT 'Unlimited';
ALTER TABLE companies ADD COLUMN seat_limit integer;
ALTER TABLE companies ADD COLUMN subscription_revision integer NOT NULL DEFAULT 1 CHECK(subscription_revision>0);
ALTER TABLE companies ADD CHECK ((plan_kind='Unlimited' AND seat_limit IS NULL) OR (plan_kind='Ten' AND seat_limit=10) OR (plan_kind='Twenty' AND seat_limit=20) OR (plan_kind='Fifty' AND seat_limit=50) OR (plan_kind='Hundred' AND seat_limit=100) OR (plan_kind='Custom' AND seat_limit>100));
-- CHECK must reject NULL numeric caps as well as unsupported plans.
ALTER TABLE companies ADD CHECK(plan_kind='Unlimited' OR seat_limit IS NOT NULL);
CREATE TABLE operator_owner (
 singleton boolean PRIMARY KEY DEFAULT true CHECK(singleton), user_id uuid NOT NULL UNIQUE REFERENCES users(id),
 issuer text NOT NULL CHECK(issuer ~ '^https://[^/?#@[:space:]]+(/[^?#[:space:]]*)?$' AND length(issuer)<=2000),
 subject text NOT NULL CHECK(length(subject) BETWEEN 1 AND 500 AND subject=btrim(subject)),
 created_at timestamptz NOT NULL DEFAULT now(), UNIQUE(issuer,subject)
);
CREATE FUNCTION guard_operator_owner() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Operator ownership is immutable'; END IF;
 PERFORM 1 FROM users WHERE id=NEW.user_id AND issuer=NEW.issuer AND identity_subject=NEW.subject AND active FOR UPDATE;
 IF NOT FOUND THEN RAISE EXCEPTION 'Operator requires matching active identity'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER operator_owner_guard BEFORE INSERT OR UPDATE OR DELETE ON operator_owner FOR EACH ROW EXECUTE FUNCTION guard_operator_owner();
CREATE TRIGGER operator_owner_no_truncate BEFORE TRUNCATE ON operator_owner FOR EACH STATEMENT EXECUTE FUNCTION guard_operator_owner();
CREATE FUNCTION guard_account_membership() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 PERFORM 1 FROM companies WHERE id=NEW.company_id FOR UPDATE;
 PERFORM 1 FROM users WHERE id=NEW.user_id FOR UPDATE;
 IF NEW.active AND NEW.role='Administrator' AND EXISTS(SELECT 1 FROM users WHERE id=NEW.user_id AND active) AND EXISTS(
  SELECT 1 FROM memberships m JOIN users u ON u.id=m.user_id WHERE m.company_id=NEW.company_id AND m.user_id<>NEW.user_id AND m.active AND u.active AND m.role='Administrator') THEN
  RAISE EXCEPTION 'Company already has an enabled Administrator' USING ERRCODE='23505';
 END IF; RETURN NEW;
END $$;
CREATE TRIGGER account_membership_guard BEFORE INSERT OR UPDATE ON memberships FOR EACH ROW EXECUTE FUNCTION guard_account_membership();
CREATE FUNCTION guard_account_identity() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE cid uuid; BEGIN
 IF EXISTS(SELECT 1 FROM operator_owner WHERE user_id=OLD.id) AND (NEW.issuer<>OLD.issuer OR NEW.identity_subject<>OLD.identity_subject) THEN RAISE EXCEPTION 'Operator identity is immutable'; END IF;
 IF NEW.active AND NOT OLD.active THEN
  SELECT company_id INTO cid FROM memberships WHERE user_id=NEW.id AND active AND role='Administrator';
  IF cid IS NOT NULL THEN
   PERFORM 1 FROM companies WHERE id=cid FOR UPDATE;
   IF EXISTS(SELECT 1 FROM memberships m JOIN users u ON u.id=m.user_id WHERE m.company_id=cid AND m.user_id<>NEW.id AND m.active AND u.active AND m.role='Administrator') THEN RAISE EXCEPTION 'Company already has an enabled Administrator' USING ERRCODE='23505'; END IF;
  END IF;
 END IF; RETURN NEW;
END $$;
CREATE TRIGGER account_identity_guard BEFORE UPDATE ON users FOR EACH ROW EXECUTE FUNCTION guard_account_identity();
CREATE TABLE account_invitations (
 id uuid PRIMARY KEY, company_id uuid NOT NULL REFERENCES companies(id), email text NOT NULL,
 role text NOT NULL CHECK(role IN ('Administrator','Editor','Viewer')), token_hash bytea NOT NULL UNIQUE CHECK(octet_length(token_hash)=32),
 created_at timestamptz NOT NULL DEFAULT now(), expires_at timestamptz NOT NULL,
 state text NOT NULL DEFAULT 'Pending' CHECK(state IN ('Pending','Accepted','Revoked')),
 accepted_user_id uuid REFERENCES users(id), revision integer NOT NULL DEFAULT 1 CHECK(revision>0),
 CHECK(expires_at=created_at+interval '7 days'), CHECK((state='Accepted')=(accepted_user_id IS NOT NULL))
);
CREATE INDEX account_invitations_pending ON account_invitations(company_id,expires_at) WHERE state='Pending';
CREATE TABLE account_sessions (
 id uuid PRIMARY KEY, user_id uuid NOT NULL REFERENCES users(id), created_at timestamptz NOT NULL DEFAULT now(),
 expires_at timestamptz NOT NULL, last_activity_at timestamptz NOT NULL DEFAULT now(), revoked_at timestamptz,
 CHECK(expires_at>created_at), CHECK(last_activity_at>=created_at)
);
CREATE INDEX account_sessions_presence ON account_sessions(user_id,last_activity_at) WHERE revoked_at IS NULL;
CREATE TABLE account_audit (
 id uuid PRIMARY KEY, actor_user_id uuid NOT NULL REFERENCES users(id), company_id uuid REFERENCES companies(id),
 action text NOT NULL, target_id uuid, occurred_at timestamptz NOT NULL DEFAULT now(), before_value jsonb, after_value jsonb
);
CREATE FUNCTION deny_account_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Account audit is immutable'; END $$;
CREATE TRIGGER account_audit_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON account_audit FOR EACH STATEMENT EXECUTE FUNCTION deny_account_audit_mutation();
