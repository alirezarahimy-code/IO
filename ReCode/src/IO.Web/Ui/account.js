'use strict';
(() => {
  const $=id=>document.getElementById(id),form=$('password-form');let busy=false,authorized=false;
  function controls(){for(const e of form.querySelectorAll('input,button'))e.disabled=busy||!authorized;$('logout').disabled=busy||!authorized;}
  function clear(){authorized=false;$('account-id').textContent='—';form.reset();}
  function show(s){$('notice').textContent=s;}
  async function request(url,options={}){const r=await fetch(url,{...options,credentials:'same-origin',cache:'no-store'});if(!r.ok){if(r.status===401||r.status===403)clear();const e=new Error(r.status===401?'نشست پایان یافته یا حساب غیرفعال است؛ دوباره وارد شوید.':r.status===429?'تعداد تلاش‌های تغییر گذرواژه بیش از حد مجاز است؛ بعداً تلاش کنید.':r.status===400?'گذرواژهٔ فعلی یا گذرواژهٔ جدید پذیرفته نشد.':'درخواست انجام نشد؛ وضعیت حساب را بررسی کنید.');throw e;}return r.status===204?null:r.json();}
  async function run(action){if(busy)return;busy=true;controls();try{await action();}catch(e){show(e instanceof TypeError?'ارتباط قطع شد؛ وضعیت عملیات نامعلوم است. پیش از تلاش مجدد وضعیت نشست را بررسی کنید.':e.message);}finally{busy=false;controls();}}
  form.addEventListener('submit',event=>{event.preventDefault();run(async()=>{if(!authorized)return;const currentPassword=form.elements.currentPassword.value,newPassword=form.elements.newPassword.value,confirm=form.elements.confirm.value;form.reset();if(newPassword!==confirm){show('تکرار گذرواژهٔ جدید یکسان نیست.');return;}const token=await request('/auth/csrf');await request('/auth/change-password',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':token.token},body:JSON.stringify({currentPassword,newPassword})});clear();show('گذرواژه تغییر کرد و نشست پایان یافت؛ با گذرواژهٔ جدید وارد شوید.');});});
  $('logout').addEventListener('click',()=>run(async()=>{if(!authorized)return;const token=await request('/auth/csrf');await request('/auth/logout',{method:'POST',headers:{'X-CSRF-TOKEN':token.token}});clear();show('از حساب خارج شدید.');}));controls();run(async()=>{const data=await request('/auth/me');$('account-id').textContent=data.id;authorized=true;show('');});
})();
