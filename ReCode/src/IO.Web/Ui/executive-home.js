'use strict';
(() => {
  const $=id=>document.getElementById(id),form=$('filter');let busy=false;
  function clear(){$('result').hidden=true;$('body').replaceChildren();}
  function show(s){$('notice').textContent=s;}
  const iso=d=>d.toISOString().slice(0,10);
  form.elements.from.value=iso(new Date(Date.now()-6*86400000));
  form.elements.until.value=iso(new Date(Date.now()+86400000));
  (async()=>{try{const r=await fetch('/executive/companies',{credentials:'same-origin',cache:'no-store'});
    if(!r.ok)return;const data=await r.json(),list=$('company-options');list.replaceChildren();
    for(const item of data){const option=document.createElement('option');option.value=item.id;option.label=item.name;list.append(option);}}catch{/* پیشنهاد شرکت اختیاری است؛ ورود دستی شناسه کار می‌کند. */}})();
  form.addEventListener('input',()=>{clear();show('');});
  form.addEventListener('submit',async e=>{e.preventDefault();if(busy)return;busy=true;for(const input of form.querySelectorAll('input,button'))input.disabled=true;clear();show('');try{
    const params=new URLSearchParams({fromUtc:form.elements.from.value+'T00:00:00Z',untilUtcExclusive:form.elements.until.value+'T00:00:00Z'});const r=await fetch('/executive/overview/'+encodeURIComponent(form.elements.companyId.value.trim())+'?'+params,{credentials:'same-origin',cache:'no-store'});
    if(!r.ok){show(r.status===401?'نشست پایان یافته؛ دوباره وارد شوید.':r.status===403?'مجوز نمای مدیر ارشد ندارید.':r.status===400?'شرکت یا بازهٔ UTC معتبر نیست؛ بازه حداکثر ۶۲ روز است.':r.status===409?'تعارض هم‌زمان رخ داد؛ دوباره تلاش کنید.':'دریافت شاخص‌ها ناموفق بود.');return;}
    const result=await r.json();
    for(const [label,value] of [['کارکنان ثبت‌شده',result.employeeCount],['حساب‌های فعال متصل',result.activeLinkedAccountCount],
      ['درخواست‌های اصلاح در انتظار (کل شرکت)',result.pendingCorrectionCount],['درخواست‌های تأییدشده',result.approvedCorrectionCount],
      ['درخواست‌های ردشده',result.rejectedCorrectionCount],['درخواست‌های لغوشده',result.cancelledCorrectionCount],
      ['تصمیم‌های ثبت‌شده در بازه',result.correctionDecisionCount],['رویدادهای خام دارای زمان در بازه',result.rawEventCount],
      ['محاسبه‌های روزانه در بازه',result.calculationRunCount],['محاسبه‌های دارای ناهنجاری',result.anomalyRunCount],
      ['دقیقه‌های کارکرد محاسبه‌شده',result.workedMinutes],['آخرین روز محاسبه‌شده',result.latestCalculationWorkDate??'—']]){
      const tr=document.createElement('tr'),th=document.createElement('th'),td=document.createElement('td');
      th.scope='row';th.textContent=label;td.textContent=String(value);tr.append(th,td);$('body').append(tr);}
    $('result').hidden=false;show('شاخص‌های داشبورد دریافت شد.');
  }catch{show('ارتباط برقرار نشد؛ دوباره تلاش کنید.');}finally{busy=false;for(const input of form.querySelectorAll('input,button'))input.disabled=false;}});
})();
