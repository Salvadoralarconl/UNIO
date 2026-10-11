import {test,expect} from './session-fixture';
test('successful department creation clears validation until the next submit',async({page})=>{
 await page.route('**/api/directory/departments',r=>r.fulfill({json:{id:'new-department',name:'Regression department',color:'#3b82f6',active:true,revision:1}}));
 await page.goto('http://127.0.0.1:5174');
 await page.getByRole('tab',{name:'Inventory',exact:true}).click();
 await page.getByRole('button',{name:'Configuration',exact:true}).click();
 const name=page.getByRole('textbox',{name:'Department name',exact:true});
 const add=page.getByRole('button',{name:'Add department',exact:true});
 await add.click();
 await expect(name).toHaveAttribute('aria-invalid','true');
 await name.fill('Regression department');
 await add.click();
 await expect(name).toHaveValue('');
 expect(await name.evaluate(el=>el.matches(':user-invalid'))).toBe(false);
 await expect(name).not.toHaveAttribute('aria-invalid','true');
 await add.click();
 await expect(name).toHaveAttribute('aria-invalid','true');
});
test('employee rows fill the directory and align their controls',async({page})=>{await page.route('**/api/directory/employees?*',r=>r.fulfill({json:{items:[{id:'e1',displayName:'Demo Person',email:'demo@example.invalid',active:true,departmentId:null,revision:1}],nextCursor:null}}));await page.goto('http://127.0.0.1:5174');await page.getByRole('tab',{name:'Inventory',exact:true}).click();await page.getByRole('button',{name:'Configuration',exact:true}).click();await expect(page.getByRole('tabpanel',{name:'Departments',exact:true})).toBeVisible();await expect(page.getByRole('tabpanel',{name:'Employees',exact:true})).not.toBeVisible();await page.getByRole('tab',{name:'Employees',exact:true}).click();await expect(page.getByRole('tabpanel',{name:'Departments',exact:true})).not.toBeVisible();const search=page.getByRole('searchbox',{name:'Search configuration',exact:true});await expect(search).toHaveCSS('background-repeat','no-repeat');expect(await search.evaluate(el=>getComputedStyle(el).backgroundImage)).toContain('data:image/svg+xml');const row=page.locator('.directory-employee-line.directory-row');await expect(row).toBeVisible();const container=row.locator('..');const r=(await row.boundingBox())!,c=(await container.boundingBox())!;expect(Math.abs(r.width-c.width)).toBeLessThan(2);const controls=[row.getByRole('textbox',{name:'Demo Person name',exact:true}),row.getByRole('textbox',{name:'Demo Person email',exact:true}),row.getByRole('combobox',{name:'Employee department',exact:true}),row.getByRole('button',{name:'Save employee',exact:true})];const boxes=await Promise.all(controls.map(control=>control.boundingBox()));for(const box of boxes)expect(Math.abs(box!.y-boxes[0]!.y)).toBeLessThan(2);await expect(row.getByRole('checkbox')).toBeVisible();});

test('add employee highlights and focuses a missing name',async({page})=>{await page.goto('http://127.0.0.1:5174');await page.getByRole('tab',{name:'Inventory',exact:true}).click();await page.getByRole('button',{name:'Configuration',exact:true}).click();await page.getByRole('tab',{name:'Employees',exact:true}).click();const add=page.getByRole('button',{name:'Add employee',exact:true});await expect(add).toBeEnabled();await expect(add).toHaveCSS('background-color','rgb(17, 17, 17)');await add.click();const name=page.getByRole('textbox',{name:'Employee name',exact:true});await expect(name).toHaveAttribute('aria-invalid','true');await expect(name).toBeFocused();await expect(name).toHaveCSS('outline-color','rgb(196, 59, 59)');});
