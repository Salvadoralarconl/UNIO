import {test,expect} from './session-fixture';
const request={id:'onboard-1',title:'Welcome Sam',purpose:'Onboarding',employeeId:'p1',departmentId:null,locationId:'l1',kitId:'k1',kitName:'Starter kit',neededDate:'2026-10-20',notes:null,state:'Pending',ticketId:'t1',revision:1,createdAt:'2026-10-10T12:00:00Z',lines:[{category:'Computer',quantity:1,customCategory:null}],assetIds:[] as string[]};
test('onboarding assigns a chosen available asset and shares readiness with Reports and Tickets',async({page})=>{
 let row={...request},assigned=false;
 await page.route('**/api/equipment/requests',r=>r.fulfill({json:[row,{...request,id:'normal',title:'Normal request',purpose:'Equipment'}]}));
 await page.route('**/api/equipment/kits',r=>r.fulfill({json:[]}));
 await page.route('**/api/directory/employees?**',r=>r.fulfill({json:{items:[{id:'p1',displayName:'Sam',active:true}],nextCursor:null}}));
 await page.route('**/api/directory/locations?**',r=>r.fulfill({json:{items:[{id:'l1',name:'Office',active:true}],nextCursor:null}}));
 await page.route('**/api/inventory/assets',r=>r.fulfill({json:[{id:'a1',assetTag:'L-1',computer:'Chosen laptop',category:'Computer',status:assigned?'Assigned':'Available',employeeId:assigned?'p1':null,assignee:assigned?'Sam':null,locationId:'l1'}]}));
 await page.route('**/api/equipment/requests/onboard-1/actions',async r=>{const body=r.request().postDataJSON();expect(body.assetIds).toEqual(['a1']);assigned=true;row={...row,state:'Ready',revision:2,assetIds:['a1']};await r.fulfill({json:row});});
 await page.goto('http://localhost:5174');await page.getByRole('tab',{name:'Inventory',exact:true}).click();await page.getByRole('button',{name:'Requests',exact:true}).click();await expect(page.getByRole('button',{name:'Onboarding',exact:true})).toHaveCount(0);await page.getByRole('combobox',{name:'Request type filter',exact:true}).click();await page.getByRole('option',{name:'Onboarding',exact:true}).click();
 const workspace=page.getByRole('region',{name:'Requests workspace'});await expect(workspace).toContainText('Welcome Sam');await expect(workspace).not.toContainText('Normal request');
 await page.getByRole('button',{name:'Choose specific assets'}).click();await page.getByRole('combobox',{name:'Computer 1',exact:true}).click();await page.getByRole('option',{name:'Chosen laptop L-1'}).click();await page.getByRole('button',{name:'Assign selected assets'}).click();await expect(page.getByRole('button',{name:'Complete handover'})).toBeVisible();await expect(workspace).toContainText('Chosen laptop');
 await page.getByRole('button',{name:'Reports',exact:true}).click();await page.getByRole('combobox',{name:'Report on',exact:true}).click();await page.getByRole('option',{name:'Onboarding',exact:true}).click();await page.getByRole('combobox',{name:'Include',exact:true}).click();await page.getByRole('option',{name:'Ready',exact:true}).click();
 const results=page.getByRole('region',{name:'Report results'});await expect(results).toContainText('Welcome Sam');await expect(results).not.toContainText('Normal request');await results.getByRole('button',{name:'Welcome Sam'}).click();await expect(page.getByRole('button',{name:'Complete handover'})).toBeVisible();
 await page.getByRole('tab',{name:'Tickets',exact:true}).click();await expect(page.getByRole('region',{name:'Tickets workspace'})).toContainText('Welcome Sam');
});

