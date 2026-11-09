'use strict';
// Isolated visual prototype. The styles and interactions here do not alter the operational panel.
const showcase = document.querySelector('.showcase');
const choices = [...document.querySelectorAll('[data-choice]')];
const sidebarButtons = [...document.querySelectorAll('.sidebar-toggle')];
const veil = document.querySelector('.sidebar-veil');
const mobile = window.matchMedia('(max-width: 700px)');
function setSidebar(open) {
  showcase.dataset.sidebar = open ? 'open' : 'closed';
  for (const button of sidebarButtons) {
    button.setAttribute('aria-expanded', String(open));
    button.setAttribute('aria-label', open ? 'جمع کردن منو' : 'باز کردن منو');
  }
  document.querySelector('.sidebar-toggle-label').textContent = open ? 'جمع کردن منو' : 'باز کردن منو';
}
setSidebar(!mobile.matches);
for (const button of sidebarButtons) button.addEventListener('click', () => setSidebar(showcase.dataset.sidebar !== 'open'));
veil.addEventListener('click', () => setSidebar(false));
document.addEventListener('keydown', event => {
  if (event.key === 'Escape' && mobile.matches && showcase.dataset.sidebar === 'open') {
    setSidebar(false);
    document.querySelector('.topbar-toggle').focus();
  }
});
mobile.addEventListener('change', event => setSidebar(!event.matches));

for (const choice of choices) choice.addEventListener('click', () => {
  showcase.dataset.theme = choice.dataset.choice;
  for (const item of choices) {
    const selected = item === choice;
    item.classList.toggle('active', selected);
    item.setAttribute('aria-pressed', String(selected));
  }
});
let toastTimer;
function showDemoMessage() {
  const toast = document.getElementById('toast');
  toast.textContent = 'این یک پیش‌نمایش گرافیکی با اطلاعات ساختگی است.';
  toast.classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => toast.classList.remove('show'), 3000);
}
for (const element of document.querySelectorAll('.demo-action, .nav-item')) element.addEventListener('click', showDemoMessage);
