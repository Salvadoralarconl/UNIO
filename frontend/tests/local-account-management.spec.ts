import {test,expect} from './session-fixture';

test('Local manager creates disabled profile without a nonexistent setup link',async({page})=>{

 await page.route('**/api/company/accounts',r=>{if(r.request().method()==='GET')return r.fulfill({json:{items:[],nextCursor:null}});expect(r.request().postDataJSON()).toEqual({firstName:'Grace',lastName:'Hopper',email:'',role:'Viewer',enabled:false});return r.fulfill({json:{account:{id:'new'},setup:null}});});

 await page.goto('http://127.0.0.1:5174');await page.getByRole('tab',{name:'Inventory',exact:true}).click();await page.getByRole('button',{name:'Settings',exact:true}).click();await page.getByRole('button',{name:'Add account',exact:true}).click();await page.getByLabel('First name',{exact:true}).fill('Grace');await page.getByLabel('Last name',{exact:true}).fill('Hopper');await page.getByLabel('Enabled',{exact:true}).uncheck();await page.getByRole('button',{name:'Save account',exact:true}).click();await expect(page.getByRole('form',{name:'New account'})).toHaveCount(0);await expect(page.getByLabel('Credential link',{exact:true})).toHaveCount(0);

});

import type {Page} from '@playwright/test';

const company={id:'local-company',name:'Local Studio',revision:1,active:true,plan:'Ten',limit:10,seats:{enabled:1,pending:0,limit:10,remaining:9,overLimit:false},onlineUsers:0,lastActivity:null,needsAdministrator:false};

async function localOperator(page:Page){await page.route('**/api/session',r=>r.fulfill({json:{authenticationMode:'Local',user:{id:'owner',displayName:'Operator',email:null},activeCompany:null,memberships:[],role:null,isOperator:true,capabilities:{}}}));await page.route('**/api/operator/**',r=>{const path=new URL(r.request().url()).pathname;if(r.request().method()==='GET'&&path==='/api/operator/companies')return r.fulfill({json:{items:[company],nextCursor:null}});if(r.request().method()==='GET'&&path.endsWith('/accounts'))return r.fulfill({json:{items:[],nextCursor:null}});if(r.request().method()==='GET'&&path.endsWith('/invitations'))return r.fulfill({json:{items:[],nextCursor:null}});if(r.request().method()==='GET'&&path.endsWith('/local-company'))return r.fulfill({json:{company,subscription:{kind:'Ten',limit:10,revision:1,seats:company.seats}}});return r.abort('blockedbyclient');});}

test('Local Operator atomic company creation shows Administrator link only in its company',async({page})=>{

 await localOperator(page);await page.route('**/api/operator/companies',r=>{if(r.request().method()==='GET')return r.fallback();expect(r.request().postDataJSON()).toEqual({name:'Local Studio',subscription:{kind:'Ten'},administrator:{firstName:'Alex',lastName:'Admin',email:'',role:'Administrator',enabled:true}});return r.fulfill({json:{company,administrator:{account:{id:'admin'},setup:{userId:'admin',expiresAt:'2099-01-01',link:'/setup-account#token=private-admin'}}}});});

 await page.goto('http://127.0.0.1:5174/operator');await page.getByRole('button',{name:'Create company',exact:true}).click();await page.getByLabel('Company name',{exact:true}).fill('Local Studio');await page.getByLabel('First name',{exact:true}).fill('Alex');await page.getByLabel('Last name',{exact:true}).fill('Admin');await page.getByRole('button',{name:'Save new company'}).click();await expect(page.getByLabel('Credential link',{exact:true})).toHaveValue(/private-admin/);await page.getByRole('button',{name:'All companies'}).click();await expect(page.getByLabel('Credential link',{exact:true})).toHaveCount(0);

});

const profile={username:'evan',firstName:'Evan',lastName:'Editor',passwordSet:true};

async function openLocalManager(page:Page){await page.route('**/api/company/accounts**',r=>r.request().method()==='GET'?r.fulfill({json:{items:[{id:'mock-user',displayName:'Test user',role:'Administrator',enabled:true,revision:1,localProfile:profile},{id:'admin',displayName:'Other Admin',role:'Administrator',enabled:true,revision:2,localProfile:profile},{id:'editor',displayName:'Evan Editor',role:'Editor',enabled:true,revision:7,email:null,online:false,lastActivity:null,localProfile:profile},{id:'pending',displayName:'Pending Viewer',role:'Viewer',enabled:true,revision:3,email:null,online:false,lastActivity:null,localProfile:{...profile,username:null,passwordSet:false}},{id:'disabled',displayName:'Disabled Viewer',role:'Viewer',enabled:false,revision:4,localProfile:null}],nextCursor:null}}):r.fallback());await page.goto('http://127.0.0.1:5174');await page.getByRole('tab',{name:'Inventory',exact:true}).click();await page.getByRole('button',{name:'Settings',exact:true}).click();await expect(page.getByRole('heading',{name:'Company accounts'})).toBeVisible();}

