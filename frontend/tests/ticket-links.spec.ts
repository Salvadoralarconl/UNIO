import {test,expect} from './session-fixture';
test('an asset can prefill a connected ticket and its history can open a ticket',async({page})=>{
 const asset={id:'asset-1',computer:'Linked laptop',assetTag:'L-1',category:'Computer',status:'Assigned',revision:1,updatedAt:'2026-10-10T12:00:00Z',customFields:{},softwareStatus:'Not checked'};
 await page.route('**/api/inventory/assets',r=>r.fulfill({json:[asset]}));await page.route('**/api/inventory/assets/asset-1/history**',r=>r.fulfill({json:{items:[{id:'h1',action:'Created',actorDisplayName:'Alex',ticketId:'t1',occurredAt:'2026-10-10T12:00:00Z',before:null,after:{status:'Assigned'}}],nextCursor:null}}));
 await page.route('**/api/tickets?**',r=>r.fulfill({json:{items:[],nextCursor:null}}));await page.route('**/api/tickets/assignees',r=>r.fulfill({json:[]}));
 await page.goto('http://localhost:5174/');await page.getByRole('tab',{name:'Inventory',exact:true}).click();await page.getByRole('button',{name:'Inventory',exact:true}).click();await page.getByRole('button',{name:'Open asset Linked laptop',exact:true}).click();
 await page.getByRole('button',{name:'Create ticket',exact:true}).click();const modal=page.getByRole('dialog',{name:'New ticket',exact:true});await expect(modal).toBeVisible();await expect(modal).toContainText('Linked laptop');await modal.getByRole('button',{name:'Close',exact:true}).click();await expect(modal).not.toBeVisible();
});
