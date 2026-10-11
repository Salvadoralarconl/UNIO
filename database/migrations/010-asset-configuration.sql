CREATE TABLE asset_types (
 id uuid PRIMARY KEY, company_id uuid NOT NULL REFERENCES companies(id), code text NOT NULL,
 name text NOT NULL CHECK(length(btrim(name)) BETWEEN 1 AND 100), icon text NOT NULL,
 active boolean NOT NULL DEFAULT true, sort_order integer NOT NULL DEFAULT 0, revision integer NOT NULL DEFAULT 1 CHECK(revision>0),
 UNIQUE(company_id,id), UNIQUE(company_id,code)
);
CREATE UNIQUE INDEX asset_types_company_name ON asset_types(company_id,lower(btrim(name)));
CREATE TABLE asset_fields (
 id uuid PRIMARY KEY, company_id uuid NOT NULL REFERENCES companies(id), asset_type_id uuid,
 name text NOT NULL CHECK(length(btrim(name)) BETWEEN 1 AND 100), kind text NOT NULL CHECK(kind IN ('shortText','longText','number','date','boolean','choice')),
 help_text text CHECK(length(help_text)<=500), required boolean NOT NULL DEFAULT false, active boolean NOT NULL DEFAULT true,
 sort_order integer NOT NULL DEFAULT 0, choices jsonb NOT NULL DEFAULT '[]' CHECK(jsonb_typeof(choices)='array'), revision integer NOT NULL DEFAULT 1 CHECK(revision>0),
 UNIQUE(company_id,id), FOREIGN KEY(company_id,asset_type_id) REFERENCES asset_types(company_id,id)
);
CREATE UNIQUE INDEX asset_fields_company_scope_name ON asset_fields(company_id,asset_type_id,lower(btrim(name))) NULLS NOT DISTINCT;
INSERT INTO asset_types(id,company_id,code,name,icon,sort_order)
 SELECT gen_random_uuid(),c.id,v.code,v.code,v.icon,v.ord FROM companies c CROSS JOIN
 (VALUES ('Computer','Laptop',0),('Printer','Printer',1),('Monitor','Monitor',2),('Network','Router',3),('Accessory','Package',4)) v(code,icon,ord);
ALTER TABLE inventory_assets ADD COLUMN asset_type_id uuid, ADD COLUMN custom_fields jsonb NOT NULL DEFAULT '{}' CHECK(jsonb_typeof(custom_fields)='object' AND octet_length(custom_fields::text)<=65536);
UPDATE inventory_assets a SET asset_type_id=t.id FROM asset_types t WHERE t.company_id=a.company_id AND t.code=a.category;
ALTER TABLE inventory_assets DROP CONSTRAINT inventory_assets_category_check;
ALTER TABLE inventory_assets ADD FOREIGN KEY(company_id,asset_type_id) REFERENCES asset_types(company_id,id), ADD FOREIGN KEY(company_id,category) REFERENCES asset_types(company_id,code);
UPDATE equipment_kits k SET lines=(SELECT COALESCE(jsonb_agg(CASE WHEN t.id IS NULL THEN e.line ELSE e.line || jsonb_build_object('assetTypeId',t.id) END ORDER BY e.ord),'[]'::jsonb)
 FROM jsonb_array_elements(k.lines) WITH ORDINALITY e(line,ord) LEFT JOIN asset_types t ON t.company_id=k.company_id AND t.code=e.line->>'category');
-- Request lines are immutable snapshots and intentionally never rewritten.

