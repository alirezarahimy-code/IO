'use strict';
(() => {
  const $=id=>document.getElementById(id),form=$('filter');let busy=false,days=null;
  const columns={date:['روز کاری',d=>d.date],id:['شناسهٔ اجرا',d=>d.latestRun?.id??'بدون اجرا'],created:['زمان ثبت UTC',d=>d.latestRun?.createdAtUtc??'—'],minutes:['دقیقهٔ مشاهده‌شده',d=>d.latestRun?.observedTicks==null?'نامشخص':String(d.latestRun.observedTicks/600000000)],anomalies:['مغایرت‌ها',d=>d.latestRun?.anomaliesJson??'—'],change:['تغییر پایگاه داده',d=>d.databaseChange]};
  function clear(){days=null;$('result').hidden=true;$('head').replaceChildren();$('body').replaceChildren();$('column-message').hidden=true;}
  function render(){if(days===null)return;const selected=[...$('columns').querySelectorAll('input:checked')].map(i=>i.value).filter(x=>Object.hasOwn(columns,x));$('head').replaceChildren();$('body').replaceChildren();$('column-message').hidden=selected.length!==0;if(!selected.length)return;const header=document.createElement('tr');for(const key of selected){const th=document.createElement('th');th.scope='col';th.textContent=columns[key][0];header.append(th);}$('head').append(header);for(const day of days){const row=document.createElement('tr');for(const key of selected){const td=document.createElement('td');td.textContent=columns[key][1](day)??'—';row.append(td);}$('body').append(row);}}
  $('columns').addEventListener('change',render);form.addEventListener('input',()=>{clear();$('notice').textContent='';});
  form.addEventListener('submit',async e=>{e.preventDefault();if(busy)return;busy=true;for(const input of form.querySelectorAll('input,button'))input.disabled=true;clear();$('notice').textContent='';try{
    const params=new URLSearchParams({start:form.elements.start.value,endExclusive:form.elements.endExclusive.value});const r=await fetch('/periods/preflight/'+encodeURIComponent(form.elements.employeeId.value.trim())+'?'+params,{credentials:'same-origin',cache:'no-store'});
    if(!r.ok){$('notice').textContent=r.status===401?'نشست پایان یافته؛ دوباره وارد شوید.':r.status===403?'مجوز بررسی دوره ندارید.':r.status===400?'کارمند یا بازه معتبر نیست.':r.status===409?'تعداد اجراها بیش از حد است یا تعارض رخ داد.':'دریافت گزارش ناموفق بود.';return;}
    days=(await r.json()).days;$('result').hidden=false;render();$('notice').textContent='پیش‌نمایش تشخیصی است و تأیید نشده است.';
  }catch{clear();$('notice').textContent='ارتباط برقرار نشد؛ دوباره تلاش کنید.';}finally{busy=false;for(const input of form.querySelectorAll('input,button'))input.disabled=false;}});
})();
