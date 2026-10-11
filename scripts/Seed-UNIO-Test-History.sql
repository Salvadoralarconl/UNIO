BEGIN;
DO $$ BEGIN IF NOT EXISTS(SELECT 1 FROM public.companies WHERE id='0972c761-87a0-43c4-93d1-be98ecec612a' AND name='UNIO-Test') THEN RAISE EXCEPTION 'Test company mismatch'; END IF; END $$;
WITH devices AS (
 SELECT a.*, (SELECT user_id FROM public.memberships WHERE company_id=a.company_id ORDER BY user_id LIMIT 1) actor,
 jsonb_build_object('computer',computer,'asset_tag',asset_tag,'category',category,'status','Available','location','Demo - Warehouse','assignee',NULL) initial
 FROM public.inventory_assets a WHERE company_id='0972c761-87a0-43c4-93d1-be98ecec612a' AND asset_tag LIKE 'DEMO-%'
), stages AS (
 SELECT *, initial || jsonb_build_object('status','Assigned','assignee','Demo Person 01') assigned,
 initial || jsonb_build_object('status','Assigned','assignee','Demo Person 01','location','Demo - North office') moved,
 initial || jsonb_build_object('status','In repair','location','Demo - Repair lab') repair
 FROM devices
), events AS (
 SELECT s.*, e.* FROM stages s CROSS JOIN LATERAL (VALUES
 (1,'Demo · Asset created',NULL::jsonb,initial),
 (2,'Demo · Assigned',initial,assigned),
 (3,'Demo · Location changed',assigned,moved),
 (4,'Demo · Sent for repair',moved,repair),
 (5,'Demo · Repair completed / current assignment',repair,jsonb_build_object('computer',computer,'asset_tag',asset_tag,'category',category,'status',status,'location',location,'assignee',assignee))
 ) e(step,label,before_data,after_data)
)
INSERT INTO public.inventory_audit(id,company_id,actor_user_id,action,resource_type,resource_id,before_snapshot,after_snapshot,occurred_at)
SELECT md5('unio-demo-history-'||id||'-'||step)::uuid,company_id,actor,label,'asset',id,before_data,after_data,
 timestamptz '2026-09-01 09:00:00+00'+(step*5)*interval '1 day'
FROM events ON CONFLICT DO NOTHING;
COMMIT;
