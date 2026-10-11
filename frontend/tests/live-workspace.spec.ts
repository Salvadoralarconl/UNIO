import { test, expect } from '@playwright/test';
test.skip(process.env.CLARY_LIVE_TEST !== '1', 'Opt-in live private Ollama evaluation.');
test.beforeEach(async()=>{test.setTimeout(180000);});
test.afterEach(async({page,request})=>{for(const id of await page.evaluate(()=>JSON.parse(sessionStorage.getItem('audit-conversations')??'[]')))await request.post(`http://127.0.0.1:5081/api/conversations/${id}/action`,{data:{operation:'delete'}});});
test.beforeEach(async({page})=>{page.on('response',async r=>{if(r.url().endsWith('/api/questions')&&r.request().method()==='POST'&&r.ok()){try{const a=await r.json();if(a.conversationId)await page.evaluate(id=>{const ids=JSON.parse(sessionStorage.getItem('audit-conversations')??'[]');if(!ids.includes(id))ids.push(id);sessionStorage.setItem('audit-conversations',JSON.stringify(ids));},a.conversationId);}catch{}}});});
test('printer clarification follow-up completes with inspectable compact citations',async({page})=>{
  await page.goto('http://127.0.0.1:5174');
  for(const message of ['My printer has power and paper but windows is not printing to it','i have the t88v','usb']) {
    await page.getByRole('textbox',{name:'Issue description'}).fill(message);
    await page.getByRole('button',{name:'Send question'}).click();
    await expect(page.getByRole('textbox',{name:'Issue description'})).toBeEnabled({timeout:80000});
  }
  await expect(page.locator('.conversation-turn')).toHaveCount(3);
  await expect(page.locator('.conversation-turn').last().locator('.advice-step').first()).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
  await page.locator('.citation').first().click();
  await expect(page.getByRole('dialog',{name:'Supporting passage'})).toBeVisible();
  await expect(page.getByRole('dialog',{name:'Supporting passage'})).not.toContainText('@quote:');await expect(page.locator('.chat-reply').last()).not.toContainText('@action:');
  await expect(page.getByRole('dialog',{name:'Supporting passage'})).not.toContainText('@retrieved-passage');
});
test('live Ollama retrieves sources and opens supporting passage', async ({page})=>{
  await page.goto('http://127.0.0.1:5174');
  await expect(page.getByText('Live Ollama',{exact:true})).toBeVisible();
  await page.getByRole('textbox',{name:'Issue description'}).fill('My Epson TM-T88V is connected by USB. How do I run a standalone printer self-test?');
  await page.getByRole('button',{name:'Send question'}).click();
  await expect(page.locator('.advice-step').first()).toBeVisible({timeout:80000});
  await page.locator('.citation').first().click();
  await expect(page.getByRole('dialog',{name:'Supporting passage'})).toContainText('Feed');
  await page.getByRole('button',{name:'Close passage'}).click();
  await page.screenshot({path:'../artifacts/clary-live-desktop.png',fullPage:true});
  // A source can disappear after reindexing. Opening it must not erase the chat.
  await page.route('**/api/passages/*',route=>route.fulfill({status:404,contentType:'application/json',body:JSON.stringify({error:'Passage unavailable'})}));
  await page.locator('.citation').first().click();
  await expect(page.getByRole('alert')).toContainText('Your conversation is preserved');
  await expect(page.locator('.conversation-turn')).toHaveCount(1);
  await page.getByRole('button',{name:'New chat',exact:true}).click();
  await page.getByRole('button',{name:'Previous chats',exact:true}).click();
  await page.locator('.recent-chat').filter({hasText:'How do I run a standalone printer self-test?'}).first().click();
  await expect(page.locator('.conversation-turn')).toHaveCount(1);
  // Inspect the next request while simulating a transport failure, not an AI answer.
  await page.route('**/api/questions',route=>route.abort());
  const nextRequest=page.waitForRequest(request=>request.url().endsWith('/api/questions') && request.method()==='POST');
  await page.getByRole('textbox',{name:'Issue description'}).fill('Can you explain that?');
  await page.getByRole('button',{name:'Send question'}).click();
  expect((await nextRequest).postDataJSON().conversationId).toBeTruthy();
  await expect(page.getByRole('textbox',{name:'Issue description'})).toBeEnabled();
  await expect(page.locator('.conversation-turn')).toHaveCount(1);
});
test('live unsupported question produces no guessed fixes',async({page})=>{
 await page.goto('http://127.0.0.1:5174');
 await page.getByRole('textbox',{name:'Issue description'}).fill('How do I bake sourdough bread?');
 await page.getByRole('button',{name:'Send question'}).click();
 await expect(page.locator('.conversation-turn').last().getByText('No relevant information was found',{exact:false})).toBeVisible({timeout:80000});
 await expect(page.locator('.advice-step')).toHaveCount(0);
});
