import type {Page} from '@playwright/test';
import {expect} from './session-fixture';
export const seats={enabled:2,pending:1,limit:10,remaining:7,overLimit:false};
export const company={id:'c1',name:'North Studio',active:true,revision:3,plan:'Ten',limit:10,seats,onlineUsers:1,lastActivity:'2026-10-08T12:00:00Z',needsAdministrator:false};
export const accounts=[{id:'admin',displayName:'Alex Admin',email:'admin@example.test',role:'Administrator',enabled:true,revision:4,lastActivity:'2026-10-08T12:00:00Z',online:true},{id:'editor',displayName:'Evan Editor',email:'evan@example.test',role:'Editor',enabled:false,revision:7,lastActivity:null,online:false}];
export async function operatorFixture(page:Page){let current={...company,seats:{...seats}},subscription={kind:'Ten',limit:10,revision:9,seats:{...seats}},rows=accounts.map(a=>({...a}));const calls:{path:string;method:string;body:any}[]=[];
 await page.route('**/api/session',r=>r.fulfill({json:{authenticationMode:'Oidc',user:{id:'owner',displayName:'Platform Operator',email:null},activeCompany:null,memberships:[],role:null,isOperator:true,capabilities:{read:false,ownInventory:false,write:false,delete:false,approve:false,index:false,repair:false,settings:false,accountAdmin:false}}}));
 await page.route('**/api/operator/**',async r=>{const path=new URL(r.request().url()).pathname,method=r.request().method(),body=method==='GET'?undefined:r.request().postDataJSON();calls.push({path,method,body});if(method!=='GET')expect(r.request().headers()['x-csrf-token']).toBe('isolated-ui-token');
 if(path==='/api/operator/companies'&&method==='GET')return r.fulfill({json:{items:[current],nextCursor:null}});
 if(path==='/api/operator/companies'&&method==='POST'){current={...current,name:body.name};return r.fulfill({json:current});}
 if(path==='/api/operator/companies/c1'&&method==='GET')return r.fulfill({json:{company:current,subscription}});
 if(path==='/api/operator/companies/c1'&&method==='PUT'){current={...current,...body,revision:current.revision+1};return r.fulfill({json:current});}
 if(path.endsWith('/subscription')&&method==='PUT'){subscription={...subscription,kind:body.kind,limit:body.kind==='Unlimited'?null:body.customLimit??10,revision:subscription.revision+1};current={...current,plan:subscription.kind,limit:subscription.limit};return r.fulfill({json:subscription});}
 if(path.endsWith('/administrator')&&method==='PUT'){rows=rows.map(a=>({...a,role:a.id===body.userId?'Administrator':'Editor',revision:a.revision+1,enabled:a.id===body.userId?true:a.enabled}));current={...current,revision:current.revision+1};return r.fulfill({json:current});}
 if(path.endsWith('/accounts')&&method==='GET')return r.fulfill({json:{items:rows,nextCursor:null}});
 if(path.endsWith('/accounts')&&method==='POST'){const a={...body,id:'new',revision:1,lastActivity:null,online:false};rows.push(a);return r.fulfill({json:a});}
 if(path.includes('/accounts/')&&method==='PUT'){const a={...rows.find(a=>a.id===path.split('/').at(-1)),...body,revision:body.revision+1};rows=rows.map(x=>x.id===a.id?a:x);return r.fulfill({json:a});}
 if(path.endsWith('/invitations')&&method==='GET')return r.fulfill({json:{items:[{id:'i1',email:'pending@example.test',role:'Viewer',status:'Pending',revision:2,createdAt:'2026-10-08T12:00:00Z',expiresAt:'2026-10-15T12:00:00Z',issuer:null,subject:null}],nextCursor:null}});
 if(path.endsWith('/invitations')&&method==='POST')return r.fulfill({json:{id:'i2',...body,status:'Pending',revision:1,createdAt:'2026-10-08T12:00:00Z',expiresAt:'2026-10-15T12:00:00Z',issuer:null,subject:null,token:'isolated-token',link:'/accept-invitation#token=isolated-token'}});
 if(path.endsWith('/invitations/i1')&&method==='DELETE')return r.fulfill({json:{id:'i1',status:'Revoked'}});
 return r.abort('blockedbyclient');});return {calls};}
export async function openCompany(page:Page){await page.goto('http://127.0.0.1:5174/operator');await page.getByRole('button',{name:'North Studio',exact:true}).click();await expect(page.getByRole('heading',{name:'Company accounts'})).toBeVisible();await expect(page.getByText('Evan Editor',{exact:true})).toBeVisible();}
