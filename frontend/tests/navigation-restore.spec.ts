import {test,expect} from './session-fixture';
test('Inventory enters Overview and refresh preserves the current section and module',async({page})=>{
 await page.goto('http://localhost:5174');
 await page.getByRole('tab',{name:'Inventory',exact:true}).click();
 await expect(page.getByRole('heading',{name:'Overview',exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Kits',exact:true}).click();
 await page.reload();
 await expect(page.getByRole('heading',{name:'Equipment kits',exact:true})).toBeVisible();
 await page.getByRole('tab',{name:'Tickets',exact:true}).click();
 await page.reload();
 await expect(page.getByRole('tab',{name:'Tickets',exact:true})).toHaveAttribute('aria-selected','true');
 await page.getByRole('tab',{name:'Inventory',exact:true}).click();
 await expect(page.getByRole('heading',{name:'Overview',exact:true})).toBeVisible();
});
