ALTER TABLE inventory_assets ADD COLUMN employee_id uuid;
CREATE INDEX inventory_assets_company_employee ON inventory_assets(company_id,employee_id);
