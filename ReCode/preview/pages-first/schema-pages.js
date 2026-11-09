'use strict';
// UI-only schemas derived from current input records and storage records; data is synthetic.
// IDs, rowversion, and server-owned timestamps are not editable create inputs.
window.IOSchemaPages = {
  org: [
    { key:'organizations', title:'سازمان‌ها', record:'OrganizationRecord', contract:'OrganizationInput', columns:['نام سازمان','شناسهٔ نمونه'], rows:[['سازمان نمونه','ORG-01']], fields:[['name','نام سازمان','text',200,true]] },
    { key:'companies', title:'شرکت‌ها', record:'CompanyRecord', contract:'CompanyInput', columns:['نام شرکت','سازمان نمونه'], rows:[['شرکت نمونه','سازمان نمونه']], fields:[['organizationId','سازمان','select',0,true],['name','نام شرکت','text',200,true]] },
    { key:'sites', title:'سایت‌ها', record:'SiteRecord', contract:'SiteInput', columns:['نام سایت','شرکت','منطقهٔ زمانی'], rows:[['سایت مرکزی','شرکت نمونه','Asia/Tehran']], fields:[['companyId','شرکت','select',0,true],['name','نام سایت','text',200,true],['timeZoneId','شناسهٔ منطقهٔ زمانی','text',100,true]] },
    { key:'units', title:'واحدهای سازمانی', record:'OrgUnitRecord', contract:'UnitInput', columns:['نام واحد','شرکت','واحد بالادست'], rows:[['منابع انسانی','شرکت نمونه','ستاد'],['عملیات','شرکت نمونه','ستاد']], fields:[['companyId','شرکت','select',0,true],['parentId','واحد بالادست (اختیاری)','select',0,false],['name','نام واحد','text',200,true]] }
  ],
  employees: [
    { key:'employees', title:'کارکنان', record:'EmployeeRecord', contract:'EmployeeInput', columns:['شمارهٔ پرسنلی','نام نمایشی','شرکت'], rows:[['۱۰۰۱','مریم احمدی','شرکت نمونه'],['۱۰۰۲','علی رضایی','شرکت نمونه']], fields:[['companyId','شرکت','select',0,true],['personnelNumber','شمارهٔ پرسنلی','text',80,true],['displayName','نام نمایشی','text',200,true]] }
  ],
  'employee-history': [
    { key:'assignments', title:'انتساب واحد', record:'EmployeeAssignmentRecord', contract:'AssignmentInput', columns:['کارمند','واحد','شروع اعتبار','پایان انحصاری'], rows:[['مریم احمدی','منابع انسانی','۱۴۰۵/۰۱/۰۱','—']], fields:[['employeeId','کارمند','select',0,true],['unitId','واحد سازمانی','select',0,true],['validFrom','شروع اعتبار','date',0,true],['validUntilExclusive','پایان انحصاری (اختیاری)','date',0,false]] },
    { key:'site-assignments', title:'محل کار', record:'EmployeeSiteAssignmentRecord', contract:'EmployeeSiteInput', columns:['کارمند','سایت','شروع اعتبار','پایان انحصاری'], rows:[['مریم احمدی','سایت مرکزی','۱۴۰۵/۰۱/۰۱','—']], fields:[['employeeId','کارمند','select',0,true],['siteId','سایت','select',0,true],['validFrom','شروع اعتبار','date',0,true],['validUntilExclusive','پایان انحصاری (اختیاری)','date',0,false]] },
    { key:'employment-profiles', title:'پروندهٔ استخدام', record:'EmploymentProfileRecord', contract:'EmploymentInput', columns:['کارمند','کد وضعیت','عنوان','شروع اعتبار','پایان انحصاری'], rows:[['مریم احمدی','ACTIVE','کارشناس','۱۴۰۵/۰۱/۰۱','—']], fields:[['employeeId','کارمند','select',0,true],['statusCode','کد وضعیت','text',80,true],['title','عنوان (اختیاری)','text',200,false],['validFrom','شروع اعتبار','date',0,true],['validUntilExclusive','پایان انحصاری (اختیاری)','date',0,false]] }
  ],
  calendar: [
    { key:'calendars', title:'تقویم‌ها', record:'CalendarRecord', contract:'CalendarInput', columns:['نام تقویم','شرکت'], rows:[['تقویم نمونه','شرکت نمونه']], fields:[['companyId','شرکت','select',0,true],['name','نام تقویم','text',200,true]] },
    { key:'calendar-days', title:'روزهای تقویم', record:'CalendarDayRecord', contract:'CalendarDayInput', columns:['تقویم','تاریخ','نوع روز','یادداشت'], rows:[['تقویم نمونه','۱۴۰۵/۰۲/۱۴','کاری','—'],['تقویم نمونه','۱۴۰۵/۰۲/۱۵','تعطیل','نمونه']], fields:[['calendarId','تقویم','select',0,true],['date','تاریخ','date',0,true],['kind','نوع روز','kind',0,true],['note','یادداشت (اختیاری)','text',500,false]] }
  ],
  shifts: [
    { key:'shifts', title:'شیفت‌های ثابت', record:'ShiftDefinitionRecord', contract:'ShiftInput', columns:['نام شیفت','شرکت','سایت','شروع','پایان','روز پایان'], rows:[['روزکار','شرکت نمونه','سایت مرکزی','۰۸:۰۰','۱۷:۰۰','همان روز'],['شب‌کار','شرکت نمونه','سایت مرکزی','۲۲:۰۰','۰۶:۰۰','روز بعد']], fields:[['companyId','شرکت','select',0,true],['siteId','سایت','select',0,true],['name','نام شیفت','text',200,true],['startsAt','ساعت شروع','time',0,true],['endsAt','ساعت پایان','time',0,true],['endDayOffset','روز پایان','dayOffset',0,true]] }
  ],
  schedules: [
    { key:'assignments', title:'انتساب برنامه', record:'WorkScheduleAssignmentRecord', contract:'ScheduleInput', columns:['کارمند','تقویم','شیفت','شروع اعتبار','پایان انحصاری'], rows:[['مریم احمدی','تقویم نمونه','روزکار','۱۴۰۵/۰۱/۰۱','—']], fields:[['employeeId','کارمند','select',0,true],['calendarId','تقویم','select',0,true],['shiftId','شیفت','select',0,true],['validFrom','شروع اعتبار','date',0,true],['validUntilExclusive','پایان انحصاری (اختیاری)','date',0,false]] }
  ],
  sources: [
    { key:'sources', title:'منابع تردد', record:'EventSourceRecord', contract:'AttendanceSourceInput', columns:['نام منبع','سایت'], rows:[['منبع نمونه','سایت مرکزی']], fields:[['siteId','سایت (اختیاری)','select',0,false],['name','نام منبع','text',200,true]] }
  ],
  mapping: [
    { key:'mappings', title:'نگاشت منبع', record:'SourceEmployeeMappingRecord', contract:'SourceMappingInput', columns:['منبع','شناسهٔ فرد در منبع','کارمند','شروع UTC','پایان انحصاری UTC'], rows:[['منبع نمونه','۱۰۰۱','مریم احمدی','2027-01-01T00:00:00Z','—']], fields:[['sourceId','منبع','select',0,true],['sourcePersonId','شناسهٔ دقیق فرد در منبع','text',100,true],['employeeId','کارمند','select',0,true],['validFrom','شروع با offset (مثال: 2027-01-01T00:00:00Z)','text',0,true],['validUntilExclusive','پایان انحصاری با offset (اختیاری)','text',0,false]] }
  ],
  rules: [
    { key:'rules', title:'قواعد آزمایشی حضور', record:'ObservedRuleVersionRecord', contract:'CalculationRuleInput', columns:['نام قاعده','شرکت','دقیقه قبل','دقیقه بعد','الگوریتم'], rows:[['جفت‌سازی آزمایشی','شرکت نمونه','۱۰','۰','paired-v1']], fields:[['companyId','شرکت','select',0,true],['name','نام قاعده','text',200,true],['beforeMinutes','دقیقه قبل شیفت (۰ تا ۷۲۰)','minutes',0,true],['afterMinutes','دقیقه بعد شیفت (۰ تا ۷۲۰)','minutes',0,true]] }
  ],
  calculations: [
    { key:'runs', title:'اجرای آزمایشی', record:'DailyCalculationRunRecord', contract:'CalculationRunInput', columns:['کارمند','روز کاری','منبع','حضور مشاهده‌شده','مغایرت'], rows:[['مریم احمدی','۱۴۰۵/۰۲/۱۴','منبع نمونه','۰۹:۱۵','نمونه']], fields:[['employeeId','کارمند','select',0,true],['workDate','روز کاری','date',0,true],['sourceId','منبع','select',0,true],['ruleVersionId','نسخهٔ قاعده','select',0,true]] }
  ],
  periods: [
    { key:'drafts', title:'پیش‌نویس تشخیصی', record:'PeriodDraftRecord', contract:'PeriodDraftInput', columns:['کارمند','شروع','پایان انحصاری','وضعیت'], rows:[['مریم احمدی','۱۴۰۵/۰۲/۰۱','۱۴۰۵/۰۳/۰۱','پیش‌نویس نمونه']], fields:[['employeeId','کارمند','select',0,true],['start','شروع','date',0,true],['endExclusive','پایان انحصاری','date',0,true]] }
  ]
};
