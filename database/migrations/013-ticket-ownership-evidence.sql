-- Historical request links alone do not prove that the current revision belongs to that ticket.
-- Retain ownership only when an attributable ticket inventory event proves that exact revision.
UPDATE ticket_asset_links x SET owned_revision=NULL
WHERE x.owned_revision IS NOT NULL AND NOT x.completed
AND NOT EXISTS(SELECT 1 FROM inventory_audit a WHERE a.company_id=x.company_id
 AND a.resource_type='asset' AND a.resource_id=x.asset_id AND a.ticket_id=x.ticket_id
 AND coalesce(a.after_snapshot->>'revision',a.after_snapshot->>'Revision')=x.owned_revision::text);
