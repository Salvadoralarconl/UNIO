import {test,expect} from './session-fixture';
test('wiki library browses nested folders, reads text safely, and searches all documents',async({page})=>{
 const id='11111111-1111-1111-1111-111111111111',folder='22222222-2222-2222-2222-222222222222';
 await page.route('**/api/library',r=>r.fulfill({json:{folders:[{id:folder,name:'Printers',parentId:null}],placements:[{documentId:id,folderId:folder}]}}));
 await page.route('**/api/documents',r=>r.fulfill({json:[{id,title:'USB guide',version:'1',approval:'approved',indexing:'ready',isUploaded:true}]}));
 await page.route(`**/api/documents/${id}/text`,r=>r.fulfill({json:{text:'# Windows detection\n\nCheck Device Manager.\n\n<img src=x onerror=alert(1)>'}}));
 await page.goto('http://127.0.0.1:5174');await page.getByRole('tab',{name:'Documentation',exact:true}).click();
 await page.getByRole('button',{name:'Open folder Printers'}).click();
 await page.getByRole('button',{name:'Open document USB guide'}).first().click();
 await expect(page.getByRole('heading',{name:'USB guide',exact:true})).toBeVisible();
 await expect(page.locator('.wiki-content')).toContainText('Check Device Manager.');
 await expect(page.locator('.wiki-content img')).toHaveCount(0);
 const gaps=await page.locator('.wiki-reader').evaluate(reader=>{const paper=reader.querySelector('.wiki-paper')!;const r=reader.getBoundingClientRect(),p=paper.getBoundingClientRect();return {left:p.left-r.left,right:r.right-p.right,bottom:r.bottom-p.bottom};});
 expect(Math.abs(gaps.bottom-gaps.left)).toBeLessThanOrEqual(1);
 expect(Math.abs(gaps.bottom-gaps.right)).toBeLessThanOrEqual(1);

 await page.screenshot({path:'../artifacts/documentation-reader.png',fullPage:true});
 await page.getByRole('searchbox',{name:'Search documentation'}).fill('USB');
 await expect(page.locator('.wiki-file-list').getByRole('button',{name:'Open document USB guide'})).toBeVisible();
 await page.getByRole('searchbox',{name:'Search documentation'}).fill('absent');
 await expect(page.getByText('No documents match your search.')).toBeVisible();
 await page.screenshot({path:'../artifacts/documentation-library.png',fullPage:true});
});
test('new folders survive reload and the reader has no move dropdown',async({page})=>{
 const id='11111111-1111-1111-1111-111111111111',folder='22222222-2222-2222-2222-222222222222';
 const library:{folders:unknown[];placements:unknown[]}={folders:[],placements:[]};
 await page.route('**/api/library',r=>r.fulfill({json:library}));
 await page.route('**/api/documents',r=>r.fulfill({json:[{id,title:'Manual',version:'1',approval:'draft',indexing:'pending'}]}));
 await page.route(`**/api/documents/${id}/text`,r=>r.fulfill({json:{text:'Read this manual.'}}));
 await page.route('**/api/library/folders',async r=>{expect(r.request().postDataJSON()).toEqual({name:'Printers',parentId:null});const added={id:folder,name:'Printers',parentId:null};library.folders.push(added);await r.fulfill({json:added});});
 await page.goto('http://127.0.0.1:5174');await page.getByRole('tab',{name:'Documentation',exact:true}).click();
 await page.getByRole('button',{name:'New folder',exact:true}).first().click();
 const modal=page.getByRole('dialog',{name:'New folder'});await expect(modal.getByLabel('Folder name')).toBeFocused();
 await modal.getByLabel('Folder name').fill('Printers');await modal.getByRole('button',{name:'Create folder'}).click();
 await expect(page.getByRole('button',{name:'Open folder Printers'})).toBeVisible();
 await page.reload();await page.getByRole('tab',{name:'Documentation',exact:true}).click();
 await expect(page.getByRole('button',{name:'Open folder Printers'})).toBeVisible();
 await page.getByRole('button',{name:'Open document Manual'}).first().click();
 await expect(page.getByLabel('Move document to folder')).toHaveCount(0);
 await page.setViewportSize({width:390,height:844});
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBeTruthy();
});

test('file right click offers actions and requires confirmation for deletion',async({page})=>{
 const id='11111111-1111-1111-1111-111111111111';
 let docs=[{id,title:'USB guide',version:'1',approval:'draft',indexing:'pending'}];const calls:string[]=[];
 await page.route('**/api/library',r=>r.fulfill({json:{folders:[],placements:[]}}));
 await page.route('**/api/documents',r=>r.fulfill({json:docs}));
 await page.route(`**/api/library/files/${id}/action`,r=>{calls.push(r.request().postDataJSON().operation);docs=[];return r.fulfill({json:{folderId:null}});});
 await page.goto('http://127.0.0.1:5174');await page.getByRole('tab',{name:'Documentation',exact:true}).click();
 const file=page.getByRole('button',{name:'Open document USB guide'}).first();await file.click({button:'right'});
 const menu=page.getByRole('menu',{name:'File actions'});
 for(const name of ['Details','Copy','Cut','Rename','Move to…','Delete'])await expect(menu.getByRole('menuitem',{name,exact:true})).toBeVisible();
 await expect(menu.getByRole('menuitem',{name:'New subfolder'})).toHaveCount(0);
 await menu.getByRole('menuitem',{name:'Details',exact:true}).click();await expect(page.getByRole('dialog',{name:'File details'})).toContainText('USB guide');await page.getByRole('button',{name:'Close',exact:true}).click();
 await file.click({button:'right'});await menu.getByRole('menuitem',{name:'Delete',exact:true}).click();await page.getByRole('button',{name:'Cancel',exact:true}).click();expect(calls).toEqual([]);
 await file.click({button:'right'});await menu.getByRole('menuitem',{name:'Delete',exact:true}).click();await page.getByRole('dialog',{name:'Delete file'}).getByRole('button',{name:'Delete',exact:true}).click();
 await expect(file).toHaveCount(0);expect(calls).toEqual(['delete']);
});

