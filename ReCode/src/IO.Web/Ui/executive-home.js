'use strict';
(() => {
  const $=id=>document.getElementById(id),form=$('filter');let busy=false;
  function clear(){$('result').hidden=true;$('body').replaceChildren();}
  form.addEventListener('input',()=>{clear();$('notice').textContent='';});
  form.addEventListener('submit',async e=>{e.preventDefault();if(busy)return;busy=true;for(const input of form.querySelectorAll('input,button'))input.disabled=true;clear();$('notice').textContent='';try{
    const params=new URLSearchParams({fromUtc:form.elements.from.value+'T00:00:00Z',untilUtcExclusive:form.elements.until.value+'T00:00:00Z'});const r=await fetch('/executive/overview/'+encodeURIComponent(form.elements.companyId.value.trim())+'?'+params,{credentials:'same-origin',cache:'no-store'});
    if(!r.ok){$('notice').textContent=r.status===401?'نشست پایان یافته؛ دوباره وارد شوید.':r.status===403?'مجوز نمای مدیر ارشد ندارید.':r.status===400?'شرکت یا بازهٔ UTC معتبر نیست؛ بازه حداکثر ۶۲ روز است.':r.status===409?'تعارض هم‌زمان رخ داد؛ دوباره تلاش کنید.':'دریافت شاخص‌ها ناموفق بود.';return;}
    const result=await r.json();for(const [label,value] of [['کارکنان ثبت‌شده',result.employeeCount],['حساب‌های فعال متصل',result.activeLinkedAccountCount],['درخواست‌های اصلاح تردد در انتظار',result.pendingCorrectionCount],['رویدادهای خام دارای زمان در بازه',result.rawEventCount]]){const tr=document.createElement('tr'),th=document.createElement('th'),td=document.createElement('td');th.scope='row';th.textContent=label;td.textContent=String(value);tr.append(th,td);$('body').append(tr);}$('result').hidden=false;$('notice').textContent='شاخص‌های تشخیصی دریافت شد.';
  }catch{$('notice').textContent='ارتباط برقرار نشد؛ دوباره تلاش کنید.';}finally{busy=false;for(const input of form.querySelectorAll('input,button'))input.disabled=false;}});
})();
