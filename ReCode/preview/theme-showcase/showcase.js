'use strict';
// Isolated visual prototype. The styles and interactions here do not alter the operational panel.
const showcase = document.querySelector('.showcase');
const choices = [...document.querySelectorAll('[data-choice]')];
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
