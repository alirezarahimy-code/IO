'use strict';
(() => {
  const $=id=>document.getElementById(id),form=$('filter');let busy=false;
  function clear(){$('result').hidden=true;$('body').replaceChildren();}
  form.addEventListener('input',()=>{clear();$('notice').textContent='';});
  form.addEventListener('submit',async e=>{e.preventDefault();if(busy)return;busy=true;for(const input of form.querySelectorAll('input,button'))input.disabled=true;clear();$('notice').textContent='';try{
    const params=new URLSearchParams({start:form.elements.start.value,endExclusive:form.elements.endExclusive.value});const r=await fetch('/periods/preflight/'+encodeURIComponent(form.elements.employeeId.value.trim())+'?'+params,{credentials:'same-origin',cache:'no-store'});
    if(!r.ok){$('notice').textContent=r.status===401?'نشست پایان یافته؛ دوباره وارد شوید.':r.status===403?'مجوز بررسی دوره ندارید.':r.status===400?'کارمند یا بازه معتبر نیست؛ بازه حداکثر ۶۲ روز است.':r.status===409?'تعداد اجراها بیش از حد است یا تعارض رخ داد.':'دریافت گزارش ناموفق بود.';return;}
    const report=await r.json();for(const day of report.days){const run=day.latestRun,tr=document.createElement('tr');for(const value of [day.date,run?.id??'بدون اجرا',run?.createdAtUtc??'—',run?.observedTicks==null?'نامشخص':String(run.observedTicks/600000000),run?.anomaliesJson??'—',day.databaseChange]){const td=document.createElement('td');td.textContent=value??'—';tr.append(td);}$('body').append(tr);}$('result').hidden=false;$('notice').textContent='گزارش تشخیصی است و تأیید نشده است.';
  }catch{$('notice').textContent='ارتباط برقرار نشد؛ دوباره تلاش کنید.';}finally{busy=false;for(const input of form.querySelectorAll('input,button'))input.disabled=false;}});
})();
