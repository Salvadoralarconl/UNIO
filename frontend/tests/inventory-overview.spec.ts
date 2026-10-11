import {test,expect} from './session-fixture';
const assets=[['Alpha','MIS','Assigned','Computer'],['Beta','Accounting','Available','Computer'],['Printer','MIS','In repair','Printer'],['Old PC',null,'Retired','Computer']].map(([computer,department,status,category],i)=>({id:String(i),computer,department,status,category,assetTag:'AS'+i,softwareStatus:'Not checked',lastSeen:null,updatedAt:'2026-10-08T12:00:00Z'}));
async function setup(page:any,rows=assets){await page.route('**/api/inventory/assets',r=>r.fulfill({json:rows}));await page.route('**/api/inventory/activity*',r=>r.fulfill({json:[]}));await page.route('**/api/inventory/documentation',r=>r.fulfill({json:{documentId:'existing',candidates:[]}}));await page.goto('http://127.0.0.1:5174');await page.getByRole('tab',{name:'Inventory',exact:true}).click();await page.getByRole('button',{name:'Overview',exact:true}).click();}
test('overview uses live counts, departments and status with working drill-down',async({page})=>{await setup(page);await expect(page.getByRole('region',{name:'Inventory overview',exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'Total assets: 4',exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'Assigned: 1',exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'Department MIS: 2 assets',exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'Department Not specified: 1 assets',exact:true})).toBeVisible();await expect(page.getByRole('region',{name:'Needs attention'})).toContainText('Alpha');await expect(page.getByRole('region',{name:'Needs attention'})).not.toContainText('Old PC');await page.screenshot({path:'../artifacts/inventory-overview-desktop.png',fullPage:true});await page.getByRole('button',{name:'Assigned: 1',exact:true}).click();await expect(page.getByRole('button',{name:'Open asset Alpha',exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'Open asset Beta',exact:true})).toHaveCount(0);await page.getByRole('button',{name:'Overview',exact:true}).click();await page.getByRole('button',{name:'Department MIS: 2 assets',exact:true}).click();await expect(page.getByRole('button',{name:/Open asset /})).toHaveCount(2);});
test('empty overview works at mobile width',async({page})=>{await page.setViewportSize({width:390,height:844});await setup(page,[]);await expect(page.getByRole('button',{name:'Total assets: 0',exact:true})).toBeVisible();await expect(page.getByText('No assets registered yet.',{exact:true}).first()).toBeVisible();expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:'../artifacts/inventory-overview-mobile.png',fullPage:true});});
test('asset failure shows unavailable instead of false zero counts',async({page})=>{await setup(page);await page.route('**/api/inventory/assets',r=>r.fulfill({status:503,json:{error:'Inventory unavailable.'}}));await page.getByRole('button',{name:'Inventory',exact:true}).click();await page.getByRole('button',{name:'Overview',exact:true}).click();await expect(page.getByRole('alert')).toContainText('Inventory unavailable.');await expect(page.getByRole('button',{name:'Total assets: 0',exact:true})).toHaveCount(0);});

test('activity outage does not hide asset counts and retry restores an asset outage',async({page})=>{await setup(page);await page.route('**/api/inventory/activity*',r=>r.fulfill({status:503,json:{error:'Activity unavailable.'}}));await page.getByRole('button',{name:'Inventory',exact:true}).click();await page.getByRole('button',{name:'Overview',exact:true}).click();await expect(page.getByRole('button',{name:'Total assets: 4',exact:true})).toBeVisible();await expect(page.getByText('Activity unavailable.',{exact:true})).toBeVisible();let failed=true;await page.route('**/api/inventory/assets',r=>failed?r.fulfill({status:503,json:{error:'Inventory unavailable.'}}):r.fulfill({json:assets}));await page.getByRole('button',{name:'Inventory',exact:true}).click();await page.getByRole('button',{name:'Overview',exact:true}).click();await expect(page.getByRole('alert')).toBeVisible();failed=false;await page.getByRole('button',{name:'Retry',exact:true}).click();await expect(page.getByRole('button',{name:'Total assets: 4',exact:true})).toBeVisible();});
test('status legend and missing-department drill-down filter the right assets',async({page})=>{await setup(page);await page.getByRole('button',{name:'View retired assets: 1',exact:true}).click();await expect(page.getByRole('button',{name:'Open asset Old PC',exact:true})).toBeVisible();await expect(page.getByRole('button',{name:/Open asset /})).toHaveCount(1);await page.getByRole('button',{name:'Overview',exact:true}).click();await page.getByRole('button',{name:'Department Not specified: 1 assets',exact:true}).click();await expect(page.getByRole('button',{name:'Open asset Old PC',exact:true})).toBeVisible();await expect(page.getByRole('button',{name:/Open asset /})).toHaveCount(1);});


test('overview panels align with toolbar and toolbar search filters counts',async({page})=>{
 await setup(page);
 const toolbar=page.locator('.workspace>.workspace-toolbar');
 const panels=page.locator('.overview-summaries');
 const top=await toolbar.boundingBox(), content=await panels.boundingBox();
 expect(Math.abs(top!.x-content!.x)).toBeLessThan(1);
 expect(Math.abs(top!.width-content!.width)).toBeLessThan(1);
 const workspace=await page.locator('.suite-shell .workspace').boundingBox();
 const side=top!.x-workspace!.x;
 expect(Math.abs(top!.y-workspace!.y-side)).toBeLessThan(1);
 expect(Math.abs(content!.y-(top!.y+top!.height)-side)).toBeLessThan(1);
 const last=await page.locator('.overview-attention').boundingBox();
 expect(Math.abs(workspace!.y+workspace!.height-(last!.y+last!.height)-side)).toBeLessThan(1);
 await toolbar.getByRole('searchbox',{name:'Search overview'}).fill('Alpha');
 await expect(page.getByRole('button',{name:'Total assets: 1',exact:true})).toBeVisible();
});

test('empty messages are centered inside overview panels and inventory table',async({page})=>{
 await setup(page,[]);
 for(const label of ['Assets by department','Needs attention']){
  const panel=page.getByRole('region',{name:label});
  const message=panel.locator('.overview-empty');
  expect(await message.evaluate(el=>getComputedStyle(el).justifyContent)).toBe('center');
  expect(await message.evaluate(el=>getComputedStyle(el).alignItems)).toBe('center');
 }
 await page.getByRole('button',{name:'Inventory',exact:true}).click();
 const cell=await page.locator('td:has(.inventory-empty)').boundingBox();
 const message=await page.locator('.inventory-empty').boundingBox();
 expect(Math.abs(cell!.x+cell!.width/2-message!.x-message!.width/2)).toBeLessThan(1);
});

test('Inventory navigation places Requests third and Reports last',async({page})=>{
 await setup(page,[]);
 const labels=await page.locator('.icon-rail nav button').evaluateAll(buttons=>buttons.map(button=>button.getAttribute('aria-label')));
 expect(labels.slice(0,5)).toEqual(['Overview','Inventory','Requests','Kits','Reports']);
});
