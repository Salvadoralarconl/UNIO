import {test,expect} from './session-fixture';

import {operatorFixture,openCompany,accounts} from './account-test-fixture';



for (const surface of ['Operator','Administrator']) {

 test(`${surface} retains committed invitation response across account search`,async({page})=>{

  if(surface==='Operator') await operatorFixture(page);

  let release!:()=>void;

  const responseGate=new Promise<void>(resolve=>{release=resolve;});

  let committed!:()=>void;

  const committedGate=new Promise<void>(resolve=>{committed=resolve;});

  let posts=0;

  const scope=surface==='Operator'?'/api/operator/companies/c1':'/api/company';

  const searches:string[]=[];

  await page.route(`**${scope}/accounts**`,route=>{

   searches.push(new URL(route.request().url()).searchParams.get('search')??'');

   return route.fulfill({json:{items:surface==='Operator'?accounts:[],nextCursor:null}});

  });

  await page.route(`**${scope}/invitations`,async route=>{

   if(route.request().method()==='GET')return route.fulfill({json:{items:[],nextCursor:null}});

   expect(route.request().method()).toBe('POST');

   expect(route.request().headers()['x-csrf-token']).toBe('isolated-ui-token');

   posts++;

   committed();

   await responseGate;

   await route.fulfill({json:{id:'committed',email:'new@example.test',role:'Viewer',status:'Pending',revision:1,link:'/accept-invitation#token=committed-secret'}});

  });

  if(surface==='Operator') await openCompany(page);

  else {

   await page.goto('http://127.0.0.1:5174');

   await page.getByRole('tab',{name:'Inventory',exact:true}).click();

   await page.getByRole('button',{name:'Settings',exact:true}).click();

   await expect(page.getByRole('heading',{name:'Company accounts'})).toBeVisible();

  }

  await page.getByRole('button',{name:'Invite account',exact:true}).click();

  await page.getByLabel('Invitation email',{exact:true}).fill('new@example.test');

  await page.getByRole('button',{name:'Create invitation',exact:true}).click();

  await committedGate;

  await page.getByLabel('Search accounts',{exact:true}).fill('latest');

  await expect(page.getByRole('button',{name:'Create invitation',exact:true})).toBeDisabled();

  await expect.poll(()=>searches.filter(query=>query==='latest').length).toBe(1);

  release();

  await expect(page.getByLabel('Invitation link',{exact:true})).toHaveValue('http://127.0.0.1:5174/accept-invitation#token=committed-secret');

  await expect(page.getByRole('button',{name:'Copy invitation link',exact:true})).toBeVisible();

  await expect(page.getByRole('form',{name:'Invite account',exact:true})).toHaveCount(0);

  await expect(page.getByRole('button',{name:'Invite account',exact:true})).toBeEnabled();

  await expect.poll(()=>searches.filter(query=>query==='latest').length).toBe(2);

  expect(posts).toBe(1);

 });

}


for (const leave of ['company','session']) {
 test(`delayed invitation cannot restore its link after leaving ${leave}`,async({page})=>{
  await operatorFixture(page);
  let release!:()=>void;
  const gate=new Promise<void>(resolve=>{release=resolve;});
  let started!:()=>void;
  const startedGate=new Promise<void>(resolve=>{started=resolve;});
  await page.route('**/api/operator/companies/c1/invitations',async route=>{
   if(route.request().method()!=='POST')return route.fallback();
   started();
   await gate;
   await route.fulfill({json:{id:'delayed',link:'/accept-invitation#token=departed-secret'}}).catch(()=>{});
  });
  await page.route('**/api/session/logout',route=>route.fulfill({status:204}));
  await openCompany(page);
  await page.getByRole('button',{name:'Invite account',exact:true}).click();
  await page.getByLabel('Invitation email',{exact:true}).fill('new@example.test');
  await page.getByRole('button',{name:'Create invitation',exact:true}).click();
  await startedGate;
  if(leave==='company')await page.getByRole('button',{name:'All companies',exact:true}).click();
  else {
   await page.getByRole('button',{name:'Account settings',exact:true}).click();await page.getByRole('button',{name:'Sign out',exact:true}).click();
   await expect(page.getByRole('heading',{name:'Sign in to your workspace'})).toBeVisible();
  }
  release();
  if(leave==='company') {
   await page.getByRole('button',{name:'North Studio',exact:true}).click();
   await expect(page.getByRole('heading',{name:'Company accounts'})).toBeVisible();
  }
  await expect(page.getByLabel('Invitation link',{exact:true})).toHaveCount(0);
  await expect(page.getByRole('form',{name:'Invite account',exact:true})).toHaveCount(0);
 });
}

