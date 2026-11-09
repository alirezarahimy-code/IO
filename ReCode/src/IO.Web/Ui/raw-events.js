'use strict';
(() => {
  const notice=document.getElementById('notice'),body=document.getElementById('body'),filter=document.getElementById('filter'),form=document.getElementById('create-form');
  const previous=document.getElementById('previous'),next=document.getElementById('next');let sourceId='',offset=0,count=0,busy=false,authorized=false;
  function controls(){filter.querySelector('button').disabled=busy;filter.elements.sourceId.disabled=busy;previous.disabled=busy||!authorized||offset===0;next.disabled=busy||!authorized||count<50||offset>=100000;form.querySelector('button').disabled=busy||!authorized;for(const input of form.querySelectorAll('input,textarea'))input.disabled=busy||!authorized;}
  function clear(){body.replaceChildren();document.getElementById('empty').hidden=true;document.getElementById('page-label').textContent='';authorized=false;count=0;form.reset();}
  function show(message){notice.textContent=message;}
  async function request(url,options={}){
    const response=await fetch(url,{...options,credentials:'same-origin',cache:'no-store'});
    if(!response.ok){if(response.status===401||response.status===403)clear();
      if(response.status===409){let details=null;try{details=await response.json();}catch{/* Other conflicts do not include a body. */}
        if(details?.result?.outcome===2 && details.result.conflictId) throw new Error('برخورد ثبت شد؛ شناسهٔ بررسی: '+details.result.conflictId+'؛ تلاش مجدد ممکن است برخورد دیگری ثبت کند.');
      }
      throw new Error(response.status===401?'نشست پایان یافته؛ دوباره وارد شوید.':response.status===403?'مجوز دریافت تردد ندارید یا باید گذرواژه را تغییر دهید.':response.status===400?'شناسه، زمان یا دادهٔ ورودی معتبر نیست.':response.status===409?'تعارض تراکنش؛ فهرست را بررسی کنید.':'درخواست انجام نشد؛ پیش از تلاش مجدد فهرست و برخوردها را بررسی کنید.');}
    return response.json();
  }
  async function run(action){if(busy)return;busy=true;controls();try{await action();}catch(e){show(e instanceof TypeError?'ارتباط قطع شد؛ وضعیت دریافت نامعلوم است. پیش از تلاش مجدد فهرست و برخوردها را بررسی کنید.':e.message);}finally{busy=false;controls();}}
  async function load(){const result=await request('/attendance/raw/'+encodeURIComponent(sourceId)+'?offset='+offset);authorized=true;count=result.items.length;body.replaceChildren();document.getElementById('empty').hidden=count!==0;document.getElementById('page-label').textContent='از ردیف '+(offset+1).toLocaleString('fa-IR');
    for(const item of result.items){const tr=document.createElement('tr');for(const key of ['externalEventId','sourceEmployeeIdentifier','occurredAtUtc','receivedAtUtc','reportedEventType','id']){const td=document.createElement('td');td.textContent=item[key]??'—';if(key==='externalEventId'||key==='sourceEmployeeIdentifier')td.className='identity-value';tr.append(td);}body.append(tr);}}
  filter.addEventListener('submit',e=>{e.preventDefault();run(async()=>{sourceId=filter.elements.sourceId.value.trim();offset=0;clear();await load();show('');});});
  filter.elements.sourceId.addEventListener('input',()=>{if(filter.elements.sourceId.value.trim()!==sourceId){sourceId='';clear();controls();show('برای انتخاب منبع جدید ابتدا فهرست را نمایش دهید.');}});
  previous.addEventListener('click',()=>run(async()=>{offset-=50;try{await load();show('');}catch(e){offset+=50;throw e;}}));next.addEventListener('click',()=>run(async()=>{offset+=50;try{await load();show('');}catch(e){offset-=50;throw e;}}));
  form.addEventListener('submit',e=>{e.preventDefault();run(async()=>{if(!authorized||!sourceId)return;
    const bytes=new TextEncoder().encode(form.elements.payload.value);let binary='';for(const byte of bytes)binary+=String.fromCharCode(byte);
    const token=await request('/auth/csrf');const result=await request('/attendance/simulated-events',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':token.token},body:JSON.stringify({sourceId,externalId:form.elements.externalId.value||null,personId:form.elements.personId.value||null,occurredAt:form.elements.occurredAt.value.trim()||null,sourceZone:form.elements.sourceZone.value||null,eventType:form.elements.eventType.value||null,payload:btoa(binary)})});
    const receipt=result.result;const summary=receipt.outcome===0?'رویداد ثبت شد.':receipt.outcome===1?'تکرار همسان بود؛ همان رویداد قبلی برگردانده شد.':receipt.outcome===3?'رویداد بدون شناسه ثبت شد و نیازمند بررسی است.':'پاسخ دریافت شد؛ وضعیت را بررسی کنید.';
    try{await load();show(summary+' شناسهٔ رویداد: '+receipt.eventId);}catch{show(summary+' شناسهٔ رویداد: '+receipt.eventId+'؛ دریافت فهرست ناموفق بود.');}
  });});controls();
})();
