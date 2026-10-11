import {test,expect} from '@playwright/test';
test('chat retains prior turns, clarifies incrementally, and resets explicitly',async({page})=>{
  await page.goto('http://127.0.0.1:5174');
  const title='My printer is not working (test '+crypto.randomUUID()+')';
  const other='Another printer is not working (test '+crypto.randomUUID()+')';
  await expect(page.getByText('Live Ollama',{exact:true})).toBeVisible();
  const created=new Set<string>();
  page.on('response',async response=>{if(response.url().endsWith('/api/questions')&&response.ok()){const answer=await response.json();if(answer.conversationId)created.add(answer.conversationId);}});
  try {
  async function send(text:string) {
    await page.getByRole('textbox',{name:'Issue description'}).fill(text);
    await page.getByRole('button',{name:'Send question'}).click();
    await expect(page.getByRole('textbox',{name:'Issue description'})).toBeEnabled({timeout:80000});
  }
  await send(title);
  await expect(page.locator('.conversation-turn')).toHaveCount(1);
  await expect(page.locator('.conversation-turn').last()).toContainText('try to print');
  await send('Not appearing in Windows Settings');
  await expect(page.locator('.conversation-turn')).toHaveCount(2);
  await expect(page.locator('.conversation-turn').last()).toContainText('model');
  await send('TM-T88V');
  await expect(page.locator('.conversation-turn')).toHaveCount(3);
  await expect(page.locator('.conversation-turn').last()).toContainText('connected');
  await expect(page.locator('.user-bubble').first()).toHaveText(title);
  await page.screenshot({path:'../artifacts/clary-conversation.png',fullPage:true});
  await page.getByRole('button',{name:'New chat',exact:true}).click();
  await expect(page.locator('.conversation-turn')).toHaveCount(0);
  await expect(page.getByRole('heading',{name:'Let’s get started'})).toBeVisible();
  await page.getByRole('button',{name:'Previous chats',exact:true}).click();
  await page.locator('.recent-chat').filter({hasText:title}).click();
  await expect(page.locator('.conversation-turn')).toHaveCount(3);
  await send("I don't know the connection type");
  await expect(page.locator('.conversation-turn')).toHaveCount(4);
  await page.reload();
  await page.getByRole('button',{name:'Previous chats',exact:true}).click();
  await page.locator('.recent-chat').filter({hasText:title}).click();
  await expect(page.locator('.conversation-turn')).toHaveCount(4);
  await page.getByRole('button',{name:'New chat',exact:true}).click();
  await send(other);
  await page.getByRole('button',{name:'Previous chats',exact:true}).click();
  await expect(page.locator('.recent-chat').filter({hasText:title})).toHaveCount(1);
  await expect(page.locator('.recent-chat').filter({hasText:other})).toHaveCount(1);
  await page.getByRole('button',{name:'Close history'}).click();
  await expect(page.locator('.conversation-turn')).toHaveCount(1);
  await page.getByRole('button',{name:'Previous chats',exact:true}).click();
  await page.locator('.recent-chat').filter({hasText:title}).click();
  await expect(page.locator('.conversation-turn')).toHaveCount(4);
  } finally {for(const id of created)await page.request.post('http://127.0.0.1:5081/api/conversations/'+id+'/action',{data:{operation:'delete'}});}
});
