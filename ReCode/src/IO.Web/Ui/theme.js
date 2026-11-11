'use strict';
// Shared theme controller: load this script before the shared stylesheet in <head>.
// Theme preference is browser-local and is not an account setting or a security control.
(() => {
  const storageKey = 'io.theme.v1';
  const themes = Object.freeze([
    Object.freeze({ id: 'ocean', label: 'دریایی روشن' }),
    Object.freeze({ id: 'forest', label: 'سبز طبیعت' }),
    Object.freeze({ id: 'sand', label: 'شنی گرم' }),
    Object.freeze({ id: 'night', label: 'شب تیره' })
  ]);
  const valid = id => themes.some(theme => theme.id === id);
  const fallback = 'ocean';
  let current = fallback;
  try { const stored = localStorage.getItem(storageKey); if (valid(stored)) current = stored; } catch { /* storage may be disabled */ }
  let selector = null;
  function apply(id, persist = false) {
    if (!valid(id)) return false;
    current = id;
    document.documentElement.dataset.ioTheme = id;
    if (selector) selector.value = id;
    if (persist) { try { localStorage.setItem(storageKey, id); } catch { /* keep this page usable */ } }
    document.dispatchEvent(new CustomEvent('io:themechange', { detail: { theme: id } }));
    return true;
  }
  apply(current);
  window.IOTheme = Object.freeze({
    themes,
    get: () => current,
    set: id => apply(id, true)
  });
  function mount() {
    const bar = document.createElement('header');
    bar.className = 'io-theme-toolbar';
    const brand = document.createElement('span'); brand.className = 'io-theme-brand'; brand.textContent = 'IO | سامانهٔ یکپارچه';
    const label = document.createElement('label'); label.className = 'io-theme-picker';
    const title = document.createElement('span'); title.textContent = 'تم سامانه';
    selector = document.createElement('select'); selector.setAttribute('aria-label', 'انتخاب تم سامانه');
    for (const theme of themes) {
      const option = document.createElement('option'); option.value = theme.id; option.textContent = theme.label; selector.append(option);
    }
    selector.value = current;
    selector.addEventListener('change', () => apply(selector.value, true));
    label.append(title, selector); bar.append(brand, label);
    const help=document.createElement('a');
    const topic=location.pathname==='/login'?'login':location.pathname==='/panel'?'technical-panel':location.pathname==='/workspace'?'workspace':location.pathname.split('/')[2]||'login';
    help.href='/workspace/help/'+encodeURIComponent(topic);help.textContent='راهنمای همین صفحه';help.className='io-page-help';bar.append(help);
    document.body.prepend(bar);
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mount, { once: true }); else mount();
  window.addEventListener('storage', event => {
    if (event.key === storageKey) apply(valid(event.newValue) ? event.newValue : fallback);
    else if (event.key === null) apply(fallback);
  });
})();
