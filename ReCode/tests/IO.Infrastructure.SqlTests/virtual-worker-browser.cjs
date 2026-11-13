'use strict';
// Real Chromium -> HTTPS forms -> SQL -> production worker -> real child ->
// stateful loopback laboratory. No route/fetch/outcome/receipt mocks.
const assert=require('node:assert/strict');
const {chromium}=require('playwright-core');
const [address,user,device,version]=process.argv.slice(2);
if(!address?.startsWith('https://127.0.0.1:')||!user?.startsWith('virtual-operator-'))throw Error('Disposable virtual laboratory only');
(async()=>{
 const browser=await chromium.launch({executablePath:process.env.IO_CHROME_BINARY,headless:true,args:['--no-sandbox']});
 try{
  const context=await browser.newContext({ignoreHTTPSErrors:true});const page=await context.newPage();page.setDefaultTimeout(60000);
  await page.goto(address+'/login');
  await page.locator('#login-form input[name=userName]').fill(user);
  await page.locator('#login-form input[name=password]').fill('Synthetic!Password-2026');
  await page.locator('#login-form button').click();await page.waitForURL('**/workspace');
  const errors=[];page.on('pageerror',e=>errors.push(e.message));page.on('response',r=>{if(r.status()>=400)errors.push(r.status()+' '+new URL(r.url()).pathname);});
  const completed=[];
  for(let run=0;run<2;run++){
   await page.goto(address+'/workspace/device-attendance?deviceId='+device+'&version='+encodeURIComponent(version));
   const form=page.locator('#extract-form');await form.waitFor({state:'visible'});
   await form.locator('input[name=fromUtc]').fill('2026-11-08T00:00:00Z');
   await form.locator('input[name=untilUtcExclusive]').fill('2026-11-09T00:00:00Z');
   await form.locator('input[name=inCode]').fill('4');await form.locator('input[name=outCode]').fill('5');
   const id=await form.locator('input[name=operationId]').inputValue();completed.push(id);
   await form.locator('button').click();await page.locator('#notice').filter({hasText:'SQL ثبت شد'}).waitFor();
   // Query the actual authenticated API for readiness; no mutation outside forms.
   await page.waitForFunction(async id=>{
    const r=await fetch('/device-attendance-downloads/'+id+'/rows?after=0');if(!r.ok)return false;
    const body=await r.json();if(body.state==='pending'||body.state==='running')return false;
    if(body.state!=='succeeded')throw Error('Worker extraction terminal: '+body.state);return body.receivedCount===103;
   },id);
   await page.goto(address+'/workspace/device-attendance?downloadId='+id);
   try{await page.locator('#snapshot').filter({hasText:'succeeded'}).waitFor({timeout:15000});}
   catch{throw Error('Device preview failed: '+JSON.stringify(await page.evaluate(()=>({url:location.pathname+location.search,notice:document.querySelector('#notice')?.textContent,snapshot:document.querySelector('#snapshot')?.textContent,previewHidden:document.querySelector('#preview')?.hidden})))+' script/errors='+JSON.stringify(errors));}
   for(let after=0;after<103;after+=50){
    const commit=page.locator('#commit-form');await commit.waitFor({state:'visible'});
    await commit.locator('input[name=confirmed]').check();await commit.locator('button').click();
    await page.locator('#notice').filter({hasText:'همان transaction'}).waitFor();
    await page.waitForFunction(()=>document.querySelector('#commit-form input[name=confirmed]').checked===false&&!document.querySelector('#commit-form button').disabled);
    const rows=JSON.parse(await page.locator('#rows').textContent());
    assert.equal(rows.length,after===100?3:50);assert(rows.every(x=>x.rawEventId));
    if(after<100){await page.locator('#more-rows').click();await page.waitForFunction(after=>{
      const rows=JSON.parse(document.querySelector('#rows').textContent()||'[]');return rows[0]?.sequence===after+51&&!document.querySelector('#more-rows').disabled;
    },after);}
   }
  }
  assert.notEqual(completed[0],completed[1]);
  console.log('Virtual browser device flow passed.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1);});
