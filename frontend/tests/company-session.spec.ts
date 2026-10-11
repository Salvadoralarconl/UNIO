import {test,expect,capabilitiesFor} from './session-fixture';

import type {Page} from '@playwright/test';

test('AccountSettingsContainsKeyboardFocusAndRestoresOpener',async({page})=>{
 await page.goto('http://127.0.0.1:5174');
 await page.getByRole('tab',{name:'Inventory',exact:true}).click();await page.getByRole('button',{name:'Inventory',exact:true}).click();
 await page.getByRole('button',{name:'Settings',exact:true}).click();
 const opener=page.getByRole('button',{name:'Account settings',exact:true});
 await opener.click();
 const dialog=page.getByRole('dialog',{name:'Account settings',exact:true});
 const close=dialog.getByRole('button',{name:'Close account settings',exact:true});
 const signOut=dialog.getByRole('button',{name:'Sign out',exact:true});
 await expect(close).toBeFocused();
 await expect(page.getByRole('button',{name:'Sign out',exact:true})).toHaveCount(1);
 await close.press('Shift+Tab');await expect(signOut).toBeFocused();
 await signOut.press('Tab');await expect(close).toBeFocused();
 await expect(page.locator('[inert]')).toHaveCount(1);
 await close.press('Escape');await expect(dialog).toHaveCount(0);await expect(opener).toBeFocused();
 await opener.click();await dialog.getByRole('button',{name:'Close account settings',exact:true}).click();await expect(opener).toBeFocused();
});
async function setup(page:Page,role='Administrator',delayed=false,assignedOnly=false){

 let company='A';let release:()=>void=()=>{};const pending=new Promise<void>(r=>release=r);

 await page.route('**/api/**',async route=>{const path=new URL(route.request().url()).pathname;

 if(path==='/api/session')return route.fulfill({json:{user:{id:'u',displayName:'Test user',email:null},activeCompany:{id:company,name:'Company '+company,revision:1},isOperator:false,capabilities:capabilitiesFor(role,assignedOnly),memberships:['A','B'].map(id=>({companyId:id,name:'Company '+id,role})),role}});

 if(path==='/api/session/antiforgery')return route.fulfill({json:{token:'test-token'}});

 if(path==='/api/session/company'){expect(route.request().headers()['x-csrf-token']).toBe('test-token');company=route.request().postDataJSON().companyId;return route.fulfill({json:{companyId:company,role}});}

 if(path==='/api/inventory/assets'){const requested=company;if(delayed&&requested==='A')await pending;return route.fulfill({json:[{id:requested,assetTag:requested,computer:'Asset '+requested,category:'Computer',status:'Available',revision:1,updatedAt:null,softwareStatus:'Not checked',lastSeen:null}]});}

 if(path==='/api/inventory/documentation')return route.fulfill({json:{documentId:null,candidates:[]}});

 return route.fallback();});return {release};

}

test('LoginFailureKeepsRecoveryVisible',async({page})=>{await page.route('**/api/session',r=>r.fulfill({status:401}));await page.goto('http://127.0.0.1:5174');await expect(page.getByRole('heading',{name:'Sign in to your workspace'})).toBeVisible();await page.getByRole('button',{name:'Retry session'}).click();await expect(page.getByRole('button',{name:'Sign in',exact:true})).toBeVisible();await expect(page.getByRole('tab')).toHaveCount(0);});

test('CompanySwitchClearsVisibleData',async({page})=>{await setup(page);await page.goto('http://127.0.0.1:5174');await page.getByLabel('Issue description').fill('Private A draft');await page.evaluate(()=>{localStorage.setItem('clary-private-A','private');sessionStorage.setItem('clary-private-A','private');});await page.getByRole('tab',{name:'Inventory',exact:true}).click();await page.getByRole('button',{name:'Inventory',exact:true}).click();await expect(page.getByRole('button',{name:'Open asset Asset A'})).toBeVisible();await page.getByRole('button',{name:'Account settings',exact:true}).click();await page.getByRole('combobox',{name:'Company',exact:true}).click();await page.getByRole('option',{name:'Company B',exact:true}).click();await page.getByRole('tab',{name:'Inventory',exact:true}).click();await page.getByRole('button',{name:'Inventory',exact:true}).click();await expect(page.getByRole('button',{name:'Open asset Asset B'})).toBeVisible();await expect(page.getByText('Asset A',{exact:true})).toHaveCount(0);await page.getByRole('tab',{name:'Brie',exact:true}).click();await expect(page.getByLabel('Issue description')).toHaveValue('');expect(await page.evaluate(()=>[localStorage.getItem('clary-private-A'),sessionStorage.getItem('clary-private-A')])).toEqual([null,null]);});

