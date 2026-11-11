'use strict';
(() => {
  const $=id=>document.getElementById(id),tf=$('type-filter'),typeForm=$('type-form'),filter=$('filter'),entryForm=$('entry-form');
  let busy=false,company='',typeOffset=0,typeCount=0,typeAuthorized=false,selection=null,entryOffset=0,entryCount=0;
  entryForm.elements.operationId.value=crypto.randomUUID();
  function controls(){for(const e of [...tf.querySelectorAll('input,button'),...filter.querySelectorAll('input,button')])e.disabled=busy;for(const e of typeForm.querySelectorAll('input,button'))e.disabled=busy||!typeAuthorized;for(const e of entryForm.querySelectorAll('input,select,button'))e.disabled=busy||!selection;$('type-prev').disabled=busy||!typeAuthorized||typeOffset===0;$('type-next').disabled=busy||!typeAuthorized||typeCount<50||typeOffset>=100000;$('entry-prev').disabled=busy||!selection||entryOffset===0;$('entry-next').disabled=busy||!selection||entryCount<50||entryOffset>=100000;}
  function clearTypes(){company='';typeAuthorized=false;typeCount=0;$('type-body').replaceChildren();$('type-empty').hidden=true;$('type-page').textContent='';typeForm.reset();}
  function clearEntries(){selection=null;entryCount=0;$('balance').hidden=true;$('balance-body').replaceChildren();$('entry-body').replaceChildren();$('entry-empty').hidden=true;$('entry-page').textContent='';}
  function show(s){$('notice').textContent=s;}
  async function request(url,options={}){const r=await fetch(url,{...options,credentials:'same-origin',cache:'no-store'});if(!r.ok){if(r.status===401||r.status===403){clearTypes();clearEntries();}throw new Error(r.status===401?'نشست پایان یافته؛ دوباره وارد شوید.':r.status===403?'مجوز مدیریت مرخصی ندارید.':r.status===400?'شرکت، کارمند، نوع، تاریخ، علامت مقدار یا سند معتبر نیست.':r.status===409?'نماد، ماندهٔ ابتدایی یا شناسهٔ عملیات با ثبت دیگری تعارض دارد.':'درخواست انجام نشد؛ پیش از تلاش مجدد وضعیت دفتر را بررسی کنید.');}return r.json();}
  async function run(fn){if(busy)return;busy=true;controls();try{await fn();}catch(e){show(e instanceof TypeError?'ارتباط قطع شد؛ وضعیت ثبت ممکن است نامعلوم باشد. با همان شناسه و داده مجدداً بررسی کنید.':e.message);}finally{busy=false;controls();}}
  async function post(url,payload){const csrf=await request('/auth/csrf');return request(url,{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':csrf.token},body:JSON.stringify(payload)});}
  async function loadTypes(){const data=await request('/leave/types/'+encodeURIComponent(company)+'?offset='+typeOffset);typeAuthorized=true;typeCount=data.items.length;$('type-body').replaceChildren();for(const item of data.items){const tr=document.createElement('tr');for(const value of [item.name,item.symbol,item.minutesPerDay,item.carryOverAllowed?'بله':'خیر',item.id]){const td=document.createElement('td');td.textContent=String(value);tr.append(td);}$('type-body').append(tr);}$('type-empty').hidden=typeCount!==0;$('type-page').textContent='از ردیف '+(typeOffset+1).toLocaleString('fa-IR');}
  async function loadEntries(){const base=encodeURIComponent(selection.employee)+'/'+encodeURIComponent(selection.type),query=new URLSearchParams({asOf:selection.asOf});const balance=await request('/leave/balance/'+base+'?'+query);const data=await request('/leave/entries/'+base+'?'+query+'&offset='+entryOffset);entryCount=data.items.length;$('balance-body').replaceChildren();for(const [label,value] of [['نوع',balance.name],['سال شمسی',balance.persianYear],['دقیقه در روز',balance.minutesPerDay],['ابتدای سال',balance.openingMinutes??'ثبت نشده'],['اعطا',balance.grantedMinutes],['مصرف دستی',balance.usedMinutes],['تعدیل',balance.adjustmentMinutes],['ماندهٔ دفتر دستی',balance.balanceMinutes??'نامشخص']]){const tr=document.createElement('tr'),th=document.createElement('th'),td=document.createElement('td');th.scope='row';th.textContent=label;td.textContent=String(value);tr.append(th,td);$('balance-body').append(tr);}$('balance').hidden=false;$('entry-body').replaceChildren();for(const item of data.items){const tr=document.createElement('tr');for(const v of [item.effectiveDate,['ابتدای سال','اعطا','مصرف','تعدیل'][item.kind]??'نامشخص',item.minutes,item.documentNumber,item.description,item.id]){const td=document.createElement('td');td.textContent=String(v);tr.append(td);}$('entry-body').append(tr);}$('entry-empty').hidden=entryCount!==0;$('entry-page').textContent='از ردیف '+(entryOffset+1).toLocaleString('fa-IR');}
  tf.addEventListener('input',()=>{clearTypes();controls();});tf.addEventListener('submit',e=>{e.preventDefault();run(async()=>{clearTypes();company=tf.elements.companyId.value.trim();typeOffset=0;await loadTypes();show('');});});
  $('type-prev').addEventListener('click',()=>run(async()=>{typeOffset-=50;try{await loadTypes();}catch(e){typeOffset+=50;throw e;}}));$('type-next').addEventListener('click',()=>run(async()=>{typeOffset+=50;try{await loadTypes();}catch(e){typeOffset-=50;throw e;}}));
  typeForm.addEventListener('submit',e=>{e.preventDefault();run(async()=>{if(!typeAuthorized)return;const result=await post('/leave/types',{companyId:company,name:typeForm.elements.name.value.trim(),symbol:typeForm.elements.symbol.value.trim(),minutesPerDay:Number(typeForm.elements.minutesPerDay.value),carryOverAllowed:typeForm.elements.carryOverAllowed.checked});typeForm.reset();try{await loadTypes();show('نوع ثبت شد؛ شناسه: '+result.result);}catch(e){show('نوع ثبت شد اما دریافت فهرست ناموفق بود: '+e.message);}});});
  filter.addEventListener('input',()=>{clearEntries();controls();});filter.addEventListener('submit',e=>{e.preventDefault();run(async()=>{clearEntries();selection={employee:filter.elements.employeeId.value.trim(),type:filter.elements.leaveTypeId.value.trim(),asOf:filter.elements.asOf.value};entryOffset=0;try{await loadEntries();show('دفتر دستی است و تأییدشده نیست.');}catch(e){clearEntries();throw e;}});});
  $('entry-prev').addEventListener('click',()=>run(async()=>{entryOffset-=50;try{await loadEntries();}catch(e){entryOffset+=50;throw e;}}));$('entry-next').addEventListener('click',()=>run(async()=>{entryOffset+=50;try{await loadEntries();}catch(e){entryOffset-=50;throw e;}}));
  entryForm.addEventListener('submit',e=>{e.preventDefault();run(async()=>{if(!selection)return;const operationId=entryForm.elements.operationId.value;const result=await post('/leave/entries',{operationId,employeeId:selection.employee,leaveTypeId:selection.type,effectiveDate:entryForm.elements.effectiveDate.value,kind:Number(entryForm.elements.kind.value),minutes:Number(entryForm.elements.minutes.value),documentNumber:entryForm.elements.documentNumber.value.trim(),description:entryForm.elements.description.value.trim()});entryForm.reset();entryForm.elements.operationId.value=crypto.randomUUID();entryOffset=0;try{await loadEntries();show('رویداد ثبت شد یا همان عملیات قبلی بود؛ شناسه: '+result.result);}catch(e){show('رویداد ثبت شد اما دریافت دفتر ناموفق بود: '+e.message);}});});controls();
})();

// Leave request inbox: scoped pending requests with approve/reject decisions.
(() => {
  const $=id=>document.getElementById(id);let busy=false,offset=0,items=[];
  const categoryText=c=>c===1?'اضافه‌کار':c===2?'مأموریت':'مرخصی';
  const kindText=k=>k===0?'روزانه':'ساعتی';
  async function request(url,options={}){const r=await fetch(url,{...options,credentials:'same-origin',cache:'no-store'});if(!r.ok)
    throw new Error(r.status===401?'نشست پایان یافته؛ دوباره وارد شوید.':r.status===403?'مجوز تصمیم مرخصی ندارید یا باید گذرواژه را تغییر دهید.':r.status===400?'اطلاعات معتبر نیست یا درخواست تصمیم‌گرفته است.':r.status===409?'تعارض رخ داد؛ کارتابل را دوباره بگیرید.':'درخواست انجام نشد.');return r.status===204?null:r.json();}
  async function post(url,data){const token=await request('/auth/csrf');return request(url,{method:'POST',headers:{'X-CSRF-TOKEN':token.token,'Content-Type':'application/json'},body:JSON.stringify(data)});}
  function controls(){$('inbox-previous').disabled=busy||offset===0;$('inbox-next').disabled=busy||items.length<50||offset>=100000;}
  function decide(id,decision){return async()=>{if(busy)return;busy=true;controls();
    try{const reason=decision===2?window.prompt('دلیل رد (اختیاری):')??'':null;
      await post('/leave/requests/'+encodeURIComponent(id)+'/decision',{decision,reason});
      $('notice').textContent=decision===1?'درخواست تأیید و از دفتر مرخصی کسر شد.':'درخواست رد شد.';
      await load();}
    catch(e){$('notice').textContent=e instanceof TypeError?'ارتباط قطع شد؛ وضعیت تصمیم نامعلوم است، کارتابل را دوباره بگیرید.':e.message;}
    finally{busy=false;controls();}};}
  function rows(){$('inbox-body').replaceChildren();$('inbox-empty').hidden=items.length!==0;
    for(const item of items){const tr=document.createElement('tr');
      for(const v of [item.workDate,categoryText(item.category)+' '+kindText(item.kind),item.minutes.toLocaleString('fa-IR'),item.reason]){
        const td=document.createElement('td');td.textContent=v;tr.append(td);}
      const td=document.createElement('td'),ok=document.createElement('button'),no=document.createElement('button');
      ok.type='button';no.type='button';ok.textContent='تأیید';no.textContent='رد';
      ok.addEventListener('click',decide(item.id,1));no.addEventListener('click',decide(item.id,2));
      td.append(ok,no);tr.append(td);$('inbox-body').append(tr);}}
  async function load(){const data=await request('/leave/inbox?offset='+offset);items=data.items;rows();controls();}
  $('inbox-previous').addEventListener('click',async()=>{if(busy)return;busy=true;controls();offset-=50;try{await load();}catch(e){offset+=50;busy=false;controls();$('notice').textContent=e.message;}});
  $('inbox-next').addEventListener('click',async()=>{if(busy)return;busy=true;controls();offset+=50;try{await load();}catch(e){offset-=50;busy=false;controls();$('notice').textContent=e.message;}});
  (async()=>{try{await load();}catch(e){$('notice').textContent=e instanceof TypeError?'ارتباط قطع شد.':e.message;controls();}})();
})();
