BEGIN;
CREATE TEMP TABLE demo_company AS SELECT id FROM public.companies WHERE id='0972c761-87a0-43c4-93d1-be98ecec612a' AND name='UNIO-Test';
DO $$ BEGIN IF (SELECT count(*) FROM demo_company)<>1 THEN RAISE EXCEPTION 'Test company mismatch'; END IF; END $$;
CREATE TEMP TABLE demo_departments AS SELECT md5('unio-demo-department-'||i)::uuid id,'Demo - '||(ARRAY['Engineering','Finance','People','Operations','Sales','Marketing','Support','Legal','Facilities','Product','Security','Research'])[i] name,i FROM generate_series(1,12) i;
INSERT INTO public.inventory_departments(company_id,id,name,normalized_name,color) SELECT c.id,d.id,d.name,lower(d.name),(ARRAY['#3b82f6','#22c55e','#f59e0b','#a855f7'])[1+(d.i-1)%4] FROM demo_company c CROSS JOIN demo_departments d ON CONFLICT DO NOTHING;
CREATE TEMP TABLE demo_locations AS SELECT md5('unio-demo-location-'||i)::uuid id,'Demo - '||(ARRAY['Headquarters','North office','South office','Warehouse','Remote','Repair lab'])[1+(i-1)%6]||' / Area '||i name,i FROM generate_series(1,18) i;
INSERT INTO public.locations(company_id,id,name) SELECT c.id,l.id,l.name FROM demo_company c CROSS JOIN demo_locations l ON CONFLICT DO NOTHING;
CREATE TEMP TABLE demo_people AS SELECT md5('unio-demo-person-'||i)::uuid id,'Demo Person '||lpad(i::text,2,'0') name,i FROM generate_series(1,60) i;
INSERT INTO public.employees(company_id,id,display_name,email,department_id) SELECT c.id,p.id,p.name,'demo.person.'||p.i||'@example.invalid',d.id FROM demo_company c CROSS JOIN demo_people p JOIN demo_departments d ON d.i=1+(p.i-1)%12 ON CONFLICT DO NOTHING;
INSERT INTO public.inventory_assets(company_id,id,asset_tag,normalized_tag,computer,serial_number,category,custom_category,status,department_id,department,location_id,location,employee_id,assignee,asset_type_id,updated_at)
SELECT c.id,md5('unio-demo-asset-'||g.n)::uuid,'DEMO-'||lpad(g.n::text,4,'0'),'demo-'||lpad(g.n::text,4,'0'),'Demo '||t.name||' '||lpad(g.n::text,3,'0'),'DEMO-SN-'||lpad(g.n::text,5,'0'),t.code,
CASE t.code WHEN 'Computer' THEN 'Laptop' WHEN 'Printer' THEN 'Office printer' WHEN 'Monitor' THEN '27-inch display' WHEN 'Network' THEN 'Access point' ELSE 'Docking station' END,
(ARRAY['Available','Available','Assigned','Assigned','In repair','Retired'])[1+(g.n-1)%6],d.id,d.name,l.id,l.name,CASE WHEN (g.n-1)%6 IN (2,3) THEN p.id END,CASE WHEN (g.n-1)%6 IN (2,3) THEN p.name END,t.id,now()-(g.n||' hours')::interval
FROM demo_company c CROSS JOIN generate_series(1,180) g(n) JOIN public.asset_types t ON t.company_id=c.id AND t.code=(ARRAY['Computer','Printer','Monitor','Network','Accessory'])[1+(g.n-1)%5]
JOIN demo_departments d ON d.i=1+(g.n-1)%12 JOIN demo_locations l ON l.i=1+(g.n-1)%18 JOIN demo_people p ON p.i=1+(g.n-1)%60 ON CONFLICT DO NOTHING;
INSERT INTO public.inventory_activity(company_id,id,asset_id,computer,kind) SELECT company_id,md5('unio-demo-activity-'||id)::uuid,id,computer,'Added' FROM public.inventory_assets WHERE company_id=(SELECT id FROM demo_company) AND asset_tag LIKE 'DEMO-%' ON CONFLICT DO NOTHING;
INSERT INTO public.equipment_kits(company_id,id,name,department_id,lines)
SELECT c.id,md5('unio-demo-kit-'||g.n)::uuid,'Demo - '||(ARRAY['New starter','Remote workspace','Field service','Office essentials','Meeting room'])[1+(g.n-1)%5]||' '||lpad(g.n::text,2,'0'),d.id,
(SELECT jsonb_agg(jsonb_build_object('assetTypeId',t.id,'category',t.code,'customCategory',null,'quantity',CASE WHEN t.code='Monitor' THEN 2 ELSE 1 END) ORDER BY t.sort_order) FROM public.asset_types t WHERE t.company_id=c.id AND t.active)
FROM demo_company c CROSS JOIN generate_series(1,30) g(n) JOIN demo_departments d ON d.i=1+(g.n-1)%12 ON CONFLICT DO NOTHING;
INSERT INTO public.library_folders(company_id,id,name,parent_id) SELECT c.id,md5('unio-demo-folder-'||g.n)::uuid,'Demo - '||(ARRAY['Getting started','IT guides','Operations','Policies','Training','Equipment'])[1+(g.n-1)%6]||' '||g.n,CASE WHEN g.n>6 THEN md5('unio-demo-folder-'||(1+(g.n-1)%6))::uuid END FROM demo_company c CROSS JOIN generate_series(1,18) g(n) ON CONFLICT DO NOTHING;
INSERT INTO public.documents(company_id,id,title,version,approval,indexing,filename,content_hash,canonical_text,rich_content)
SELECT c.id,md5('unio-demo-document-'||g.n)::uuid,'Demo - '||(ARRAY['Laptop setup guide','Printer troubleshooting','Remote working checklist','Equipment handover','Office network guide','Security basics'])[1+(g.n-1)%6]||' '||lpad(g.n::text,2,'0'),'1.0','draft','pending','wiki/'||md5('unio-demo-document-'||g.n)::uuid||'.md',md5('demo-page-'||g.n),
'DEMO DATA - for layout testing only. '||repeat('Preparation: review your equipment. Follow the sample checklist and record the outcome. Escalation: contact your demo support team. ',40),
NULL
FROM demo_company c CROSS JOIN generate_series(1,48) g(n) ON CONFLICT DO NOTHING;
INSERT INTO public.document_folders(company_id,document_id,folder_id) SELECT c.id,md5('unio-demo-document-'||g.n)::uuid,md5('unio-demo-folder-'||(1+(g.n-1)%18))::uuid FROM demo_company c CROSS JOIN generate_series(1,48) g(n) ON CONFLICT DO NOTHING;
COMMIT;
