import {test,expect} from './session-fixture';
test('placeholder modules match Tickets and inventory headings match Documentation',async({page})=>{
 await page.goto('http://127.0.0.1:5174/');
 for(const name of ['Tickets','Vault','Remote Support']){
  await page.getByRole('tab',{name,exact:true}).click();
  await expect(page.getByRole('heading',{name,exact:true})).toBeVisible();
  await expect(page.locator('.module-placeholder')).toContainText(`${name} is not available yet.`);
  await expect(page.getByRole('tab',{name,exact:true})).toHaveCSS('background-color','rgb(255, 255, 255)');
  await expect(page.locator('.icon-rail nav button')).toHaveCount(1);
 }
 await page.getByRole('tab',{name:'Documentation',exact:true}).click();
 const style=await page.getByRole('heading',{name:'Documentation',exact:true}).evaluate(el=>{const s=getComputedStyle(el);return {size:s.fontSize,weight:s.fontWeight,spacing:s.letterSpacing,height:s.lineHeight};});
 await page.getByRole('tab',{name:'Inventory',exact:true}).click();
 for(const name of ['Overview','Inventory','Kits','Requests','Configuration','Settings']){
  await page.locator('.icon-rail').getByRole('button',{name,exact:true}).click();
  const heading=page.locator('.inventory-heading h1,.overview-heading h1,.equipment-heading h1').first();
  await expect(heading).toBeVisible();
  await expect(heading).toHaveCSS('font-size',style.size);await expect(heading).toHaveCSS('font-weight',style.weight);await expect(heading).toHaveCSS('letter-spacing',style.spacing);await expect(heading).toHaveCSS('line-height',style.height);
 }
});

