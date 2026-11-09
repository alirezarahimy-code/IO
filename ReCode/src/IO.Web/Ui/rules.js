'use strict';
(() => {
  const notice=document.getElementById('notice'), body=document.getElementById('body'), filter=document.getElementById('filter'), form=document.getElementById('create-form');
  const previous=document.getElementById('previous'), next=document.getElementById('next');
  let companyId='', offset=0, count=0, busy=false, authorized=false;
  function controls(){filter.querySelector('button').disabled=busy;filter.elements.companyId.disabled=busy;previous.disabled=busy||!authorized||offset===0;next.disabled=busy||!authorized||count<50||offset>=100000;form.querySelector('button').disabled=busy||!authorized;for(const input of form.querySelectorAll('input'))input.disabled=busy||!authorized;}
  function clear(){body.replaceChildren();document.getElementById('empty').hidden=true;document.getElementById('page-label').textContent='';authorized=false;count=0;form.reset();}
  function show(message){notice.textContent=message;}
  async function request(url,options={}){
    const response=await fetch(url,{...options,credentials:'same-origin',cache:'no-store'});
    if(!response.ok){if(response.status===401||response.status===403)clear();throw new Error(response.status===401?'نشست پایان یافته؛ دوباره وارد شوید.':response.status===403?'مجوز محاسبه ندارید یا باید گذرواژه را تغییر دهید.':response.status===409?'تعارض رخ داد؛ فهرست را بررسی کنید.':response.status===400?'شناسه یا داده‌های ورودی معتبر نیست.':'درخواست انجام نشد؛ فهرست را پیش از تلاش مجدد بررسی کنید.');}
    return response.json();
  }
  async function run(action){if(busy)return;busy=true;controls();try{await action();}catch(e){show(e instanceof TypeError?'ارتباط قطع شد؛ وضعیت ثبت نامعلوم است. پیش از تلاش مجدد فهرست را بررسی کنید.':e.message);}finally{busy=false;controls();}}
  async function load(){const result=await request('/calculations/rules/'+encodeURIComponent(companyId)+'?offset='+offset);authorized=true;count=result.items.length;body.replaceChildren();document.getElementById('empty').hidden=count!==0;document.getElementById('page-label').textContent='از ردیف '+(offset+1).toLocaleString('fa-IR');
    for(const rule of result.items){const tr=document.createElement('tr');for(const key of ['name','algorithm','beforeMinutes','afterMinutes','createdAtUtc','id']){const td=document.createElement('td');td.textContent=String(rule[key]??'—');tr.append(td);}body.append(tr);}}
  filter.addEventListener('submit',event=>{event.preventDefault();run(async()=>{companyId=filter.elements.companyId.value.trim();offset=0;clear();await load();show('');});});
  // A change in the company field must not leave creation enabled for a previously selected company.
  filter.elements.companyId.addEventListener('input',()=>{if(filter.elements.companyId.value.trim()!==companyId){companyId='';clear();controls();show('برای انتخاب شرکت جدید ابتدا فهرست آن را نمایش دهید.');}});
  previous.addEventListener('click',()=>run(async()=>{offset-=50;try{await load();show('');}catch(e){offset+=50;throw e;}}));
  next.addEventListener('click',()=>run(async()=>{offset+=50;try{await load();show('');}catch(e){offset-=50;throw e;}}));
  form.addEventListener('submit',event=>{event.preventDefault();run(async()=>{if(!authorized||!companyId)return;const token=await request('/auth/csrf');await request('/calculations/rules',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':token.token},body:JSON.stringify({companyId,name:form.elements.name.value.trim(),beforeMinutes:Number(form.elements.beforeMinutes.value),afterMinutes:Number(form.elements.afterMinutes.value)})});form.reset();show('قاعده ثبت شد؛ در حال دریافت فهرست…');try{await load();show('قاعده ثبت شد؛ ممکن است در صفحهٔ دیگری باشد.');}catch{show('قاعده ثبت شد، اما دریافت فهرست ناموفق بود.');}});});
  controls();
})();
