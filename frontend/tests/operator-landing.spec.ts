import {test,expect} from './session-fixture';
import {operatorFixture} from './account-test-fixture';

for(const viewport of [{width:1280,height:720},{width:390,height:844}]){
 test(`Operator content scrolls with fixed navigation at ${viewport.width}px`,async({page})=>{
  await page.setViewportSize(viewport);await operatorFixture(page);
  await page.goto('http://127.0.0.1:5174/');
  await expect(page.getByText('pending@example.test',{exact:true})).toBeVisible();
  const content=page.locator('.operator-dashboard'),toolbar=page.locator('.workspace-toolbar');
  const before=await toolbar.boundingBox();
  const railBefore=await page.locator('.icon-rail').boundingBox(),tabsBefore=await page.locator('.module-tabs').boundingBox();
  await content.evaluate(element=>element.scrollTo(0,element.scrollHeight));
  await expect.poll(()=>content.evaluate(element=>element.scrollTop)).toBeGreaterThan(0);
  const invitation=await page.getByText('pending@example.test',{exact:true}).boundingBox();
  expect(invitation!.y+invitation!.height).toBeLessThanOrEqual(viewport.height);
  expect(await toolbar.boundingBox()).toEqual(before);
  expect(await page.locator('.icon-rail').boundingBox()).toEqual(railBefore);
  expect(await page.locator('.module-tabs').boundingBox()).toEqual(tabsBefore);
  expect(await page.evaluate(()=>window.scrollY)).toBe(0);
  await page.getByRole('button',{name:'Companies',exact:true}).click();
  await expect.poll(()=>page.locator('#operator-companies').evaluate(element=>element.getBoundingClientRect().top)).toBeGreaterThanOrEqual(before!.y+before!.height);
  await expect.poll(()=>page.locator('#operator-companies').evaluate(element=>element.getBoundingClientRect().top)).toBeLessThanOrEqual(before!.y+before!.height+30);
  await page.getByRole('button',{name:'Users',exact:true}).click();
  await expect.poll(()=>page.locator('#operator-users').evaluate(element=>Math.round(element.getBoundingClientRect().top))).toBeGreaterThanOrEqual(Math.floor(before!.y+before!.height));
  await expect.poll(()=>page.locator('#operator-users').evaluate(element=>element.getBoundingClientRect().top)).toBeLessThanOrEqual(before!.y+before!.height+30);
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  await content.evaluate(element=>element.scrollTo(0,element.scrollHeight));
  await page.screenshot({path:`../artifacts/operator-scroll-${viewport.width}.png`});
 });
}
test('Operator root opens companies and their users immediately',async({page})=>{
 await operatorFixture(page);
 await page.goto('http://127.0.0.1:5174/');
 await expect(page.getByRole('heading',{name:'Companies',exact:true})).toBeVisible();
 await expect(page.getByRole('heading',{name:'Company accounts',exact:true})).toBeVisible();
 await expect(page.getByText('Alex Admin',{exact:true})).toBeVisible();
 await expect(page.getByText('Evan Editor',{exact:true})).toBeVisible();
 await expect(page.getByText('No active company membership. Contact your administrator.')).toHaveCount(0);
 await page.screenshot({path:'../artifacts/operator-landing-desktop.png',fullPage:true});
 await page.setViewportSize({width:390,height:844});
 await page.screenshot({path:'../artifacts/operator-landing-mobile.png',fullPage:true});
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
});

test('Operator account controls live in Settings instead of a top strip',async({page})=>{
 await operatorFixture(page);await page.goto('http://127.0.0.1:5174/');
 await expect(page.getByRole('heading',{name:'Companies',exact:true})).toBeVisible();
 await expect(page.locator('.company-session-bar')).toHaveCount(0);
 await expect(page.getByRole('button',{name:'Sign out',exact:true})).toHaveCount(0);
 await page.getByRole('button',{name:'Account settings',exact:true}).click();
 await expect(page.getByRole('dialog',{name:'Account settings'})).toBeVisible();
 await expect(page.getByRole('button',{name:'Sign out',exact:true})).toBeVisible();
 await page.keyboard.press('Escape');await expect(page.getByRole('dialog')).toHaveCount(0);
});
