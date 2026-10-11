import {test,expect} from './session-fixture';
test('reports include directory records without assets and export chosen columns',async({page})=>{
 await page.route('**/api/inventory/assets',r=>r.fulfill({json:[{id:'a1',computer:'Work laptop',assetTag:'L-1',category:'Computer',status:'Assigned',employeeId:'p1',assignee:'Alex',departmentId:'d1',department:'Engineering',locationId:'l1',location:'Office'}]}));
 await page.route('**/api/directory/employees?**',r=>r.fulfill({json:{items:[{id:'p1',displayName:'Alex',departmentId:'d1',active:true},{id:'p2',displayName:'Sam',departmentId:'d1',active:true}],nextCursor:null}}));
 await page.route('**/api/directory/departments?**',r=>r.fulfill({json:{items:[{id:'d1',name:'Engineering',active:true},{id:'d2',name:'Sales',active:true}],nextCursor:null}}));
 await page.route('**/api/directory/locations?**',r=>r.fulfill({json:{items:[{id:'l1',name:'Office',active:true},{id:'l2',name:'Warehouse',active:true}],nextCursor:null}}));
 await page.goto('http://localhost:5174');await page.getByRole('tab',{name:'Inventory',exact:true}).click();await page.getByRole('button',{name:'Reports',exact:true}).click();
 const results=page.getByRole('region',{name:'Report results'});
 await expect(results).toContainText('Work laptop');
 async function choose(label:string,value:string){await page.getByRole('combobox',{name:label,exact:true}).click();await page.getByRole('option',{name:value,exact:true}).click();}
 await choose('Include','Unassigned');await expect(results).toContainText('No matching results');await choose('Include','Assigned');await expect(results).toContainText('Work laptop');await choose('Report on','People');await choose('Include','Without assets');
 await expect(results).toContainText('Sam');await expect(results).not.toContainText('Alex');
 await page.getByRole('checkbox',{name:'Email',exact:true}).uncheck();await expect(results.getByRole('columnheader',{name:'Email',exact:true})).toHaveCount(0);
 const download=page.waitForEvent('download');await page.getByRole('button',{name:'Export CSV'}).click();expect((await download).suggestedFilename()).toBe('inventory-people.csv');
 await choose('Report on','Locations');await choose('Include','Without assets');await expect(results).toContainText('Warehouse');await expect(results).not.toContainText('Office');
 await choose('Report on','Departments');await choose('Include','Without assets');await expect(results).toContainText('Sales');await expect(results).not.toContainText('Engineering');
});

