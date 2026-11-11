'use strict';
// Presentation follows the same catalogue as the server. This is never an API authorization boundary.
(() => {
 const path=location.pathname,leaf=path.split('/')[2];if(!path.startsWith('/workspace')||leaf==='help')return;
 const specs={
  employees:{'create-employee':['employees.create','organization.manage'],'rename-employee-form':['employees.update','organization.manage']},
  organization:{'create-form':['organization.create','organization.manage']},
  calendars:{'create-form':['schedule.calendar.create','schedule.calendar.update','schedule.manage']},
  shifts:{'create-form':['schedule.shift.create','schedule.manage']},schedules:{'create-form':['schedule.assign','schedule.manage']},
  'employee-history':{'create-form':['organization.assign','organization.manage']},sources:{'create-form':['attendance.sources.create','attendance.ingest']},
  'raw-events':{'create-form':['attendance.events.receive','attendance.ingest']},mappings:{'create-form':['attendance.mappings.manage','attendance.ingest'],'replace-form':['attendance.mappings.manage','attendance.ingest']},
  conflicts:{'note-form':['attendance.conflicts.note','attendance.ingest']},calculations:{'create-form':['calculation.run','attendance.calculate']},rules:{'create-form':['calculation.rules.create','attendance.calculate']},
  roles:{'role-form':['identity.roles.manage','identity.manage'],'permission-form':['identity.roles.manage','identity.manage'],'scope-form':['identity.roles.manage','identity.manage'],'revoke-form':['identity.roles.manage','identity.manage']},
  users:{'create-form':['identity.users.create','identity.manage'],'state-form':['identity.users.status','identity.manage'],'password-form':['identity.users.password','identity.manage'],'role-form':['identity.roles.manage','identity.manage'],'scope-form':['identity.roles.manage','identity.manage']},
  'my-leave':{'request-form':['leave.request.submit'],'filter':['leave.read.self']},
  'leave-admin':{'type-form':['leave.types.create','leave.manage'],'entry-form':['leave.entries.record','leave.manage']},settings:{save:['settings.correction.update','settings.correction.manage']},
  'new-request':{'create-form':['correction.create.self','correction.submit.self']},'my-requests':{'create-form':['correction.create.self','correction.submit.self'],'cancel-form':['correction.cancel.self','correction.submit.self'],'edit-form':['correction.edit.self','correction.submit.self']},
  workflow:{'create-form':['correction.configure']},periods:{'create-form':['period.draft.manage'],'preflight-form':['period.preflight']}
 };
 const forms=specs[leaf]||{};let caps=null,ticket=0;
 function allowed(codes){return caps&&codes.some(c=>caps.permissions.includes(c));}
 function apply(){
  for(const [id,configured] of Object.entries(forms)){
   let codes=configured;
   if(leaf==='calendars'&&id==='create-form')codes=document.querySelector('[data-kind="days"][aria-pressed="true"]')?['schedule.calendar.update','schedule.manage']:['schedule.calendar.create','schedule.manage'];
   const form=document.getElementById(id);if(form){form.hidden=!allowed(codes);form.dataset.accessDenied=allowed(codes)?'no':'yes';}
  }
  const templates=document.getElementById('create-templates');if(templates)templates.hidden=!allowed(['identity.roles.manage','identity.manage']);
  if(caps)for(const a of document.querySelectorAll('a[href^="/workspace"],a[href="/panel"]')){
   const route=new URL(a.getAttribute('href'),location.origin).pathname;a.hidden=!(route==='/workspace/help'||route.startsWith('/workspace/help/')||caps.pages.some(p=>p.route===route));
  }
 }
 // Prevent a synthetic/client submit while the fresh grant set is unavailable or denied.
 document.addEventListener('submit',e=>{if(e.target.dataset.accessDenied==='yes'){e.preventDefault();e.stopImmediatePropagation();}},true);
 async function refresh(){const current=++ticket;try{const r=await fetch('/workspace/capabilities',{credentials:'same-origin',cache:'no-store'});if(!r.ok)throw new Error();const data=await r.json();if(current!==ticket)return;caps=data;}catch{if(current!==ticket)return;caps=null;}apply();}
 new MutationObserver(apply).observe(document.body,{subtree:true,attributes:true,attributeFilter:['aria-pressed']});
 apply();refresh();window.addEventListener('focus',refresh);setInterval(refresh,30000);
})();
