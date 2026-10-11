-- Local identities attach to immutable existing user IDs; legacy profiles are not inferred.
CREATE TABLE local_credentials (
 user_id uuid PRIMARY KEY REFERENCES users(id),
 username text, normalized_username text COLLATE "C" UNIQUE,
 first_name text, last_name text, password_hash text,
 revision integer NOT NULL DEFAULT 1 CHECK(revision>0),
 failed_attempts integer NOT NULL DEFAULT 0 CHECK(failed_attempts BETWEEN 0 AND 5),
 locked_until timestamptz,
 CHECK((username IS NULL)=(normalized_username IS NULL)),
 CHECK(username IS NULL OR (username ~ '^[A-Za-z0-9._-]{3,64}$' AND normalized_username=lower(username COLLATE "C"))),
 CHECK(first_name IS NULL OR (length(first_name) BETWEEN 1 AND 100 AND length(btrim(first_name))>0)),
 CHECK(last_name IS NULL OR (length(last_name) BETWEEN 1 AND 100 AND length(btrim(last_name))>0)),
 CHECK(password_hash IS NULL OR (username IS NOT NULL AND first_name IS NOT NULL AND last_name IS NOT NULL))
);
CREATE TABLE local_credential_tokens (
 id uuid PRIMARY KEY, user_id uuid NOT NULL REFERENCES local_credentials(user_id),
 purpose text NOT NULL CHECK(purpose IN ('Setup','Reset')),
 token_hash bytea NOT NULL UNIQUE CHECK(octet_length(token_hash)=32),
 credential_revision integer NOT NULL CHECK(credential_revision>0),
 created_at timestamptz NOT NULL, expires_at timestamptz NOT NULL,
 consumed_at timestamptz, revoked_at timestamptz,
 CHECK(expires_at=created_at+interval '30 minutes'),
 CHECK(consumed_at IS NULL OR consumed_at>=created_at),
 CHECK(revoked_at IS NULL OR revoked_at>=created_at)
);
CREATE INDEX local_credential_tokens_pending ON local_credential_tokens(user_id) WHERE consumed_at IS NULL AND revoked_at IS NULL;
-- Anonymous attempts must not be attributed to the account being attacked.
CREATE TABLE local_auth_security_events (
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 actor_user_id uuid REFERENCES users(id),
 event text NOT NULL CHECK(event IN ('LoginFailed','LockedOut','Throttled','TokenRejected')),
 occurred_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX local_auth_security_events_time ON local_auth_security_events(occurred_at);
CREATE FUNCTION bound_local_auth_security_events() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 DELETE FROM local_auth_security_events WHERE id <= NEW.id-10000;
 RETURN NEW;
END $$;
CREATE TRIGGER local_auth_security_events_bound AFTER INSERT ON local_auth_security_events FOR EACH ROW EXECUTE FUNCTION bound_local_auth_security_events();
