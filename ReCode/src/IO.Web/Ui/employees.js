'use strict';
(() => {
  const notice = document.getElementById('notice');
  const rows = document.getElementById('employee-rows');
  const company = document.getElementById('company');
  const createForm = document.getElementById('create-employee');
  const renameForm = document.getElementById('rename-employee-form');
  const renameSection = document.getElementById('rename-section');
  const previous = document.getElementById('previous');
  const next = document.getElementById('next');
  const companyPrevious = document.getElementById('company-previous');
  const companyNext = document.getElementById('company-next');
  const refresh = document.getElementById('refresh');
  const controls = [previous, next, companyPrevious, companyNext, refresh, company, createForm.elements.personnelNumber, createForm.elements.displayName, createForm.querySelector('button'), renameForm.elements.displayName, renameForm.querySelector('button'), document.getElementById('cancel-rename')];
  let offset = 0, companyOffset = 0, employeeCount = 0, companyCount = 0, selected = null, busy = false;
  const setNotice = text => { notice.textContent = text; };
  function updateControls() {
    controls.forEach(control => { control.disabled = busy; });
    previous.disabled = busy || offset === 0;
    next.disabled = busy || employeeCount < 100 || offset >= 1000000;
    companyPrevious.disabled = busy || companyOffset === 0;
    companyNext.disabled = busy || companyCount < 100 || companyOffset >= 1000000;
    createForm.querySelector('button').disabled = busy || !company.value;
  }
  function clearSensitive() {
    rows.replaceChildren(); company.replaceChildren(new Option('شرکتی در دسترس نیست', '')); companyCount = 0; employeeCount = 0;
    selected = null; renameSection.hidden = true; document.getElementById('rename-employee').textContent = '';
    createForm.reset(); renameForm.reset(); document.getElementById('empty').hidden = true;
  }
  async function request(url, options = {}) {
    const response = await fetch(url, { ...options, credentials:'same-origin', cache:'no-store' });
    if (!response.ok) {
      if (response.status === 401 || response.status === 403) clearSensitive();
      const message = response.status === 401 ? 'نشست شما پایان یافته است؛ دوباره از پنل وارد شوید.' :
        response.status === 403 ? 'دسترسی به مدیریت سازمان ندارید یا باید ابتدا گذرواژه را تغییر دهید.' :
        response.status === 409 ? 'تعارض یا تغییر هم‌زمان رخ داد؛ اطلاعات را به‌روزرسانی و دوباره انتخاب کنید.' :
        response.status === 400 ? 'اطلاعات یا درخواست معتبر نیست؛ فیلدها را بررسی کنید.' : 'دریافت یا ثبت اطلاعات انجام نشد. دوباره تلاش کنید.';
      const error = new Error(message); error.status = response.status; throw error;
    }
    return response.status === 204 ? null : response.json();
  }
  async function post(url, body) {
    const csrf = await request('/auth/csrf');
    return request(url, { method:'POST', headers:{'Content-Type':'application/json','X-CSRF-TOKEN':csrf.token}, body:JSON.stringify(body) });
  }
  function cancelRename() { selected = null; renameSection.hidden = true; renameForm.reset(); document.getElementById('rename-employee').textContent = ''; }
  async function loadEmployees() {
    const result = await request('/org/catalog/employees?offset=' + offset);
    employeeCount = result.items.length; rows.replaceChildren();
    document.getElementById('page-label').textContent = 'از ردیف ' + (offset + 1).toLocaleString('fa-IR');
    document.getElementById('empty').hidden = employeeCount !== 0;
    for (const item of result.items) {
      const tr = document.createElement('tr');
      for (const value of [item.personnelNumber, item.displayName, item.companyId, item.id]) {
        const td = document.createElement('td'); td.textContent = value; tr.append(td);
      }
      const td = document.createElement('td'); const button = document.createElement('button');
      button.type = 'button'; button.textContent = 'تغییر نام'; button.setAttribute('aria-label', 'تغییر نام ' + item.displayName);
      button.addEventListener('click', () => { if (busy) return; selected = item; renameForm.elements.displayName.value = item.displayName;
        document.getElementById('rename-employee').textContent = item.personnelNumber + ' — ' + item.displayName;
        renameSection.hidden = false; renameForm.elements.displayName.focus(); });
      td.append(button); tr.append(td); rows.append(tr);
    }
  }
  async function loadCompanies() {
    const result = await request('/org/catalog/companies?offset=' + companyOffset);
    companyCount = result.items.length; company.replaceChildren(new Option('انتخاب شرکت', ''));
    for (const item of result.items) company.add(new Option(item.name + ' — ' + item.id, item.id));
    document.getElementById('company-page').textContent = 'از ردیف ' + (companyOffset + 1).toLocaleString('fa-IR');
  }
  async function perform(action) {
    if (busy) return;
    busy = true; updateControls();
    try { await action(); } catch (error) { setNotice(error instanceof Error ? error.message : 'خطای ارتباط با سرور.'); }
    finally { busy = false; updateControls(); }
  }
  refresh.addEventListener('click', () => perform(async () => { cancelRename(); await loadCompanies(); await loadEmployees(); setNotice('اطلاعات به‌روزرسانی شد.'); }));
  previous.addEventListener('click', () => perform(async () => { offset = Math.max(0, offset - 100); cancelRename(); try { await loadEmployees(); setNotice(''); } catch(e) { offset += 100; throw e; } }));
  next.addEventListener('click', () => perform(async () => { offset += 100; cancelRename(); try { await loadEmployees(); setNotice(''); } catch(e) { offset -= 100; throw e; } }));
  companyPrevious.addEventListener('click', () => perform(async () => { companyOffset = Math.max(0, companyOffset - 100); try { await loadCompanies(); setNotice(''); } catch(e) { companyOffset += 100; throw e; } }));
  companyNext.addEventListener('click', () => perform(async () => { companyOffset += 100; try { await loadCompanies(); setNotice(''); } catch(e) { companyOffset -= 100; throw e; } }));
  company.addEventListener('change', updateControls);
  document.getElementById('cancel-rename').addEventListener('click', cancelRename);
  createForm.addEventListener('submit', event => { event.preventDefault(); perform(async () => {
    if (!company.value) { setNotice('ابتدا شرکت را انتخاب کنید.'); return; }
    await post('/org/employees', { companyId:company.value, personnelNumber:createForm.elements.personnelNumber.value.trim(), displayName:createForm.elements.displayName.value.trim() });
    createForm.reset(); setNotice('کارمند ثبت شد. در حال به‌روزرسانی فهرست…');
    try { await loadEmployees(); setNotice('کارمند ثبت شد. ممکن است در صفحهٔ دیگری از فهرست باشد.'); }
    catch { setNotice('کارمند ثبت شد، اما دریافت مجدد فهرست ناموفق بود؛ از به‌روزرسانی استفاده کنید.'); }
  }); });
  renameForm.addEventListener('submit', event => { event.preventDefault(); perform(async () => {
    if (!selected) return;
    try { await post('/org/employees/' + encodeURIComponent(selected.id) + '/rename', { displayName:renameForm.elements.displayName.value.trim(), expectedVersion:selected.version }); }
    catch (error) { if (error.status === 409) cancelRename(); throw error; }
    cancelRename(); setNotice('نام ثبت شد. در حال به‌روزرسانی فهرست…');
    try { await loadEmployees(); setNotice('نام کارمند تغییر کرد.'); }
    catch { setNotice('نام ثبت شد، اما دریافت مجدد فهرست ناموفق بود؛ از به‌روزرسانی استفاده کنید.'); }
  }); });
  perform(async () => { await loadCompanies(); await loadEmployees(); setNotice(''); });
})();
