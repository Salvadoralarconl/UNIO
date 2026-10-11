BEGIN;
CREATE TEMP TABLE demo_company AS SELECT id FROM companies WHERE id='0972c761-87a0-43c4-93d1-be98ecec612a' AND name='UNIO-Test';
DO $$ BEGIN IF (SELECT count(*) FROM demo_company)<>1 THEN RAISE EXCEPTION 'Test company mismatch'; END IF; END $$;
CREATE TEMP TABLE demo_guides AS SELECT md5('unio-ticket-guide-'||v.kind)::uuid id,v.* FROM (VALUES
('Onboarding','Demo - Employee onboarding checklist',E'# Employee onboarding checklist\n\nDEMO GUIDE - sample content for testing.\n\n1. Confirm the intended employee, start date, department, and location.\n2. Collect the reserved equipment using the brand, model, and asset tags shown in the ticket.\n3. Configure the employee account, standard applications, and access.\n4. Test the laptop, monitors, network connection, and printer.\n5. Record the handover in a comment and mark the ticket Complete. Inventory then records the equipment as Assigned to the intended employee.'),
('Equipment','Demo - Equipment collection and handover',E'# Equipment collection and handover\n\nDEMO GUIDE - sample content for testing.\n\nMatch each asset tag against the equipment shown in the ticket. Check its condition and accessories. Deliver it to the intended recipient and location. Record the handover in comments. Complete the ticket to finalize the inventory assignment.'),
('Repair','Demo - Device repair checklist',E'# Device repair checklist\n\nDEMO GUIDE - sample content for testing.\n\nRecord the reported fault and asset tag. Send the device for repair, document work and replacement parts, and test it before return. Confirm the intended recipient before completing the ticket.')) v(kind,title,body);
INSERT INTO documents(id,company_id,title,version,approval,indexing,filename,content_hash,canonical_text)
SELECT g.id,c.id,g.title,'1.0','draft','pending','wiki/'||g.id||'.md',md5(g.body),g.body FROM demo_company c CROSS JOIN demo_guides g ON CONFLICT DO NOTHING;
INSERT INTO ticket_document_links(company_id,ticket_id,id,document_id,document_title)
SELECT t.company_id,t.id,md5('unio-ticket-doc-'||t.id||g.id)::uuid,g.id,g.title
FROM equipment_tickets t JOIN demo_company c ON c.id=t.company_id JOIN demo_guides g ON g.kind=t.type
WHERE t.title LIKE 'Demo%' ON CONFLICT DO NOTHING;
INSERT INTO ticket_settings(company_id,onboarding_document_id) SELECT c.id,g.id FROM demo_company c CROSS JOIN demo_guides g WHERE g.kind='Onboarding'
ON CONFLICT(company_id) DO UPDATE SET onboarding_document_id=coalesce(ticket_settings.onboarding_document_id,excluded.onboarding_document_id);
INSERT INTO document_folders(company_id,document_id,folder_id)
SELECT c.id,g.id,f.id FROM demo_company c CROSS JOIN demo_guides g
JOIN LATERAL(SELECT id FROM library_folders WHERE company_id=c.id AND name LIKE 'Demo - Getting started%' ORDER BY id LIMIT 1) f ON true ON CONFLICT DO NOTHING;
COMMIT;

