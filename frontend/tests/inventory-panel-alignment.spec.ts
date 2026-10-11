import {test,expect} from './session-fixture';
test('Inventory sections align first panels',async({page})=>{
 await page.route('**/api/equipment/**',r=>r.fulfill({json:[]}));
 await page.goto('http://127.0.0.1:5174/');await page.getByRole('tab',{name:'Inventory',exact:true}).click();
 let target=0;
 for(const name of ['Inventory','Overview','Kits','Requests','Configuration','Settings']){
  await page.locator('.icon-rail').getByRole('button',{name,exact:true}).click();
  const heading=page.locator('.inventory-heading,.overview-heading,.equipment-heading').first();await expect(heading).toBeVisible();
  const panel=heading.locator('xpath=following-sibling::*[1]');await expect(panel).toBeVisible();
  const top=(await panel.boundingBox())!.y;console.log(name,top);
  if(name==='Inventory')target=top;else expect(Math.abs(top-target)).toBeLessThan(1);
 }
});
