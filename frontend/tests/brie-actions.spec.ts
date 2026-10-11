import {test,expect,capabilitiesFor} from './session-fixture';
const id='11111111-1111-4111-8111-111111111111';
const answer=(task:object)=>({status:'action',questions:[],steps:[],citations:[],origin:'Brie',error:null,ticketTask:task,evidenceNote:'Task result'});
test('Retry preserves request identity; clarification choices and confirmation are explicit',async({page})=>{
 await page.route('**/api/brie/capabilities',r=>r.fulfill({json:{tickets:true}}));let calls:any[]=[];
 await page.route('**/api/brie/messages',r=>{const value=r.request().postDataJSON();calls.push(value);if(calls.length===1)return r.fulfill({status:503,json:{error:'Temporary failure'}});if(calls.length===2)return r.fulfill({json:answer({taskId:id,status:'clarify',message:'Choose user',choices:[{id,label:'Jane'}]})});if(calls.length===3)return r.fulfill({json:answer({taskId:id,status:'confirmation',message:'Confirm selected action',confirmationToken:'bound-token'})});return r.fulfill({json:answer({taskId:id,status:'completed',message:'Created',ticketId:id})});});
 await page.goto('http://127.0.0.1:5174');await page.getByLabel('Issue description').fill('Create ticket');await page.getByLabel('Issue description').press('Enter');await expect(page.getByText('Temporary failure')).toBeVisible();
 await page.reload();await page.getByLabel('Issue description').fill('Create ticket');await page.getByLabel('Issue description').press('Enter');await page.getByRole('button',{name:/Jane/}).click();await expect(page.getByRole('button',{name:'Confirm this action'})).toBeVisible();expect(calls).toHaveLength(3);expect(calls[0].requestId).toBe(calls[1].requestId);expect(calls[2].taskId).toBe(id);
 await page.getByRole('button',{name:'Confirm this action'}).click();await expect(page.getByRole('link',{name:'Open ticket'})).toHaveAttribute('href','/?ticket='+id);expect(calls[3].confirmationToken).toBe('bound-token');
});
test('Asset deep link opens existing company record details',async({page})=>{
 await page.route('**/api/inventory/assets',r=>r.fulfill({json:[{id,assetTag:'AI-1',computer:'Fixture laptop',category:'Computer',status:'Available',revision:1,customFields:{}}]}));
 await page.goto('http://127.0.0.1:5174/?asset='+id);await expect(page.getByRole('dialog',{name:'Asset details',exact:true})).toBeVisible();await expect(page.getByRole('heading',{name:'Fixture laptop'})).toBeVisible();
});
test('Company switch clears pending action and prevents foreign task submission',async({page})=>{
 let company='A';let tasks:any[]=[];
 await page.route('**/api/session',r=>r.fulfill({json:{user:{id:'user',displayName:'User'},activeCompany:{id:company,name:'Company '+company,revision:1},memberships:['A','B'].map(companyId=>({companyId,name:'Company '+companyId,role:'Administrator'})),role:'Administrator',isOperator:false,capabilities:capabilitiesFor('Administrator')}}));
 await page.route('**/api/session/company',r=>{company=r.request().postDataJSON().companyId;return r.fulfill({json:{}});});
 await page.route('**/api/brie/capabilities',r=>r.fulfill({json:{tickets:true}}));
 await page.route('**/api/brie/messages',r=>{tasks.push(r.request().postDataJSON());return r.fulfill({json:answer({taskId:id,status:'clarify',message:'Private company A task'})});});
 await page.goto('http://127.0.0.1:5174');await page.getByLabel('Issue description').fill('Create ticket');await page.getByLabel('Issue description').press('Enter');await expect.poll(()=>tasks.length).toBe(1);
 await page.getByRole('button',{name:'Account settings',exact:true}).click();await page.getByRole('combobox',{name:'Company',exact:true}).click();await page.getByRole('option',{name:'Company B',exact:true}).click();await expect(page.getByText('Task result')).toHaveCount(0);await page.getByLabel('Issue description').fill('Create ticket');await page.getByLabel('Issue description').press('Enter');await expect.poll(()=>tasks.length).toBe(2);expect(tasks[1].taskId).toBeNull();
});

test('Employee deep link selects only the intended directory record',async({page})=>{
 await page.route('**/api/directory/employees?*',r=>r.fulfill({json:{items:[{id,displayName:'Demo Employee',email:null,departmentId:null,active:true,revision:1},{id:'22222222-2222-4222-8222-222222222222',displayName:'Different Employee',email:null,departmentId:null,active:true,revision:1}],nextCursor:null}}));
 await page.goto('http://127.0.0.1:5174/?employee='+id);await expect(page.getByRole('tab',{name:'Employees',exact:true})).toHaveAttribute('aria-selected','true');await expect(page.getByRole('textbox',{name:'Demo Employee name',exact:true})).toBeVisible();await expect(page.getByRole('textbox',{name:'Different Employee name',exact:true})).toHaveCount(0);
});

