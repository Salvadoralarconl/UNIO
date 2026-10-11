BEGIN;
DO $$ DECLARE r record; ln jsonb; chosen uuid[]; candidate uuid; BEGIN
FOR r IN SELECT * FROM public.equipment_requests WHERE company_id='0972c761-87a0-43c4-93d1-be98ecec612a' AND id IN (SELECT md5('unio-demo-request-'||n)::uuid FROM generate_series(1,9) n WHERE n%3=0) AND state='Approved' FOR UPDATE LOOP
 FOR ln IN SELECT * FROM jsonb_array_elements(r.lines) LOOP
 SELECT array_agg(id) INTO chosen FROM (SELECT id FROM public.inventory_assets WHERE company_id=r.company_id AND status='Available' AND employee_id IS NULL AND asset_tag LIKE 'DEMO-%' AND category=ln->>'category' ORDER BY asset_tag LIMIT (ln->>'quantity')::integer FOR UPDATE) stock;
 IF coalesce(array_length(chosen,1),0)<>(ln->>'quantity')::integer THEN RAISE EXCEPTION 'Insufficient demo stock'; END IF;
 FOREACH candidate IN ARRAY chosen LOOP
 UPDATE public.inventory_assets SET employee_id=r.employee_id,assignee=(SELECT display_name FROM public.employees WHERE id=r.employee_id AND company_id=r.company_id),department_id=r.department_id,department=(SELECT name FROM public.inventory_departments WHERE id=r.department_id AND company_id=r.company_id),status='Assigned',revision=revision+1,updated_at=now() WHERE id=candidate AND company_id=r.company_id;
 INSERT INTO public.equipment_request_assets(company_id,request_id,asset_id) VALUES(r.company_id,r.id,candidate);
 INSERT INTO public.inventory_activity(company_id,id,asset_id,computer,kind) SELECT r.company_id,gen_random_uuid(),id,computer,'Assigned' FROM public.inventory_assets WHERE id=candidate AND company_id=r.company_id;
 END LOOP; END LOOP;
 UPDATE public.equipment_requests SET state='Ready',revision=revision+1 WHERE id=r.id AND company_id=r.company_id;
 UPDATE public.equipment_tickets SET state='Ready',revision=revision+1,updated_at=now() WHERE id=r.ticket_id AND company_id=r.company_id;
END LOOP; END $$;
COMMIT;
SELECT count(*) assigned_assets FROM public.equipment_request_assets WHERE company_id='0972c761-87a0-43c4-93d1-be98ecec612a';
