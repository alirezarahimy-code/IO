'use strict';
(() => {
  const notice=document.getElementById('notice'),body=document.getElementById('body'),filter=document.getElementById('filter'),form=document.getElementById('create-form'),replace=document.getElementById('replace-form'),section=document.getElementById('replace-section');
  const previous=document.getElementById('previous'),next=document.getElementById('next'),cancel=document.getElementById('cancel-replace');
  let sourceId='',offset=0,count=0,busy=false,authorized=false,selected=null;
  function controls(){filter.querySelector('button').disabled=busy;filter.elements.sourceId.disabled=busy;previous.disabled=busy||!authorized||offset===0;next.disabled=busy||!authorized||count<50||offset>=100000;
    form.querySelector('button').disabled=busy||!authorized;for(const input of form.querySelectorAll('input'))input.disabled=busy||!authorized;
    for(const input of replace.querySelectorAll('input,button'))input.disabled=busy||!authorized;}
  function cancelReplace(){selected=null;section.hidden=true;replace.reset();document.getElementById('selected-mapping').textContent='';}
  function clear(){body.replaceChildren();document.getElementById('empty').hidden=true;document.getElementById('page-label').textContent='';authorized=false;count=0;form.reset();cancelReplace();}
  function show(message){notice.textContent=message;}
  async function request(url,options={}){
    const response=await fetch(url,{...options,credentials:'same-origin',cache:'no-store'});
    if(!response.ok){if(response.status===401||response.status===403)clear();const e=new Error(response.status===401?'نشست پایان یافته؛ دوباره وارد شوید.':response.status===403?'مجوز دریافت تردد ندارید یا باید گذرواژه را تغییر دهید.':response.status===409?'تعارض یا نسخهٔ قدیمی؛ فهرست را به‌روزرسانی و دوباره انتخاب کنید.':response.status===400?'شناسه، زمان یا داده‌های ورودی معتبر نیست.':'درخواست انجام نشد؛ فهرست را پیش از تلاش مجدد بررسی کنید.');e.status=response.status;throw e;}
    return response.json();
  }
  async function post(url,payload){const token=await request('/auth/csrf');return request(url,{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':token.token},body:JSON.stringify(payload)});}
  async function run(action){if(busy)return;busy=true;controls();try{await action();}catch(e){show(e instanceof TypeError?'ارتباط قطع شد؛ وضعیت ثبت نامعلوم است. پیش از تلاش مجدد فهرست را بررسی کنید.':e.message);}finally{busy=false;controls();}}
  async function load(){const result=await request('/attendance/mappings/'+encodeURIComponent(sourceId)+'?offset='+offset);authorized=true;count=result.items.length;body.replaceChildren();document.getElementById('empty').hidden=count!==0;document.getElementById('page-label').textContent='از ردیف '+(offset+1).toLocaleString('fa-IR');
    for(const item of result.items){const tr=document.createElement('tr');for(const key of ['sourcePersonId','employeeId','validFromUtc','validUntilUtcExclusive','id']){const td=document.createElement('td');td.textContent=item[key]??'—';if(key==='sourcePersonId')td.className='identity-value';tr.append(td);}
      const td=document.createElement('td'),button=document.createElement('button');button.type='button';button.textContent='انتقال';button.disabled=item.validUntilUtcExclusive!==null;button.setAttribute('aria-label','انتقال نگاشت '+item.id);
      button.addEventListener('click',()=>{if(busy)return;selected=item;section.hidden=false;document.getElementById('selected-mapping').textContent='نگاشت: '+item.id+' — شناسهٔ فرد: «'+item.sourcePersonId+'»';replace.elements.employeeId.focus();});td.append(button);tr.append(td);body.append(tr);}}
  filter.addEventListener('submit',e=>{e.preventDefault();run(async()=>{sourceId=filter.elements.sourceId.value.trim();offset=0;clear();await load();show('');});});
  filter.elements.sourceId.addEventListener('input',()=>{if(filter.elements.sourceId.value.trim()!==sourceId){sourceId='';clear();controls();show('برای انتخاب منبع جدید ابتدا فهرست را نمایش دهید.');}});
  previous.addEventListener('click',()=>run(async()=>{offset-=50;cancelReplace();try{await load();show('');}catch(e){offset+=50;throw e;}}));
  next.addEventListener('click',()=>run(async()=>{offset+=50;cancelReplace();try{await load();show('');}catch(e){offset-=50;throw e;}}));
  cancel.addEventListener('click',cancelReplace);
  form.addEventListener('submit',e=>{e.preventDefault();run(async()=>{if(!authorized||!sourceId)return;
    await post('/attendance/mappings',{sourceId,sourcePersonId:form.elements.sourcePersonId.value,employeeId:form.elements.employeeId.value.trim(),validFrom:form.elements.validFrom.value.trim(),validUntilExclusive:form.elements.validUntilExclusive.value.trim()||null});form.reset();show('نگاشت ثبت شد؛ در حال دریافت فهرست…');try{await load();show('نگاشت ثبت شد؛ ممکن است در صفحهٔ دیگری باشد.');}catch{show('نگاشت ثبت شد، اما دریافت فهرست ناموفق بود.');}});});
  replace.addEventListener('submit',e=>{e.preventDefault();run(async()=>{if(!authorized||!selected)return;
    try{await post('/attendance/mapping-replacements',{sourceId,sourcePersonId:selected.sourcePersonId,mappingId:selected.id,employeeId:replace.elements.employeeId.value.trim(),effectiveAt:replace.elements.effectiveAt.value.trim(),expectedVersion:selected.version,reason:replace.elements.reason.value.trim()});}
    catch(e){if(e.status===409)cancelReplace();throw e;}cancelReplace();show('انتقال ثبت شد؛ در حال دریافت فهرست…');try{await load();show('انتقال نگاشت ثبت شد.');}catch{show('انتقال ثبت شد، اما دریافت فهرست ناموفق بود.');}});});
  controls();
})();
