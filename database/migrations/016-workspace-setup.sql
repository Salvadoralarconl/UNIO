ALTER TABLE companies ADD COLUMN workspace_setup_created boolean NOT NULL DEFAULT false;
ALTER TABLE equipment_tickets ADD COLUMN is_workspace_setup boolean NOT NULL DEFAULT false;
ALTER TABLE equipment_tickets ADD COLUMN dismissed boolean NOT NULL DEFAULT false;
CREATE FUNCTION seed_workspace_setup(target_company uuid, target_user uuid) RETURNS void LANGUAGE plpgsql AS $$
DECLARE ticket uuid := gen_random_uuid(); actor text;
BEGIN
 SELECT display_name INTO actor FROM users u JOIN memberships m ON m.user_id=u.id WHERE m.company_id=target_company AND m.user_id=target_user AND m.active AND u.active AND m.role='Administrator';
 IF actor IS NULL THEN RETURN; END IF;
 UPDATE companies SET workspace_setup_created=true WHERE id=target_company AND NOT workspace_setup_created;
 IF NOT FOUND THEN RETURN; END IF;
 INSERT INTO equipment_tickets(id,company_id,title,state,type,description,creator_user_id,creator_name,assignment_mode,is_workspace_setup,checklist)
 VALUES(ticket,target_company,'Set up your workspace','Assigned','Support',
 'Welcome! Use this optional checklist to set up your workspace. Open Inventory → Configuration for departments, locations, employees, and asset categories. Employees receive equipment; assigned technicians are signed-in users who complete tickets. Use Asset type → Category → Brand → Model, for example Computer → Laptop → Asus → ProArt P16. Check off each step when you are ready. You can ignore or delete this ticket at any time.',
 target_user,actor,'Users',true,(SELECT jsonb_agg(jsonb_build_object('id',gen_random_uuid(),'text',step,'completed',false) ORDER BY ord) FROM unnest(ARRAY[
 'Inventory → Configuration → Departments: add your departments',
 'Inventory → Configuration → Locations: add your offices or sites',
 'Inventory → Configuration → Employees: add people who receive equipment (no login required)',
 'Inventory → Configuration → Asset types: set categories such as Computer → Laptop',
 'Inventory: add your first asset with its category, brand, model, and serial number',
 'Account settings: add signed-in users who will handle tickets',
 'Tickets: create your first ticket and choose its equipment recipient and assigned technicians'
 ]) WITH ORDINALITY s(step,ord)));
 INSERT INTO ticket_assignees(company_id,ticket_id,user_id) VALUES(target_company,ticket,target_user);
 INSERT INTO ticket_activity(company_id,ticket_id,id,actor_user_id,actor_name,action,detail) VALUES(target_company,ticket,gen_random_uuid(),target_user,actor,'Created','Optional first-time workspace setup');
END $$;
-- Existing companies with tickets are established; do not add onboarding noise.
UPDATE companies c SET workspace_setup_created=true WHERE EXISTS(SELECT 1 FROM equipment_tickets t WHERE t.company_id=c.id);
SELECT seed_workspace_setup(c.id,a.user_id) FROM companies c JOIN LATERAL(SELECT m.user_id FROM memberships m JOIN users u ON u.id=m.user_id WHERE m.company_id=c.id AND m.active AND u.active AND m.role='Administrator' ORDER BY m.user_id LIMIT 1)a ON true WHERE NOT c.workspace_setup_created;
INSERT INTO asset_fields(id,company_id,name,kind,help_text,sort_order)
 SELECT gen_random_uuid(),c.id,v.name,'shortText',v.help,v.ord FROM companies c CROSS JOIN(VALUES('Brand','Manufacturer, for example Asus',-2),('Model','Product model, for example ProArt P16',-1))v(name,help,ord)
 WHERE NOT EXISTS(SELECT 1 FROM asset_fields f WHERE f.company_id=c.id AND f.asset_type_id IS NULL AND lower(btrim(f.name))=lower(v.name));
