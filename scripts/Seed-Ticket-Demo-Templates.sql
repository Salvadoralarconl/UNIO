BEGIN;
CREATE TEMP TABLE demo_company AS SELECT id FROM companies WHERE id='0972c761-87a0-43c4-93d1-be98ecec612a' AND name='UNIO-Test';
DO $$ BEGIN IF (SELECT count(*) FROM demo_company)<>1 THEN RAISE EXCEPTION 'Test company mismatch'; END IF; END $$;
INSERT INTO ticket_templates(company_id,id,name,type,instructions,kit_id,assignment_mode,assignee_ids,document_ids,checklist)
SELECT c.id,md5('unio-demo-onboarding-template')::uuid,'Demo - New employee onboarding','Onboarding',
'DEMO TEMPLATE - Confirm the employee, due date, department, and location. Collect the equipment reserved for this ticket, prepare the accounts and applications, and complete the checklist before handover.',
(SELECT id FROM equipment_kits WHERE company_id=c.id AND name LIKE 'Demo - New starter%' AND active ORDER BY name LIMIT 1),'Users',
coalesce((SELECT jsonb_agg(user_id) FROM (SELECT m.user_id FROM memberships m JOIN users u ON u.id=m.user_id WHERE m.company_id=c.id AND m.active AND u.active AND m.role='Administrator' ORDER BY m.user_id LIMIT 1) s),'[]'::jsonb),
jsonb_build_array(md5('unio-ticket-guide-Onboarding')::uuid,md5('unio-ticket-guide-Equipment')::uuid),
'["Confirm employee, department, location, and start date","Collect reserved equipment by asset tag","Create the employee account","Assign department access groups","Configure email and distribution lists","Configure standard applications","Set up the laptop and monitors","Test network and printer access","Update the employee directory","Record handover details in comments","Confirm the employee received the equipment","Complete the ticket"]'::jsonb
FROM demo_company c ON CONFLICT DO NOTHING;
INSERT INTO equipment_tickets(company_id,id,title,type,state,description,due_date,assignment_mode,creator_user_id,creator_name,approver_user_id,approver_name,approved_at,checklist)
SELECT c.id,md5('unio-demo-connected-ticket-'||v.n)::uuid,v.title,'Support',v.state,'DEMO TICKET - sample work and comments for reviewing the ticket layout.',current_date+v.n,'All',u.user_id,u.display_name,CASE WHEN v.state<>'Pending' THEN u.user_id END,CASE WHEN v.state<>'Pending' THEN u.display_name END,CASE WHEN v.state<>'Pending' THEN now() END,
jsonb_build_array(jsonb_build_object('id',md5('unio-demo-ticket-step-'||v.n)::uuid,'text','Confirm the equipment is working','completed',v.state='Complete','completedBy',CASE WHEN v.state='Complete' THEN u.display_name END,'completedAt',CASE WHEN v.state='Complete' THEN now() END))
FROM demo_company c CROSS JOIN (VALUES(1,'Demo - Confirm workstation access','Approved'),(2,'Demo - Test conference room display','In Progress'),(3,'Demo - Standard applications verified','Complete'),(4,'Demo - Duplicate support request','Canceled')) v(n,title,state)
JOIN LATERAL(SELECT m.user_id,u.display_name FROM memberships m JOIN users u ON u.id=m.user_id WHERE m.company_id=c.id AND m.active AND u.active AND m.role='Administrator' ORDER BY m.user_id LIMIT 1) u ON true ON CONFLICT DO NOTHING;
INSERT INTO ticket_document_links(company_id,ticket_id,id,document_id,document_title)
SELECT t.company_id,t.id,md5('unio-demo-connected-guide-'||t.id)::uuid,d.id,d.title FROM equipment_tickets t JOIN demo_company c ON c.id=t.company_id JOIN documents d ON d.company_id=c.id AND d.id=md5('unio-ticket-guide-Equipment')::uuid WHERE t.id IN(SELECT md5('unio-demo-connected-ticket-'||n)::uuid FROM generate_series(1,4)n) ON CONFLICT DO NOTHING;
INSERT INTO ticket_activity(company_id,ticket_id,id,actor_user_id,actor_name,action,detail)
SELECT t.company_id,t.id,md5('unio-demo-connected-created-'||t.id)::uuid,t.creator_user_id,t.creator_name,'Created','Demo example added' FROM equipment_tickets t JOIN demo_company c ON c.id=t.company_id WHERE t.id IN(SELECT md5('unio-demo-connected-ticket-'||n)::uuid FROM generate_series(1,4)n) ON CONFLICT DO NOTHING;
INSERT INTO ticket_comments(company_id,ticket_id,id,author_user_id,author_name,body)
SELECT t.company_id,t.id,md5('unio-demo-connected-comment-'||t.id)::uuid,t.creator_user_id,t.creator_name,'DEMO COMMENT - Equipment checked and helpful documentation reviewed. Record your work and any handover notes here.' FROM equipment_tickets t JOIN demo_company c ON c.id=t.company_id WHERE t.id IN(SELECT md5('unio-demo-connected-ticket-'||n)::uuid FROM generate_series(1,4)n) ON CONFLICT DO NOTHING;
INSERT INTO ticket_asset_links(company_id,ticket_id,asset_id,outcome,baseline_revision,baseline_status,baseline_employee_id,baseline_assignee,baseline_location_id)
SELECT t.company_id,t.id,a.id,'Reference',a.revision,a.status,a.employee_id,a.assignee,a.location_id FROM equipment_tickets t JOIN demo_company c ON c.id=t.company_id
JOIN LATERAL(SELECT * FROM inventory_assets WHERE company_id=t.company_id AND asset_tag LIKE 'DEMO-%' ORDER BY asset_tag LIMIT 1) a ON true
WHERE t.id IN(SELECT md5('unio-demo-connected-ticket-'||n)::uuid FROM generate_series(1,4)n) ON CONFLICT DO NOTHING;
COMMIT;
