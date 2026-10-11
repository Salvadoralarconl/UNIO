import {test,expect,capabilitiesFor} from './session-fixture';
test('Delayed citation from previous company cannot populate the switched workspace',async({page})=>{
 let company='A',release!:()=>void,started=false;const pending=new Promise<void>(r=>release=r);
 await page.route('**/api/session',r=>r.fulfill({json:{user:{id:'user',displayName:'User'},activeCompany:{id:company,name:'Company '+company,revision:1},memberships:['A','B'].map(companyId=>({companyId,name:'Company '+companyId,role:'Administrator'})),role:'Administrator',isOperator:false,capabilities:capabilitiesFor('Administrator')}}));
 await page.route('**/api/session/company',r=>{company=r.request().postDataJSON().companyId;return r.fulfill({json:{}});});
 await page.route('**/api/brie/capabilities',r=>r.fulfill({json:{tickets:true}}));
 await page.route('**/api/brie/messages',r=>r.fulfill({json:{status:'supported',questions:[],steps:[{text:'Private A procedure',citations:[{passageId:'A-source',quote:'Private A evidence'}]}],citations:[],origin:'Fixture',error:null}}));
 await page.route('**/api/passages/A-source',async r=>{started=true;await pending;await r.fulfill({json:{id:'A-source',revisionId:'A-rev',title:'Private A guide',version:'1',location:'step1',text:'Private A evidence'}}).catch(()=>{});});
 await page.goto('http://127.0.0.1:5174');await page.getByLabel('Issue description').fill('Help with fixture');await page.getByLabel('Issue description').press('Enter');await expect.poll(()=>started).toBe(true);
 await page.getByRole('button',{name:'Account settings',exact:true}).click();await page.getByRole('combobox',{name:'Company',exact:true}).click();await page.getByRole('option',{name:'Company B',exact:true}).click();release();await expect(page.getByLabel('Issue description')).toHaveValue('');await expect(page.getByText('Private A procedure',{exact:true})).toHaveCount(0);await expect(page.getByText('Private A evidence',{exact:true})).toHaveCount(0);await expect(page.getByRole('button',{name:/Open citation/})).toHaveCount(0);
});
test('Unavailable citation is reported instead of displaying unverified advice',async({page})=>{
 await page.route('**/api/brie/capabilities',r=>r.fulfill({json:{tickets:true}}));await page.route('**/api/brie/messages',r=>r.fulfill({json:{status:'supported',questions:[],steps:[{text:'Advice requiring source',citations:[{passageId:'missing-source',quote:'Evidence'}]}],citations:[],origin:'Fixture',error:null}}));await page.route('**/api/passages/missing-source',r=>r.fulfill({status:503,json:{error:'Citation temporarily unavailable'}}));
 await page.goto('http://127.0.0.1:5174');await page.getByLabel('Issue description').fill('Help fixture');await page.getByLabel('Issue description').press('Enter');await expect(page.getByText('Citation temporarily unavailable')).toBeVisible();await expect(page.getByText('Advice requiring source',{exact:true})).toHaveCount(0);await expect(page.getByLabel('Issue description')).toHaveValue('Help fixture');
});
