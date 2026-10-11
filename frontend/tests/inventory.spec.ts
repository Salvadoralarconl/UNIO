import {test,expect} from './session-fixture';
test.beforeEach(async({page})=>{await page.route('**/api/inventory/assets',r=>r.fulfill({json:[]}));await page.route('**/api/inventory/activity*',r=>r.fulfill({json:[]}));await page.route('**/api/inventory/documentation',r=>r.fulfill({json:{documentId:null,candidates:[]}}));});
test('inventory landing has categories and clear empty search states',async({page})=>{
 await page.goto('http://127.0.0.1:5174');await page.getByRole('tab',{name:'Inventory',exact:true}).click();
 await expect(page.getByRole('heading',{name:'Inventory',exact:true})).toBeVisible();await expect(page.getByText('No assets yet.',{exact:true})).toBeVisible();
 for(const name of ['Printers','Computers','Monitors','Network','Accessories'])await expect(page.getByRole('button',{name:new RegExp('^'+name+' 0 assets')})).toBeVisible();
 await page.getByRole('button',{name:/^Printers 0 assets/}).click();await expect(page.getByRole('button',{name:/^Printers 0 assets/})).toHaveAttribute('aria-pressed','true');
 await page.getByRole('searchbox',{name:'Search inventory'}).fill('TM-T88V');await expect(page.getByText('No matching assets.',{exact:true})).toBeVisible();await page.getByRole('button',{name:'Clear filters',exact:true}).click();await expect(page.getByRole('searchbox',{name:'Search inventory'})).toHaveValue('');await expect(page.getByText('No assets yet.',{exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Recent activity',exact:true}).click();await expect(page.getByRole('heading',{name:'Recent activity',exact:true})).toBeFocused();
 await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBeTruthy();
 await page.screenshot({path:'../artifacts/inventory-mobile.png',fullPage:true});
});
test('category filters do not move the assets table',async({page})=>{
 await page.goto('http://127.0.0.1:5174');await page.getByRole('tab',{name:'Inventory',exact:true}).click();const table=page.locator('.inventory-table-scroll'),before=await table.boundingBox();
 for(const name of ['Printers','Computers','Monitors','Network','Accessories']){await page.getByRole('button',{name:new RegExp('^'+name+' 0 assets')}).click();const after=await table.boundingBox();expect(after!.y).toBeCloseTo(before!.y,1);expect(after!.height).toBeCloseTo(before!.height,1);await page.getByRole('button',{name:'Clear filters',exact:true}).click();}
});