test('Lost committed response reload replays exact original conversation envelope',async({page})=>{
 await page.route('**/api/brie/capabilities',r=>r.fulfill({json:{tickets:true}}));const calls:any[]=[];
 await page.route('**/api/brie/messages',r=>{const body=r.request().postDataJSON();calls.push(body);if(calls.length===1)return r.fulfill({json:{status:'clarify',questions:['Which symptom?'],steps:[],citations:[],origin:'Brie',conversationId:id}});if(calls.length===2)return r.fulfill({status:503,json:{error:'Response lost after commit'}});return r.fulfill({json:answer({taskId:id,status:'completed',message:'Created',ticketId:id})});});
 await page.goto('http://127.0.0.1:5174');await page.getByLabel('Issue description').fill('My printer fails');await page.getByLabel('Issue description').press('Enter');await expect(page.getByText('Which symptom?',{exact:true})).toBeVisible();
 await page.getByLabel('Issue description').fill('Create ticket Example due 2026-10-12 Low unassigned');await page.getByLabel('Issue description').press('Enter');await expect(page.getByText('Response lost after commit')).toBeVisible();await page.reload();
 await page.getByLabel('Issue description').fill('Create ticket Example due 2026-10-12 Low unassigned');await page.getByLabel('Issue description').press('Enter');await expect(page.getByRole('link',{name:'Open ticket'})).toBeVisible();expect(calls[2]).toEqual(calls[1]);
});

test('Resuming a chat restores its own pending task',async({page})=>{
 const other='22222222-2222-4222-8222-222222222222';const calls:any[]=[];await page.route('**/api/brie/capabilities',r=>r.fulfill({json:{tickets:true}}));
 await page.route('**/api/brie/messages',r=>{const b=r.request().postDataJSON();calls.push(b);return r.fulfill({json:answer({taskId:calls.length===2?other:id,status:'clarify',message:calls.length===2?'Asset tag?':'Due date?'})});});
 await page.goto('http://127.0.0.1:5174');await page.getByLabel('Issue description').fill('Create ticket Chat A');await page.getByLabel('Issue description').press('Enter');await expect(page.getByText('Task result',{exact:true})).toBeVisible();await page.getByRole('button',{name:'New chat',exact:true}).click();
 await page.getByLabel('Issue description').fill('Create asset Chat B');await page.getByLabel('Issue description').press('Enter');await expect(page.getByText('Task result',{exact:true})).toBeVisible();await page.getByRole('button',{name:'Previous chats',exact:true}).click();await page.getByText('Create ticket Chat A',{exact:true}).click();
 await page.getByLabel('Issue description').fill('2026-10-12');await page.getByLabel('Issue description').press('Enter');await expect.poll(()=>calls.length).toBe(3);expect(calls[2].taskId).toBe(id);
});


test('Lost completion response retries original task after reload sees completed status',async({page})=>{
 const calls:any[]=[];await page.route('**/api/brie/capabilities',r=>r.fulfill({json:{tickets:true}}));await page.route('**/api/brie/tickets/*',r=>r.fulfill({json:{taskId:id,status:'completed',message:'Created',ticketId:id}}));
 await page.route('**/api/brie/messages',r=>{calls.push(r.request().postDataJSON());if(calls.length===1)return r.fulfill({json:answer({taskId:id,status:'clarify',message:'Date needed'})});if(calls.length===2)return r.fulfill({status:503,json:{error:'Committed response lost'}});return r.fulfill({json:answer({taskId:id,status:'completed',message:'Created',ticketId:id})});});
 await page.goto('http://127.0.0.1:5174');await page.getByLabel('Issue description').fill('Create ticket Example');await page.getByLabel('Issue description').press('Enter');await expect(page.getByText('Task result',{exact:true})).toBeVisible();await page.getByLabel('Issue description').fill('2026-10-12');await page.getByLabel('Issue description').press('Enter');await expect(page.getByText('Committed response lost')).toBeVisible();await page.reload();await page.getByLabel('Issue description').fill('2026-10-12');await page.getByLabel('Issue description').press('Enter');await expect(page.getByRole('link',{name:'Open ticket'})).toBeVisible();expect(calls[2]).toEqual(calls[1]);
});

