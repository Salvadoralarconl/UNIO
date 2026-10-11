ALTER TABLE equipment_requests ADD COLUMN purpose text NOT NULL DEFAULT 'Equipment' CHECK(purpose IN ('Equipment','Onboarding'));
ALTER TABLE equipment_requests ADD COLUMN location_id uuid;
ALTER TABLE equipment_requests ADD FOREIGN KEY(company_id,location_id) REFERENCES locations(company_id,id);
CREATE INDEX equipment_requests_company_purpose_date ON equipment_requests(company_id,purpose,needed_date,id);
