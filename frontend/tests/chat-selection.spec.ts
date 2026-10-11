import {test,expect} from './session-fixture';
test('chat Ctrl selection, Shift range and bulk pin',async({page})=>{
 const chats=Array.from({length:4},(_,i)=>({id:`00000000-0000-0000-0000-00000000000${i}`,title:`Chat ${i}`,pinned:false,archived:false}));const actions:string[]=[];
 await page.route('**/api/documents',r=>r.fulfill({json:[]}));
 await page.route('**/api/conversations',r=>r.fulfill({json:chats}));
 await page.route('**/api/chat-projects',r=>r.fulfill({json:[]}));
 await page.route('**/api/conversations/*/action',r=>{actions.push(r.request().url());return r.fulfill({json:{status:'ok'}});});
 await page.goto('http://127.0.0.1:5174');await page.getByRole('button',{name:'Previous chats',exact:true}).click();
 const chat=(i:number)=>page.getByRole('button',{name:`Chat ${i}`,exact:true});
 await chat(0).click({modifiers:['Control']});await chat(2).click({modifiers:['Control']});
 await expect(chat(0)).toHaveAttribute('aria-pressed','true');await expect(chat(1)).toHaveAttribute('aria-pressed','false');await expect(chat(2)).toHaveAttribute('aria-pressed','true');
 await chat(3).click({modifiers:['Shift']});await expect(chat(0)).toHaveAttribute('aria-pressed','false');await expect(chat(2)).toHaveAttribute('aria-pressed','true');await expect(chat(3)).toHaveAttribute('aria-pressed','true');
 await page.keyboard.press('Tab');await chat(2).focus();await expect(chat(2)).toBeFocused();expect(await chat(2).evaluate(el=>Number.parseFloat(getComputedStyle(el).outlineOffset))).toBeLessThan(0);await page.screenshot({path:'../artifacts/chat-selection-focus.png'});await chat(2).click({button:'right'});await page.getByRole('menuitem',{name:'Pin',exact:true}).click();await expect.poll(()=>actions.length).toBe(2);
 expect(actions.some(url=>url.includes(chats[2].id))).toBeTruthy();expect(actions.some(url=>url.includes(chats[3].id))).toBeTruthy();
});

