import {test,expect} from './session-fixture';
test('ticket toolbar order, priority, and attachment controls work',async({page})=>{
 let ticket={id:'feature-ticket',title:'Prepare laptop',description:'Configure for handover.',type:'Support',status:'Assigned',priority:'High',revision:1,dueDate:'2026-10-09',createdAt:'2026-10-10T12:00:00Z',assignmentMode:'Users',assignees:[],assets:[],documents:[],lines:[],checklist:[],allowedActions:['priority','comment']};
 await page.route('**/api/tickets?**',r=>r.fulfill({json:{items:[ticket],nextCursor:null}}));
 await page.route('**/api/tickets/feature-ticket',r=>r.fulfill({json:ticket}));
 await page.route('**/api/tickets/feature-ticket/comments?**',r=>r.fulfill({json:{items:[],nextCursor:null}}));
 await page.route('**/api/tickets/feature-ticket/actions',r=>{expect(r.request().postDataJSON()).toEqual({action:'priority',priority:'Urgent',revision:1});ticket={...ticket,priority:'Urgent',revision:2};return r.fulfill({json:ticket});});
 await page.route('**/api/tickets/feature-ticket/attachments',r=>r.fulfill({json:r.request().method()==='POST'?{id:'file1',filename:'handover.txt',size:5,canRemove:true}:[]}));
 await page.route('**/api/tickets/feature-ticket/attachments/file1',r=>r.fulfill({json:{deleted:true}}));
 await page.goto('http://localhost:5174/');await page.getByRole('tab',{name:'Tickets',exact:true}).click();
 const toolbar=page.locator('.workspace-toolbar');const controls=[toolbar.getByRole('button',{name:'Filter tickets',exact:true}),toolbar.getByRole('button',{name:'My tickets',exact:true}),toolbar.getByRole('button',{name:'New ticket',exact:true})];
 const boxes=await Promise.all(controls.map(control=>control.boundingBox()));expect(boxes[0]!.x).toBeLessThan(boxes[1]!.x);expect(boxes[1]!.x).toBeLessThan(boxes[2]!.x);
 await expect(page.locator('.tickets-page>.tickets-heading')).toHaveCount(0);
 await controls[1].click();await expect(controls[1]).toHaveAttribute('aria-pressed','true');
 await page.getByRole('combobox',{name:'Ticket priority',exact:true}).click();await page.getByRole('option',{name:'Urgent',exact:true}).click();await expect(page.getByRole('combobox',{name:'Ticket priority',exact:true})).toHaveValue('Urgent');
 await page.getByLabel('Upload ticket attachment').setInputFiles({name:'handover.txt',mimeType:'text/plain',buffer:Buffer.from('Ready')});await expect(page.getByRole('button',{name:'Remove handover.txt',exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Remove handover.txt',exact:true}).click();await expect(page.getByRole('button',{name:'Remove handover.txt',exact:true})).toHaveCount(0);
 await page.screenshot({path:'test-results/tickets-color-preview.png',fullPage:true});
});
test('optional setup ticket can be deleted from the workspace',async({page})=>{
 let deleted=false;
 const ticket={id:'setup-ticket',title:'Set up your workspace',description:'Optional setup checklist',type:'Support',status:'Assigned',revision:1,dueDate:null,createdAt:'2026-10-10T12:00:00Z',assignmentMode:'Users',assignees:[],assets:[],documents:[],lines:[],checklist:[],allowedActions:['delete','start','complete','comment']};
 await page.route('**/api/tickets?**',r=>r.fulfill({json:{items:deleted?[]:[ticket],nextCursor:null}}));
 await page.route('**/api/tickets/setup-ticket',r=>r.fulfill({json:ticket}));
 await page.route('**/api/tickets/setup-ticket/comments?**',r=>r.fulfill({json:{items:[],nextCursor:null}}));
 await page.route('**/api/tickets/setup-ticket/actions',r=>{expect(r.request().postDataJSON()).toEqual({action:'delete',revision:1});deleted=true;return r.fulfill({json:{...ticket,revision:2,allowedActions:[]}});});
 await page.goto('http://localhost:5174/');await page.getByRole('tab',{name:'Tickets',exact:true}).click();
 await page.getByRole('button',{name:'Delete setup ticket',exact:true}).click();
 await expect(page.getByRole('heading',{name:'Set up your workspace',exact:true})).toHaveCount(0);
 await page.reload();await page.getByRole('tab',{name:'Tickets',exact:true}).click();
 await expect(page.getByRole('button',{name:'Delete setup ticket',exact:true})).toHaveCount(0);
});
test('focused ticket workspace separates recipient and assignees and puts comments below information',async({page})=>{
 const ticket={id:'t1',title:'Prepare Jordan workspace',description:'Install standard apps and deliver the laptop.',type:'Onboarding',status:'In Progress',revision:3,dueDate:'2026-10-14',createdAt:'2026-10-10T12:00:00Z',creatorName:'Casey Parker',approverName:'Jamie Rivera',approvedAt:'2026-10-10T13:00:00Z',recipientName:'Jordan Taylor',assignmentMode:'Users',assignees:[{id:'u1',displayName:'Alex Morgan'},{id:'u2',displayName:'Sam Lee'}],assets:[{id:'a1',name:'Dell Latitude',assetTag:'L-026',assetType:'Computer',status:'Available',outcome:'Assign',available:true,brand:'Lenovo',model:'ThinkPad P1'}],documents:[{id:'d1',title:'Onboarding guide',available:true}],allowedActions:['complete','comment','cancel','edit'],lines:[]};
 await page.route('**/api/tickets?**',r=>r.fulfill({json:{items:[ticket],nextCursor:null}}));await page.route('**/api/tickets/t1',r=>r.fulfill({json:ticket}));
 await page.route('**/api/tickets/t1/activity?**',r=>r.fulfill({json:{items:[{id:'h1',actorName:'Alex Morgan',action:'In Progress',createdAt:'2026-10-10T14:00:00Z'}],nextCursor:null}}));
 await page.route('**/api/tickets/t1/comments?**',r=>r.fulfill({json:{items:[{id:'c1',authorName:'Sam Lee',body:'Laptop is configured.',createdAt:'2026-10-10T14:30:00Z'}],nextCursor:null}}));
 await page.route('**/api/tickets/t1/comments',r=>r.fulfill({json:{id:'c2',authorName:'You',body:r.request().postDataJSON().body,createdAt:'2026-10-10T15:00:00Z'}}));
 await page.goto('http://localhost:5174/');await page.getByRole('tab',{name:'Tickets',exact:true}).click();
 const workspace=page.getByRole('region',{name:'Tickets workspace'});await expect(page.locator('.workspace-toolbar').getByText('Tickets',{exact:true})).toBeVisible();await expect(workspace).toContainText('Jordan Taylor');await expect(workspace).toContainText('Alex Morgan, Sam Lee');await expect(workspace).toContainText('Onboarding guide');await expect(workspace).toContainText('Lenovo');await expect(workspace).toContainText('ThinkPad P1');await expect(workspace.getByText('Equipment will be assigned', {exact:false})).toHaveCount(0);await workspace.getByRole('button',{name:'Change ticket status'}).click();await expect(workspace.getByRole('menuitem').filter({hasText:'Complete'})).toBeVisible();await page.keyboard.press('Escape');
 await page.getByRole('textbox',{name:'Add a comment'}).fill('Ready for handover.');const send=page.getByRole('button',{name:'Send',exact:true});const box=(await page.getByRole('textbox',{name:'Add a comment'}).boundingBox())!,button=(await send.boundingBox())!;expect(button.x).toBeGreaterThan(box.x);expect(button.y+button.height).toBeLessThan(box.y+box.height);const comment=page.getByRole('textbox',{name:'Add a comment'});await comment.press('Shift+Enter');await expect(comment).toHaveValue('Ready for handover.\n');await comment.press('Enter');await expect(workspace).toContainText('Ready for handover.');
 const list=await page.getByRole('region',{name:'Ticket list'}).boundingBox(),detail=await page.getByRole('region',{name:'Ticket details'}).boundingBox();expect(detail!.width).toBeGreaterThan(list!.width*1.5);
 const listHeader=await page.getByRole('region',{name:'Ticket list'}).locator(':scope > header').boundingBox(),detailHeader=await page.getByRole('region',{name:'Ticket details'}).locator(':scope > header').boundingBox();expect(detailHeader!.height).toBe(listHeader!.height);expect(detailHeader!.y).toBe(listHeader!.y);
 await expect(page.getByRole('heading',{name:'Activity',exact:true})).toHaveCount(0);await page.screenshot({path:'test-results/tickets-workspace-preview.png',fullPage:true});
 await page.route('**/api/inventory/assets',r=>r.fulfill({json:[{id:'a1',computer:'Dell Latitude',assetTag:'L-026',category:'Computer',customCategory:'Laptop',status:'Available'},{id:'a2',computer:'ThinkPad replacement',assetTag:'L-027',category:'Computer',customCategory:'Laptop',status:'Available'},{id:'a3',computer:'Wrong category',assetTag:'D-01',category:'Computer',customCategory:'Desktop',status:'Available'}]}));
 await page.route('**/api/tickets/t1',r=>{if(r.request().method()==='PUT'){expect(r.request().postDataJSON().assetLinks).toEqual([{assetId:'a2',outcome:'Assign'}]);return r.fulfill({json:{...ticket,revision:4,assets:[{...ticket.assets[0],id:'a2',name:'ThinkPad replacement',assetTag:'L-027'}]}});}return r.fulfill({json:ticket});});
 await page.getByRole('button',{name:'Replace Dell Latitude',exact:true}).click();const replacement=page.getByRole('dialog',{name:'Replace equipment'});await replacement.getByRole('combobox',{name:'Replacement equipment',exact:true}).click();await expect(replacement.getByRole('option',{name:'Wrong category',exact:true})).toHaveCount(0);await replacement.getByRole('option',{name:'ThinkPad replacement L-027'}).click();await replacement.getByRole('button',{name:'Save',exact:true}).click();await expect(replacement).not.toBeVisible();await expect(page.getByRole('button',{name:'ThinkPad replacement',exact:true})).toBeVisible();

});

test('ticket status is selected using the filter dropdown',async({page})=>{await page.route('**/api/tickets?**',r=>r.fulfill({json:{items:[],nextCursor:null}}));await page.goto('http://127.0.0.1:5174');await page.getByRole('tab',{name:'Tickets',exact:true}).click();await expect(page.locator('.ticket-status-summary')).toHaveCount(0);await page.getByRole('button',{name:'Filter tickets',exact:true}).click();await page.getByRole('combobox',{name:'Status',exact:true}).click();for(const status of ['All statuses','Pending','Approved','Assigned','In Progress','Complete','Canceled'])await expect(page.getByRole('option',{name:status,exact:true})).toBeVisible();await page.getByRole('option',{name:'Pending',exact:true}).click();await page.getByRole('button',{name:'Filter tickets',exact:true}).click();await expect(page.getByRole('combobox',{name:'Status',exact:true})).toHaveValue('Pending');await expect(page.getByRole('combobox')).toHaveCount(2);});