test('DelayedOldCompanyResponseIsIgnored',async({page})=>{const {release}=await setup(page,'Administrator',true);await page.goto('http://127.0.0.1:5174');await page.getByRole('tab',{name:'Inventory',exact:true}).click();await page.getByRole('button',{name:'Inventory',exact:true}).click();await expect(page.getByText('Loading inventory…')).toBeVisible();await page.getByRole('button',{name:'Account settings',exact:true}).click();await page.getByRole('combobox',{name:'Company',exact:true}).click();await page.getByRole('option',{name:'Company B',exact:true}).click();await page.getByRole('tab',{name:'Inventory',exact:true}).click();await page.getByRole('button',{name:'Inventory',exact:true}).click();await expect(page.getByRole('button',{name:'Open asset Asset B'})).toBeVisible();release();await expect(page.getByRole('button',{name:'Open asset Asset A'})).toHaveCount(0);});

test('AssignedOnlyViewerCannotSeeAdminControls',async({page})=>{await setup(page,'Viewer',false,true);await page.goto('http://127.0.0.1:5174');await page.getByRole('tab',{name:'Inventory',exact:true}).click();await page.getByRole('button',{name:'Inventory',exact:true}).click();await expect(page.getByRole('button',{name:'Settings',exact:true})).toHaveCount(0);await expect(page.getByRole('button',{name:'Add asset',exact:true})).toHaveCount(0);await expect(page.getByRole('tab',{name:'Documentation',exact:true})).toHaveCount(0);});



test('ExpiredSessionClearsCompanyData',async({page})=>{await setup(page);await page.goto('http://127.0.0.1:5174');await page.getByRole('tab',{name:'Inventory',exact:true}).click();await page.getByRole('button',{name:'Inventory',exact:true}).click();await expect(page.getByRole('button',{name:'Open asset Asset A'})).toBeVisible();await page.route('**/api/inventory/assets',r=>r.fulfill({status:401}));await page.getByRole('tab',{name:'Brie',exact:true}).click();await page.getByRole('tab',{name:'Inventory',exact:true}).click();await expect(page.getByRole('heading',{name:'Sign in to your workspace'})).toBeVisible();await expect(page.getByText('Asset A',{exact:true})).toHaveCount(0);});

test('DelayedOldCompanyBodyIsRejected',async({page})=>{await setup(page);await page.goto('http://127.0.0.1:5174');await expect(page.getByLabel('Issue description')).toBeVisible();const result=await page.evaluate(async()=>{const path='/src/companyApi.ts';const api=await import(path);const realFetch=window.fetch;let release!:(value:unknown)=>void;const body=new Promise(resolve=>release=resolve);window.fetch=async()=>{const response=new Response('{}');response.json=()=>body;return response;};try{const response=await api.authenticatedFetch('/api/delayed-body');const pending=response.json().then(()=> 'accepted',(e:Error)=>e.name);api.clearCompanyScope();release({private:'A'});return await pending;}finally{window.fetch=realFetch;}});expect(result).toBe('AbortError');});

test('ViewerCannotSeeMutationControls',async({page})=>{await setup(page,'Viewer');await page.goto('http://127.0.0.1:5174');await page.getByRole('tab',{name:'Inventory',exact:true}).click();await page.getByRole('button',{name:'Inventory',exact:true}).click();await expect(page.getByRole('button',{name:'Add asset',exact:true})).toHaveCount(0);await page.getByRole('button',{name:'Open asset Asset A'}).click();await expect(page.getByRole('button',{name:'Edit asset',exact:true})).toHaveCount(0);await page.getByRole('button',{name:'Close asset details'}).click();await page.getByRole('tab',{name:'Documentation',exact:true}).click();await expect(page.getByRole('button',{name:'Upload files',exact:true})).toHaveCount(0);await expect(page.getByRole('button',{name:'New page',exact:true})).toHaveCount(0);});



test('LogoutClearsWorkspaceAndUsesCsrf',async({page})=>{await setup(page);await page.route('**/api/session/logout',route=>{expect(route.request().headers()['x-csrf-token']).toBe('test-token');return route.fulfill({status:204});});await page.goto('http://127.0.0.1:5174');await page.getByLabel('Issue description').fill('Private draft');await page.getByRole('button',{name:'Account settings',exact:true}).click();await page.getByRole('button',{name:'Sign out',exact:true}).click();await expect(page.getByRole('heading',{name:'Sign in to your workspace'})).toBeVisible();await expect(page.getByRole('tab')).toHaveCount(0);});



test('CompanyPickerKeyboardActiveOptionIsVisible',async({page})=>{await setup(page);await page.goto('http://127.0.0.1:5174');await page.getByRole('button',{name:'Account settings',exact:true}).click();const picker=page.getByRole('combobox',{name:'Company',exact:true});await picker.press('ArrowDown');const option=page.getByRole('option',{name:'Company B',exact:true});await expect(option).toHaveCSS('background-color','rgb(238, 237, 230)');});

