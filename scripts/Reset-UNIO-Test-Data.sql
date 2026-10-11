BEGIN;
CREATE TEMP TABLE reset_company AS SELECT id FROM companies WHERE id='0972c761-87a0-43c4-93d1-be98ecec612a' AND name='UNIO-Test';
DO $$ BEGIN IF (SELECT count(*) FROM reset_company)<>1 THEN RAISE EXCEPTION 'Test company mismatch'; END IF; END $$;
SELECT id FROM companies WHERE id IN(SELECT id FROM reset_company) FOR UPDATE;
UPDATE memberships SET employee_id=NULL,department_id=NULL WHERE company_id IN(SELECT id FROM reset_company);
UPDATE documents SET replaced_by=NULL WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM ticket_attachments WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM ticket_asset_reservations WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM ticket_asset_links WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM ticket_assignees WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM ticket_comments WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM ticket_activity WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM ticket_document_links WHERE company_id IN(SELECT id FROM reset_company);
ALTER TABLE inventory_audit DISABLE TRIGGER inventory_audit_immutable;
DELETE FROM inventory_audit WHERE company_id IN(SELECT id FROM reset_company);
ALTER TABLE inventory_audit ENABLE TRIGGER inventory_audit_immutable;
DELETE FROM equipment_request_assets WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM equipment_tickets WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM equipment_requests WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM ticket_templates WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM ticket_settings WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM directory_mapping_review WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM inventory_activity WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM inventory_assets WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM equipment_kits WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM employees WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM inventory_departments WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM locations WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM asset_fields WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM asset_types WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM inventory_documentation WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM passages WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM document_files WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM document_folders WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM documents WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM library_folders WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM owned_files WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM conversation_shares WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM conversations WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM chat_projects WHERE company_id IN(SELECT id FROM reset_company);
DELETE FROM pinned_chat_folders WHERE company_id IN(SELECT id FROM reset_company);
COMMIT;
SELECT 'assets',count(*) FROM inventory_assets WHERE company_id='0972c761-87a0-43c4-93d1-be98ecec612a'
UNION ALL SELECT 'employees',count(*) FROM employees WHERE company_id='0972c761-87a0-43c4-93d1-be98ecec612a'
UNION ALL SELECT 'tickets',count(*) FROM equipment_tickets WHERE company_id='0972c761-87a0-43c4-93d1-be98ecec612a'
UNION ALL SELECT 'memberships',count(*) FROM memberships WHERE company_id='0972c761-87a0-43c4-93d1-be98ecec612a';

