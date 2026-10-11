BEGIN;
CREATE TEMP TABLE demo_company AS SELECT id FROM companies WHERE id='0972c761-87a0-43c4-93d1-be98ecec612a' AND name='UNIO-Test';
DO $$ BEGIN IF (SELECT count(*) FROM demo_company)<>1 THEN RAISE EXCEPTION 'Test company mismatch'; END IF; END $$;
INSERT INTO asset_fields(id,company_id,name,kind,help_text,sort_order)
SELECT md5('unio-demo-field-'||v.name)::uuid,c.id,v.name,'shortText','Sample inventory identification',v.ord
FROM demo_company c CROSS JOIN (VALUES ('Brand',0),('Model',1)) v(name,ord)
WHERE NOT EXISTS(SELECT 1 FROM asset_fields f WHERE f.company_id=c.id AND f.asset_type_id IS NULL AND lower(f.name)=lower(v.name));
CREATE TEMP TABLE demo_brand_changes AS
SELECT a.id,a.company_id,to_jsonb(a) before_snapshot,a.custom_fields || jsonb_build_object(b.id::text,
CASE a.category WHEN 'Computer' THEN 'Lenovo' WHEN 'Monitor' THEN 'Dell' WHEN 'Printer' THEN 'HP' WHEN 'Network' THEN 'Ubiquiti' ELSE 'Lenovo' END,
m.id::text,CASE a.category WHEN 'Computer' THEN CASE WHEN right(a.asset_tag,1)::int%2=0 THEN 'ThinkPad P1' ELSE 'ThinkPad T14' END WHEN 'Monitor' THEN 'UltraSharp U2723QE' WHEN 'Printer' THEN 'LaserJet Pro M404dn' WHEN 'Network' THEN 'UniFi U6 Pro' ELSE 'ThinkPad Universal USB-C Dock' END) fields
FROM inventory_assets a JOIN demo_company c ON c.id=a.company_id
JOIN asset_fields b ON b.company_id=a.company_id AND b.asset_type_id IS NULL AND lower(b.name)='brand'
JOIN asset_fields m ON m.company_id=a.company_id AND m.asset_type_id IS NULL AND lower(m.name)='model'
WHERE a.asset_tag ~ '^DEMO-[0-9]+$';
UPDATE inventory_assets a SET custom_fields=d.fields,revision=revision+1,updated_at=now() FROM demo_brand_changes d WHERE a.company_id=d.company_id AND a.id=d.id AND a.custom_fields IS DISTINCT FROM d.fields;
INSERT INTO inventory_audit(id,company_id,actor_user_id,actor_name_snapshot,action,resource_type,resource_id,before_snapshot,after_snapshot)
SELECT gen_random_uuid(),a.company_id,u.user_id,u.display_name,'Updated','asset',a.id,d.before_snapshot,to_jsonb(a)
FROM inventory_assets a JOIN demo_brand_changes d ON d.id=a.id AND d.company_id=a.company_id
JOIN LATERAL(SELECT m.user_id,u.display_name FROM memberships m JOIN users u ON u.id=m.user_id WHERE m.company_id=a.company_id AND m.active AND u.active AND m.role='Administrator' ORDER BY m.user_id LIMIT 1) u ON true
WHERE a.custom_fields IS DISTINCT FROM d.before_snapshot->'custom_fields';
-- Only the matching revision changed by this demo metadata operation is carried forward.
UPDATE ticket_asset_links x SET baseline_revision=a.revision,owned_revision=CASE WHEN x.owned_revision IS NULL THEN NULL ELSE a.revision END
FROM inventory_assets a JOIN demo_brand_changes d ON d.id=a.id AND d.company_id=a.company_id
WHERE x.company_id=a.company_id AND x.asset_id=a.id AND x.baseline_revision=(d.before_snapshot->>'revision')::int
AND (x.owned_revision IS NULL OR x.owned_revision=(d.before_snapshot->>'revision')::int);
COMMIT;
