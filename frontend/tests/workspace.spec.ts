import { test, expect } from './session-fixture';
test.beforeEach(async({page})=>{
 const id='11111111-1111-1111-1111-111111111111', other='22222222-2222-2222-2222-222222222222';
 await page.route('**/api/health',r=>r.fulfill({json:{status:'ok',origin:'Demo workspace',liveReady:true}}));
 await page.route('**/api/conversations',r=>r.fulfill({json:[]}));
 await page.route('**/api/passages/*',r=>r.fulfill({json:{id:r.request().url().split('/').pop(),revisionId:id,title:'VPN guide',version:'1',location:'test',text:'Internal services require the Corporate Access profile.'}}));
 await page.route('**/api/questions',r=>{const q=r.request().postDataJSON().question;return r.fulfill({json:{conversationId:id,status:q.includes('bread')?'insufficient':q.includes('timeout')?'conflicting':'supported',evidenceNote:q.includes('bread')?'No relevant information was found.':null,questions:[],steps:q.includes('bread')||q.includes('timeout')?[]:[{text:'Check the Corporate Access profile.',citations:[{passageId:id,quote:'Internal services require the Corporate Access profile.'}]}],citations:q.includes('timeout')?[{passageId:id,quote:'Internal services require the Corporate Access profile.'},{passageId:other,quote:'Internal services require the Corporate Access profile.'}]:[],origin:'Demo workspace',error:null}});});
});

test('demo question opens an exact supporting passage', async ({ page }) => {
  await page.goto('http://127.0.0.1:5174');
  await expect(page.getByText('Demo workspace', { exact: true })).toBeVisible();
  await page.getByRole('textbox', { name: 'Issue description' }).fill('My VPN connects but I cannot access internal services.');
  await page.getByRole('button', { name: 'Send question' }).click();
  await expect(page.getByRole('button', { name: 'Open citation 1' })).toBeVisible();
  await page.getByRole('button', { name: 'Open citation 1' }).click();
  await expect(page.getByRole('dialog', { name: 'Supporting passage' })).toContainText('Internal services require the Corporate Access profile.');
  await page.getByRole('button', { name: 'Close passage' }).click();
  await page.screenshot({ path: '../artifacts/clary-desktop.png', fullPage: true });
});

test('unsupported and conflicting questions do not show fixes', async ({ page }) => {
  await page.goto('http://127.0.0.1:5174');
  await page.getByRole('textbox', { name: 'Issue description' }).fill('How do I bake sourdough bread?');
  await page.getByRole('button', { name: 'Send question' }).click();
  await expect(page.getByText('No relevant information was found.',{exact:true})).toBeVisible();
  await expect(page.locator('.advice-step')).toHaveCount(0);
  await page.getByRole('textbox', { name: 'Issue description' }).fill('What is the remote access idle timeout policy?');
  await page.getByRole('button', { name: 'Send question' }).click();
  await expect(page.getByText('These approved sources disagree.',{exact:false})).toBeVisible();
  await expect(page.locator('.source-card')).toHaveCount(2);
  await expect(page.locator('.advice-step')).toHaveCount(0);
});

test('passage HTML is displayed as text and failed citation fetch preserves conversation', async ({ page }) => {
  const id='11111111-1111-1111-1111-111111111111';
  await page.route('**/api/questions', route=>route.fulfill({json:{status:'supported',questions:[],steps:[{text:'Check the profile',citations:[{passageId:id,quote:'<img src=x onerror=alert(1)>'}]}],citations:[],origin:'Demo workspace',error:null}}));
  let requests=0;
  await page.route('**/api/passages/*', route=> { requests++;return requests===1?route.fulfill({json:{id,revisionId:id,title:'Fixture',version:'1',location:'test',text:'<img src=x onerror=alert(1)>'}}):route.fulfill({status:404,json:{detail:'Source was revoked.'}}); });
  await page.goto('http://127.0.0.1:5174');
  await page.getByRole('textbox',{name:'Issue description'}).fill('profile');
  await page.getByRole('button',{name:'Send question'}).click();
  await expect(page.locator('blockquote')).toContainText('<img src=x onerror=alert(1)>');
  await expect(page.locator('blockquote img')).toHaveCount(0);
  await page.getByRole('button',{name:'Open citation 1'}).click();
  await expect(page.getByRole('alert')).toContainText('Source was revoked.');
  await expect(page.getByRole('alert')).toContainText('Your conversation is preserved');
  await expect(page.locator('.advice-step')).toHaveCount(1);
});

