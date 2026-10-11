-- No Administrator invitation flow is authorized. Reject existing ambiguous state.
ALTER TABLE account_invitations DROP CONSTRAINT account_invitations_role_check;
ALTER TABLE account_invitations ADD CONSTRAINT account_invitations_role_check CHECK(role IN ('Editor','Viewer'));
ALTER TABLE account_invitations ADD COLUMN bound_issuer text;
ALTER TABLE account_invitations ADD COLUMN bound_subject text;
ALTER TABLE account_invitations ADD CHECK((bound_issuer IS NULL)=(bound_subject IS NULL));
CREATE UNIQUE INDEX account_invitations_one_pending_email ON account_invitations(company_id,lower(email)) WHERE state='Pending';
CREATE UNIQUE INDEX account_invitations_one_pending_identity ON account_invitations(company_id,bound_issuer,bound_subject) WHERE state='Pending' AND bound_issuer IS NOT NULL;
