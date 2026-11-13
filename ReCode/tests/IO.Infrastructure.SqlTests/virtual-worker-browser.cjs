'use strict';
// Real Chromium -> HTTPS forms -> SQL -> production worker -> real child ->
// stateful loopback laboratory. No route/fetch/outcome/receipt mocks.
const assert=require('node:assert/strict');
const {chromium}=require('playwright-core');
const [address,user,device,version,scopeUser,scopeDevice,legacyDevice,legacyVersion]=process.argv.slice(2);
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
  await page.goto(address+'/workspace/device-settings?deviceId='+device);
  await page.locator('[data-function-keys]').first().waitFor();
  assert.match(await page.locator('[data-network-report]').first().textContent(),/255.255.255.0/);
  assert.match(await page.locator('#items').textContent(),/virtual-key-not-ZK/);
  assert.match(await page.locator('#items').textContent(),/Index 3.*گزارش نشده/);
  const completed=[];
  for(let run=0;run<4;run++){
   const target=run<2?device:legacyDevice,pinnedVersion=run<2?version:legacyVersion;
   await page.goto(address+'/workspace/device-attendance?deviceId='+target+'&version='+encodeURIComponent(pinnedVersion));
   const form=page.locator('#extract-form');await form.waitFor({state:'visible'});
   await form.locator('select[name=readFormat]').selectOption(run<2?'ssr-v1':'legacy-int32-minute-v1');
   await form.locator('input[name=fromUtc]').fill('2026-11-08T00:00:00Z');
   await form.locator('input[name=untilUtcExclusive]').fill('2026-11-09T00:00:00Z');
   await form.locator('input[name=inCode]').fill('4');await form.locator('input[name=outCode]').fill('5');
   const id=await form.locator('input[name=operationId]').inputValue();completed.push(id);
   await form.locator('button').click();await page.locator('#notice').filter({hasText:'SQL ثبت شد'}).waitFor();
   // Query the actual authenticated API for readiness; no mutation outside forms.
   // waitForFunction's synchronous predicate sees a Promise as truthy before its
   // false result is resolved. Poll completed browser fetches, not Promise objects.
   const deadline=Date.now()+60000;let ready=false;
   while(Date.now()<deadline){
    const body=await page.evaluate(async id=>{const r=await fetch('/device-attendance-downloads/'+id+'/rows?after=0',{cache:'no-store'});if(!r.ok)throw Error('Readiness HTTP '+r.status);return r.json();},id);
    if(body.state!=='pending'&&body.state!=='running'){
     assert.equal(body.state,'succeeded');assert.equal(body.receivedCount,103);assert.equal(body.readFormat,run<2?'ssr-v1':'legacy-int32-minute-v1');ready=true;break;
    }
    await page.waitForTimeout(100);
   }
   assert(ready,'Timed out waiting for actual worker extraction');
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
      const rows=JSON.parse(document.querySelector('#rows').textContent||'[]');return rows[0]?.sequence===after+51&&!document.querySelector('#more-rows').disabled;
    },after);}
   }
  }
  assert.notEqual(completed[0],completed[1]);
  // Approve real non-TFT polling through the actual schedule form. The live
  // production worker ticks the schedule, reads the child and reviews raw pages.
  page.on('dialog',dialog=>dialog.accept());
  await page.goto(address+'/workspace/device-schedules?deviceId='+legacyDevice+'&version='+encodeURIComponent(legacyVersion));
  const schedule=page.locator('#schedule-form');await schedule.waitFor({state:'visible'});
  await schedule.locator('select[name=readFormat]').selectOption('legacy-int32-minute-v1');
  await schedule.locator('input[name=startUtc]').fill('2026-11-08T08:00:00Z');
  await schedule.locator('input[name=everySeconds]').fill('86400');await schedule.locator('input[name=windowSeconds]').fill('3600');
  await schedule.locator('input[name=lagSeconds]').fill('0');await schedule.locator('input[name=overlapSeconds]').fill('1800');
  await schedule.locator('input[name=timeoutSeconds]').fill('10');await schedule.locator('input[name=inCode]').fill('4');await schedule.locator('input[name=outCode]').fill('5');
  await schedule.locator('input[name=autoCommit]').check();await schedule.locator('input[name=confirmed]').check();
  const scheduleId=await schedule.locator('input[name=id]').inputValue();await schedule.locator('button').click();
  await page.locator('#notice').filter({hasText:'SQL ثبت شد'}).waitFor();
  const pollDeadline=Date.now()+90000;let acknowledged=false;
  while(Date.now()<pollDeadline){
   const body=await page.evaluate(async id=>{const r=await fetch('/device-poll-schedules/'+id+'/runs?offset=0',{cache:'no-store'});if(r.status===409)return null;if(!r.ok)throw Error('Poll HTTP '+r.status);return r.json();},scheduleId);
   if(body?.items[0]?.outcome==='succeeded'){assert.equal(body.items[0].readFormat,'legacy-int32-minute-v1');assert.equal(body.items[0].nextAfter,103);assert.equal(body.items[0].receivedCount,103);acknowledged=true;break;}
   // Readiness GET may deliberately conflict with serializable worker review.
   // Retry only this read, never the schedule-creation or pause POST.
   await page.waitForTimeout(500);
  }
  assert(acknowledged,'Legacy polling did not complete acquisition and approved automatic review');
  await page.locator('#refresh').click();const scheduleRow=page.locator('#schedules li').filter({hasText:scheduleId});
  await scheduleRow.filter({hasText:'2026-11-08T09:00:00'}).waitFor();
  await scheduleRow.getByRole('button',{name:'توقف مصوب'}).click();await scheduleRow.filter({hasText:'متوقف'}).waitFor();
  // Exercise actual operator-scope administration forms with the explicitly
  // granted human administrator, targeting the OTHER (worker) identity.
  await page.goto(address+'/workspace/devices');await page.locator('#device-access').waitFor({state:'visible'});
  await page.locator('#scope-operator input[name=userId]').fill(scopeUser);await page.locator('#scope-operator button').click();
  await page.locator('#scope-policy').filter({hasText:scopeUser}).waitFor();
  await page.locator('#scope-mode select[name=restricted]').selectOption('true');await page.locator('#scope-mode input[name=confirmed]').check();
  await page.locator('#scope-mode button').click();await page.locator('#scope-notice').filter({hasText:'دامنه ثبت شد'}).waitFor();
  await page.locator('#scope-grant input[name=deviceId]').fill(scopeDevice);await page.locator('#scope-grant button').click();
  await page.locator('#scope-notice').filter({hasText:'انتساب دستگاه ثبت شد'}).waitFor();
  await page.locator('#scope-grants button').click();await page.locator('#scope-notice').filter({hasText:'دسترسی لغو شد'}).waitFor();
  await page.locator('#scope-grant button').click();await page.locator('#scope-notice').filter({hasText:'انتساب دستگاه ثبت شد'}).waitFor();
  assert.equal(await page.locator('#scope-grants li').count(),2);
  assert.equal(await page.locator('#scope-grants button').count(),1);

  console.log('Virtual browser device flow passed.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1);});