test('Local manager protects self Administrator disabled targets and issues once reset link using revision',async({page})=>{

 await openLocalManager(page);for(const name of ['Test user','Other Admin','Disabled Viewer'])await expect(page.getByRole('button',{name:'Issue credential link for '+name,exact:true})).toHaveCount(0);await expect(page.getByRole('row').filter({hasText:'Pending Viewer'})).toContainText('Enabled · password pending');await page.route('**/api/company/accounts/editor/credential-link',r=>{expect(r.request().postDataJSON()).toEqual({revision:7});expect(r.request().headers()['x-csrf-token']).toBe('isolated-ui-token');return r.fulfill({json:{userId:'editor',expiresAt:'2099-01-01',link:'/setup-account#token=private-reset'}});});await page.getByRole('button',{name:'Issue credential link for Evan Editor',exact:true}).click();await expect(page.getByLabel('Credential link',{exact:true})).toHaveValue(/private-reset/);await page.getByRole('region',{name:'Company accounts',exact:true}).evaluate(el=>el.scrollIntoView({block:'start'}));await page.screenshot({path:'../artifacts/local-manager-desktop.png',fullPage:true});await page.setViewportSize({width:390,height:844});await page.getByRole('region',{name:'Company accounts',exact:true}).evaluate(el=>el.scrollIntoView({block:'start'}));expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:'../artifacts/local-manager-mobile.png',fullPage:true});await page.getByRole('button',{name:'Dismiss link',exact:true}).click();await expect(page.getByLabel('Credential link',{exact:true})).toHaveCount(0);

});

test('Delayed reset issuance after logout never reveals a link',async({page})=>{

 await openLocalManager(page);let release!:()=>void,started=false;const pending=new Promise<void>(r=>release=r);await page.route('**/api/company/accounts/editor/credential-link',async r=>{started=true;await pending;await r.fulfill({json:{userId:'editor',expiresAt:'2099-01-01',link:'/setup-account#token=must-not-reveal'}}).catch(()=>{});});await page.route('**/api/session/logout',r=>r.fulfill({status:204}));await page.getByRole('button',{name:'Issue credential link for Evan Editor',exact:true}).click();await expect.poll(()=>started).toBe(true);await page.getByRole('button',{name:'Account settings',exact:true}).click();await page.getByRole('dialog',{name:'Account settings',exact:true}).getByRole('button',{name:'Sign out',exact:true}).click();release();await expect(page.getByRole('heading',{name:'Sign in to your workspace'})).toBeVisible();await expect(page.getByLabel('Credential link',{exact:true})).toHaveCount(0);await expect(page.getByRole('tab')).toHaveCount(0);

});

test('Delayed Local company creation cannot replace an existing-company selection or publish its link',async({page})=>{
 await localOperator(page);let release!:()=>void,started!:()=>void;const pending=new Promise<void>(r=>release=r),requested=new Promise<void>(r=>started=r);
 await page.route('**/api/operator/companies',async r=>{if(r.request().method()==='GET')return r.fallback();started();await pending;return r.fulfill({json:{company:{...company,id:'created-company',name:'Created Company'},administrator:{account:{id:'created-admin'},setup:{userId:'created-admin',expiresAt:'2099-01-01',link:'/setup-account#token=stale-admin-link'}}}});});
 await page.route('**/api/operator/companies/created-company',r=>r.fulfill({json:{company:{...company,id:'created-company',name:'Created Company'},subscription:{kind:'Ten',limit:10,revision:1,seats:company.seats}}}));
 await page.goto('http://127.0.0.1:5174/operator');await page.getByRole('button',{name:'Create company',exact:true}).click();await page.getByLabel('Company name',{exact:true}).fill('Created Company');await page.getByLabel('First name',{exact:true}).fill('Created');await page.getByLabel('Last name',{exact:true}).fill('Admin');await page.getByRole('button',{name:'Save new company'}).click();await requested;
 await page.getByRole('button',{name:'Local Studio',exact:true}).click();await expect(page.getByRole('heading',{name:'Local Studio',exact:true})).toBeVisible();const response=page.waitForResponse(r=>r.url().endsWith('/api/operator/companies')&&r.request().method()==='POST');release();await (await response).finished();await page.evaluate(()=>new Promise<void>(resolve=>requestAnimationFrame(()=>requestAnimationFrame(()=>resolve()))));
 await expect(page.getByLabel('Credential link',{exact:true})).toHaveCount(0);await expect(page.getByRole('heading',{name:'Local Studio',exact:true})).toBeVisible();await expect(page.getByRole('heading',{name:'Created Company',exact:true})).toHaveCount(0);
});

