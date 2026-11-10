'use strict';
(() => {
  const form=document.getElementById('login-form'),notice=document.getElementById('notice');let busy=false;
  form.addEventListener('submit',async event=>{event.preventDefault();if(busy)return;busy=true;form.querySelector('button').disabled=true;notice.textContent='';const userName=form.elements.userName.value.trim(),password=form.elements.password.value;form.elements.password.value='';
    try {const csrf=await fetch('/auth/csrf',{credentials:'same-origin',cache:'no-store'});if(!csrf.ok)throw new Error('امکان شروع ورود نیست؛ دوباره تلاش کنید.');const token=(await csrf.json()).token;const response=await fetch('/auth/login',{method:'POST',credentials:'same-origin',cache:'no-store',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':token},body:JSON.stringify({userName,password})});
      if(response.ok){const me=await fetch('/auth/me',{credentials:'same-origin',cache:'no-store'});const target=me.ok&&await me.json().then(j=>j.mustChangePassword===true)?'/workspace/account':'/workspace';location.assign(target);return;}notice.textContent=response.status===429?'تلاش‌های ورود بیش از حد مجاز است؛ بعداً دوباره تلاش کنید.':response.status===401?'نام کاربری یا گذرواژه پذیرفته نشد.':'ورود انجام نشد؛ دوباره تلاش کنید.';
    }catch{notice.textContent='ارتباط برقرار نشد؛ وضعیت نشست را پیش از تلاش مجدد بررسی کنید.';}finally{busy=false;form.querySelector('button').disabled=false;}
  });
})();
