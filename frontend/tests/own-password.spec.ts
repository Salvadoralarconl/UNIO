import {test,expect} from './session-fixture';

test('Companyless Operator can change own password with exact bytes and fresh login',async({page})=>{

 await page.route('**/api/session',r=>r.fulfill({json:{authenticationMode:'Local',user:{id:'owner',displayName:'Owner'},activeCompany:null,memberships:[],role:null,isOperator:true,capabilities:{}}}));

 let count=0;await page.route('**/api/auth/password',r=>{count++;expect(r.request().headers()['x-csrf-token']).toBe('isolated-ui-token');expect(r.request().postDataJSON()).toEqual({currentPassword:' old password with spaces ',password:' new password with spaces ',confirmPassword:' new password with spaces '});return r.fulfill({json:{freshLoginRequired:true}});});

 await page.goto('http://127.0.0.1:5174');await page.getByRole('button',{name:'Account settings',exact:true}).click();await page.getByRole('button',{name:'Change password',exact:true}).click();

 await page.getByLabel('Current password',{exact:true}).fill(' old password with spaces ');await page.getByLabel('New password',{exact:true}).fill(' new password with spaces ');await page.getByLabel('Confirm new password',{exact:true}).fill(' new password with spaces ');await page.getByLabel('Confirm new password',{exact:true}).press('Enter');

 await expect(page.getByRole('heading',{name:'Sign in to your workspace'})).toBeVisible();expect(count).toBe(1);await expect(page.getByRole('button',{name:'Sign out',exact:true})).toHaveCount(0);

});



test('Own password validation and wrong current password retain signed in form',async({page})=>{

 let count=0;await page.route('**/api/auth/password',r=>{count++;return r.fulfill({status:400,json:{error:'Current password is incorrect.'}});});

 await page.goto('http://127.0.0.1:5174');await page.getByRole('button',{name:'Account settings',exact:true}).click();await page.getByRole('button',{name:'Change password',exact:true}).click();

 await page.getByLabel('Current password',{exact:true}).fill('wrong current password');await page.getByLabel('New password',{exact:true}).fill('short');await page.getByLabel('Confirm new password',{exact:true}).fill('short');await page.getByRole('button',{name:'Save password',exact:true}).click();expect(count).toBe(0);

 await page.getByLabel('New password',{exact:true}).fill('new password long enough');await page.getByLabel('Confirm new password',{exact:true}).fill('mismatched long password');await page.getByRole('button',{name:'Save password',exact:true}).click();await expect(page.getByRole('alert')).toHaveText('Passwords must match.');expect(count).toBe(0);

 await page.getByLabel('Confirm new password',{exact:true}).fill('new password long enough');await page.getByRole('button',{name:'Save password',exact:true}).click();await expect(page.getByRole('alert')).toHaveText('Current password is incorrect.');expect(count).toBe(1);

 const boxes=await page.locator('.login-fields input').evaluateAll(inputs=>inputs.map(x=>({left:x.getBoundingClientRect().left,width:x.getBoundingClientRect().width})));expect(new Set(boxes.map(x=>x.left)).size).toBe(1);expect(new Set(boxes.map(x=>x.width)).size).toBe(1);await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);

});

test('Pending password change submits once and stale success cannot replace new session',async({page})=>{

 let release!:()=>void,count=0;const pending=new Promise<void>(r=>release=r);await page.route('**/api/auth/password',async r=>{count++;await pending;await r.fulfill({json:{freshLoginRequired:true}}).catch(()=>{});});

 await page.goto('http://127.0.0.1:5174');await page.getByRole('button',{name:'Account settings',exact:true}).click();await page.getByRole('button',{name:'Change password',exact:true}).click();await page.getByLabel('Current password',{exact:true}).fill('old password long enough');await page.getByLabel('New password',{exact:true}).fill('new password long enough');await page.getByLabel('Confirm new password',{exact:true}).fill('new password long enough');await page.getByLabel('Confirm new password',{exact:true}).press('Enter');await expect.poll(()=>count).toBe(1);await expect(page.getByRole('button',{name:'Saving�'})).toBeDisabled();await page.evaluate(()=>document.querySelector('form')!.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true})));expect(count).toBe(1);

 await page.evaluate(()=>window.dispatchEvent(new Event('clary-session-expired')));await expect(page.getByRole('heading',{name:'Sign in to your workspace'})).toBeVisible();await page.getByRole('button',{name:'Retry session'}).click();await page.getByRole('button',{name:'Account settings',exact:true}).click();await expect(page.getByRole('button',{name:'Sign out',exact:true})).toBeVisible();release();await expect(page.getByRole('button',{name:'Sign out',exact:true})).toBeVisible();await expect(page.getByLabel('Current password',{exact:true})).toHaveCount(0);

});