test('Pin refresh and resume preserve outstanding exact retry envelope',async({page})=>{
 let saved=false,pinned=false;const calls:any[]=[];await page.route('**/api/brie/capabilities',r=>r.fulfill({json:{tickets:true}}));await page.route('**/api/conversations',r=>r.fulfill({json:saved?[{id,title:'Existing chat',pinned}]:[]}));await page.route('**/api/conversations/*/action',r=>{pinned=true;return r.fulfill({json:{}});});
 await page.route('**/api/brie/messages',r=>{calls.push(r.request().postDataJSON());if(calls.length===1){saved=true;return r.fulfill({json:{status:'clarify',questions:['Which symptom?'],steps:[],citations:[],origin:'Brie',conversationId:id}});}if(calls.length===2)return r.fulfill({status:503,json:{error:'Response lost after commit'}});return r.fulfill({json:answer({taskId:id,status:'completed',message:'Created',ticketId:id})});});
 await page.goto('http://127.0.0.1:5174');await page.getByLabel('Issue description').fill('Existing chat');await page.getByLabel('Issue description').press('Enter');await expect(page.getByText('Which symptom?',{exact:true})).toBeVisible();await page.getByLabel('Issue description').fill('Create ticket Example due 2026-10-12 Low unassigned');await page.getByLabel('Issue description').press('Enter');await expect(page.getByText('Response lost after commit')).toBeVisible();
 await page.getByRole('button',{name:'Previous chats',exact:true}).click();await page.getByRole('button',{name:'Existing chat',exact:true}).click({button:'right'});await page.getByRole('menuitem',{name:'Pin',exact:true}).click();await expect.poll(()=>pinned).toBe(true);await page.getByRole('button',{name:'Existing chat',exact:true}).click();await page.getByLabel('Issue description').fill('Create ticket Example due 2026-10-12 Low unassigned');await page.getByLabel('Issue description').press('Enter');await expect(page.getByRole('link',{name:'Open ticket'})).toBeVisible();expect(calls[2]).toEqual(calls[1]);
});

test('Capability delay cannot silently route an action to documentation',async({page})=>{
 await page.route('**/api/brie/capabilities',r=>r.fulfill({status:503,json:{error:'Unavailable'}}));const routes:string[]=[];
 for(const path of ['brie/messages','questions'])await page.route('**/api/'+path,r=>{routes.push(path);return r.fulfill({json:answer({taskId:id,status:'clarify',message:'What title should the support ticket have?'})});});
 await page.goto('http://127.0.0.1:5174');await page.getByLabel('Issue description').fill('can u create a new ticket for me my computer stopped working');await page.getByLabel('Issue description').press('Enter');await expect.poll(()=>routes.length).toBe(1);expect(routes).toEqual(['brie/messages']);
});

test('Ticket URL is navigation context, never a pending action task',async({page})=>{
 await page.route('**/api/brie/capabilities',r=>r.fulfill({json:{tickets:true}}));const bodies:any[]=[];
 await page.route('**/api/brie/messages',r=>{bodies.push(r.request().postDataJSON());return r.fulfill({json:answer({taskId:'22222222-2222-4222-8222-222222222222',status:'clarify',message:'What name should the asset have?'})});});
 await page.goto('http://127.0.0.1:5174/?ticket='+id);await page.getByRole('tab',{name:'Brie',exact:true}).click();const text='Can u add a new asset for me we just received a new computer';await page.getByLabel('Issue description').fill(text);await page.getByLabel('Issue description').press('Enter');await expect.poll(()=>bodies.length).toBe(1);expect(bodies[0].question).toBe(text);expect(bodies[0].taskId).toBeNull();expect(bodies[0].requestId).not.toBe(id);
});

test('Paused task stays paused after switching chats and resuming',async({page})=>{
 const calls:any[]=[];await page.route('**/api/brie/capabilities',r=>r.fulfill({json:{tickets:true}}));await page.route('**/api/brie/messages',r=>{calls.push(r.request().postDataJSON());return r.fulfill({json:answer({taskId:id,status:calls.length===1?'clarify':'paused',message:'Task response'})});});
 await page.goto('http://127.0.0.1:5174');await page.getByLabel('Issue description').fill('Add asset paused chat');await page.getByLabel('Issue description').press('Enter');await expect.poll(()=>calls.length).toBe(1);await expect(page.getByLabel('Issue description')).toBeEnabled();await page.getByLabel('Issue description').fill('Leave that for now. My laptop cannot get online');await page.getByLabel('Issue description').press('Enter');await expect.poll(()=>calls.length).toBe(2);await expect(page.getByLabel('Issue description')).toBeEnabled();await page.getByRole('button',{name:'New chat',exact:true}).click();await page.getByRole('button',{name:'Previous chats',exact:true}).click();await page.getByText('Add asset paused chat',{exact:true}).click();await page.getByLabel('Issue description').fill('It still fails');await page.getByLabel('Issue description').press('Enter');await expect.poll(()=>calls.length).toBe(3);expect(calls[2].taskId).toBeNull();
});
