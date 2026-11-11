'use strict';
(() => {
  const $=id=>document.getElementById(id),form=$('reverse-form'),params=new URLSearchParams(location.search),own=params.get('own');
  let kind=params.get('kind')==='leave'?'leave':'correction',offset=0,count=0,busy=false,selected=null,canReverse=false;
  const states=['در انتظار','تأیید','رد','لغو'];$('kind').value=kind;
  function controls(){for(const e of document.querySelectorAll('button,input,textarea,select'))e.disabled=busy;$('kind').disabled=busy||!!own;$('previous').disabled=busy||offset===0;$('next').disabled=busy||count<50||offset>=100000;}
  function reset(){pendingPayload=null;selected=null;$('reverse-section').hidden=true;form.reset();}
  async function request(url,options={}){const r=await fetch(url,{...options,credentials:'same-origin',cache:'no-store'});if(!r.ok){if(r.status===401||r.status===403){$('body').replaceChildren();reset();}throw new Error(r.status===403?'مجوز یا دامنهٔ جاری کافی نیست؛ خود شخص و جانشین مجاز به بازگشت نیستند.':r.status===401?'دوباره وارد شوید.':r.status===409?'تصمیم تغییر کرده، قبلاً بازگشته، شاهد دفتر قابل تطبیق نیست یا پیش‌نویس دوره وجود دارد. تاریخچه را بررسی کنید؛ هیچ اثر ناقصی ثبت نشده است.':r.status===400?'شناسه و دلیل بازگشت را بررسی کنید.':'عملیات انجام نشد؛ ابتدا وضعیت را بررسی کنید.');}return r.json();}
  async function run(fn){if(busy)return;busy=true;controls();try{await fn();}catch(e){$('notice').textContent=e instanceof TypeError?'ارتباط قطع شد؛ وضعیت نامعلوم است. داده و شناسه را حفظ و تاریخچه را بررسی کنید.':e.message;}finally{busy=false;controls();}}
  async function load(){const root=kind==='leave'?'/leave':'/corrections',url=own?root+'/requests/'+encodeURIComponent(own)+'/history':root+'/reversals';
    const data=await request(url+'?offset='+offset);if(selected&&!data.items.some(x=>x.decisionId===selected.decisionId&&x.canReverse))reset();count=data.items.length;$('body').replaceChildren();$('empty').hidden=count!==0;$('page-label').textContent='از ردیف '+(offset+1);$('history-context').textContent=own?'تاریخچهٔ درخواست خودم: '+own:'تصمیم‌های کارکنان در محدودهٔ مجاز جاری';
    for(const x of data.items){const tr=document.createElement('tr');for(const v of [own??(x.requestId+' / '+x.employeeId+' / '+x.workDate),x.sequence+' — '+(states[x.decision]??x.decision),(x.decisionReason??x.reason??'—')+' / '+x.atUtc,x.reversalId?(x.reversalReason+' / '+x.reversedAtUtc+' / '+x.reversedByUserId):'بازگشت ندارد',[x.ledgerEntryId,x.compensationLedgerEntryId].filter(Boolean).join(' / ')||'—']){const td=document.createElement('td');td.textContent=v;tr.append(td);}
      const td=document.createElement('td');if(!own&&canReverse&&x.canReverse&&x.decision!==3){const b=document.createElement('button');b.type='button';b.textContent='بازگشت با دلیل';b.addEventListener('click',()=>{if(busy)return;reset();selected=x;form.elements.operationId.value=crypto.randomUUID();$('selected').textContent=x.requestId+' — تصمیم '+x.decisionId;$('reverse-section').hidden=false;form.elements.reason.focus();});td.append(b);}tr.append(td);$('body').append(tr);}}
  $('refresh').addEventListener('click',()=>run(async()=>{reset();await authorize();await load();$('notice').textContent='به‌روز شد.';}));
  $('kind').addEventListener('change',()=>run(async()=>{kind=$('kind').value;offset=0;reset();await authorize();await load();}));
  for(const [id,delta] of [['previous',-50],['next',50]])$(id).addEventListener('click',()=>run(async()=>{offset+=delta;reset();try{await load();}catch(e){offset-=delta;throw e;}}));
  $('close').addEventListener('click',reset);
  let pendingPayload=null;
  form.addEventListener('submit',e=>{e.preventDefault();run(async()=>{if(!selected||!canReverse)return;const payload={operationId:form.elements.operationId.value,expectedDecisionId:selected.decisionId,reason:form.elements.reason.value};
    const key=JSON.stringify(payload);if(pendingPayload&&pendingPayload!==key)throw new Error('در وضعیت نامعلوم داده را تغییر ندهید؛ همان عملیات را تکرار یا تاریخچه را به‌روز کنید.');pendingPayload=key;
    const token=await request('/auth/csrf');await request((kind==='leave'?'/leave':'/corrections')+'/requests/'+encodeURIComponent(selected.requestId)+'/reverse',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':token.token},body:key});pendingPayload=null;reset();await load();$('notice').textContent='بازگشت ثبت شد؛ درخواست در کارتابل منتظر تصمیم مجدد است. ماندهٔ دفتر و اعلان را به‌روزرسانی کنید.';});});
  async function authorize(){const caps=await request('/workspace/capabilities');canReverse=caps.permissions.includes(kind==='leave'?'leave.request.reverse':'correction.reverse');}
  function refreshAccess(){run(async()=>{await authorize();await load();});}
  window.addEventListener('focus',refreshAccess);setInterval(refreshAccess,30000);
  controls();refreshAccess();
})();
