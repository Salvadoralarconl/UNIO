import {test as base,expect} from '@playwright/test';
export function capabilitiesFor(role:string,assignedOnly=false){const admin=role==='Administrator',editor=role==='Editor';return {read:!assignedOnly&&['Administrator','Editor','Viewer'].includes(role),ownInventory:['Administrator','Editor','Viewer'].includes(role),write:!assignedOnly&&(admin||editor),delete:!assignedOnly&&admin,approve:!assignedOnly&&(admin||editor),index:!assignedOnly&&(admin||editor),repair:!assignedOnly&&(admin||editor),settings:!assignedOnly&&admin,accountAdmin:!assignedOnly&&admin};}
// Only isolated UI tests use this fixture. Real-network suites keep the base test.
export const test=base.extend({page:async({page},use)=>{
 // Never let an isolated browser test fall through to the undeployed live API.
 await page.route('**/api/**',route=>{
  const path=new URL(route.request().url()).pathname;
  if(route.request().method()!=='GET')return route.abort('blockedbyclient');
  if(['/api/directory/employees','/api/directory/departments','/api/directory/locations'].includes(path))return route.fulfill({json:{items:[],nextCursor:null}});
  if(path==='/api/directory/mapping-review'||path==='/api/company/memberships')return route.fulfill({json:[]});
  if(path==='/api/company/accounts'||path==='/api/company/invitations')return route.fulfill({json:{items:[],nextCursor:null}});
  if(path==='/api/company/seats')return route.fulfill({json:{enabled:1,pending:0,limit:10,remaining:9,overLimit:false}});
  if(path==='/api/company')return route.fulfill({json:{id:'mock-company',name:'Test company',revision:1}});
  if(path==='/api/inventory/configuration')return route.fulfill({json:{types:['Computer','Printer','Monitor','Network','Accessory'].map((code,i)=>({id:'00000000-0000-4000-8000-00000000000'+i,code,name:code,icon:['Laptop','Printer','Monitor','Server','Headphones'][i],active:true,sortOrder:i,revision:1})),fields:[],icons:[]}});
  if(path==='/api/health')return route.fulfill({json:{status:'ok',origin:'Isolated test workspace',liveReady:false,configurationError:null}});
  if(path==='/api/library')return route.fulfill({json:{folders:[],placements:[]}});
  if(path==='/api/inventory/documentation')return route.fulfill({json:{documentId:null,candidates:[]}});
  if(['/api/documents','/api/conversations','/api/chat-projects','/api/pinned-folders','/api/equipment/requests','/api/equipment/kits','/api/tickets/templates','/api/inventory/assets','/api/inventory/activity','/api/inventory/departments'].includes(path))return route.fulfill({json:[]});
  return route.abort('blockedbyclient');
 });
 await page.route('**/api/session',route=>route.fulfill({json:{authenticationMode:'Local',user:{id:'mock-user',displayName:'Test user',email:null},activeCompany:{id:'mock-company',name:'Test company',revision:1},memberships:[{companyId:'mock-company',name:'Test company',role:'Administrator'}],role:'Administrator',isOperator:false,capabilities:capabilitiesFor('Administrator')}}));
 await page.route('**/api/session/antiforgery',route=>route.fulfill({json:{token:'isolated-ui-token'}}));
 await use(page);
}});
export {expect};

