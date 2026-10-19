/*
 * 
 * persian-datepicker -  1.0.1
 * Ali Hussain Panahi <p.alihussain@gmail.com>
 * 
 * 
 */
const miladi_month_days = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
const shamsi_month_days = [31, 31, 31, 31, 31, 31, 30, 30, 30, 30, 30, 29]
const shamsi_months_da = ["حمل", "ثور", "جوزا", "سرطان","اسد","سنبله","میزان","عقرب","قوس","جدی","دلو","حوت"]
const shamsi_months_pa = ["وری", "غویی", "غبرګولی", "چنګاښ","زمری","وږی","تله","لړم","لیندی","مرغومی","سلواغه","کب"]
const shamsi_months_fa = ["فروردین", "اردیبهشت", "خرداد", "تیر","مرداد","شهریور","مهر","آبان","آذر","دی","بهمن","اسفند"]
const miladi_months = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec']
//create all elements needed for datepicker inside body
let body = document.body
let section = document.createElement('section')//main container of datepicker
section.setAttribute('id','container')
body.appendChild(section)
let sectionContent = `
<header>
    <button class="today" id="todayBtn">امروز</button>
    <h1><span id="shamsiDate"></span></h1>
    <div class="week-days" dir="rtl">
        <p>ش</p>
        <p>ی</p>
        <p>د</p>
        <p>س</p>
        <p>چ</p>
        <p>پ</p>
        <p>ج</p>
    </div>
    <div id="days" dir="rtl"></div>
</header>
<div class="actions">
    <button id="nextMonth" aria-label="Previous"><</button>
    <button id="previousMonth" aria-label="Next">></button>
</div>
<div id="modal" style="display:none">
    <select id="select_year"></select>
    <select id="select_month"></select>
    <button id="modalBtn" class="modalBtn">انتخاب</button>
</div>
`
section.innerHTML = sectionContent
section.style.display='none'
//DOMs
let container = document.getElementById('container')
const modal = document.getElementById('modal')
const shamsiDateSpan = document.getElementById('shamsiDate')
const daysDiv = document.getElementById('days')
const nextBtn = document.getElementById('nextMonth')
const previousBtn = document.getElementById('previousMonth')
const modalOpen = document.getElementById('modalBtn')
const todayBtn = document.getElementById('todayBtn')
let yearDropdown = document.getElementById('select_year')
let monthDropdown = document.getElementById('select_month')
let selectedMonth = 0//selected month by action buttons and dropdowns
let selectedYear = 0//selected year by action buttons and dropdowns
let selectedDay = 0
let currentSYear = 0//the current year shamsi
let currentSMonth = 0//the current month shamsi
let currentSDay = 0//the current day shamsi
let settings = {format:"yyyy/mm/dd",autoClose:true,dateSeparator:"/",labelLanguage:"dari"}
let textBox = ''//input text with class datepicker-farsi
//event listeners
nextBtn.addEventListener('click',loadNextMonth)
previousBtn.addEventListener('click',loadPreviousMonth)
shamsiDateSpan.addEventListener('click',LoadModal)
modalOpen.addEventListener('click',renderModal)
todayBtn.addEventListener('click',renderToday)
//event for today button listener to render the current month
function renderToday(){
    //empty old data
    clearOldData()
    //according to today's date, render the current month
    let unixdate = Date.now()
    let today = new Date(unixdate)
    let todayDate = today.toLocaleDateString()
    let todayArray = todayDate.split('/')
    let miladi_m = todayArray[0]//current miladi month
    let miladi_d = todayArray[1]//current miladi day
    let miladi_y = todayArray[2]//current miladi year
    let shamsiDate = dateToShamsi(miladi_y,miladi_m,miladi_d)//convert the current date to shamsi
    selectedYear = shamsiDate[0]
    selectedMonth = shamsiDate[1]
    selectedDay = shamsiDate[2]
    renderCalendar(shamsiDate[0],shamsiDate[1],shamsiDate[2])
    
}
//main function to render the calendar base on given date
function renderCalendar(sY,sM,sD){
   
    let monthLabel = shamsi_months_da[sM-1]
    if(settings['labelLanguage']){
        if(settings['labelLanguage']=='pashto'){
            monthLabel = shamsi_months_pa[sM-1]
        }
        else if(settings['labelLanguage']=='farsi'){
            monthLabel = shamsi_months_fa[sM-1]
        }
    }
    shamsiDateSpan.textContent=sY+' '+monthLabel//shamsi month header

    let sMiladiArray = dateToMiladi(sY,sM,1)//get the miladi date of start date for pairing and day week
    
    let shamsiLeapMonth = sM ==12 && shamsiIsLeap(sY) ? 30 : shamsi_month_days[sM-1]//as array index starts from 0, then -1 to get the real one
    
    let eMiladiArray = dateToMiladi(sY,sM,shamsiLeapMonth)//miladi date of end day of shamsi month
    //finding the first day of shamsi of the current month
    //0:sun ... 6: sat
    let sWeekDay = new Date(sMiladiArray[0],sMiladiArray[1]-1,sMiladiArray[2]).getDay()
    let eWeekDay = new Date(eMiladiArray[0],eMiladiArray[1]-1,eMiladiArray[2]).getDay()
    if(sWeekDay != 6){//empty span for days which are before start day of the month
        for(let j = 0;j<=sWeekDay;j++){
            let squar = document.createElement('span')
            squar.classList.add('day')
            daysDiv.appendChild(squar)
        }
    }

    for(let i = 1; i <= shamsiLeapMonth; i++){
        let squar = document.createElement('div')
        squar.setAttribute('id','day_'+i)
        squar.setAttribute('onclick','putDate('+i+')')
        squar.classList.add('day')
        if(i==sD ){
                squar.classList.add('current-day')
        }
        
        daysDiv.appendChild(squar)
        let sSpan = document.createElement('span')
        sSpan.classList.add('shamsi_day')
        sSpan.textContent=i
        document.getElementById('day_'+i).appendChild(sSpan)

    }
    //empty spans for days which are after last day of the month
    if(eWeekDay != 6){
        for(let j = eWeekDay;j<5;j++){
            let squar = document.createElement('span')
            squar.classList.add('day')
            daysDiv.appendChild(squar)
        }
    }else{
        let squar = document.createElement('span')
        squar.classList.add('day')
        daysDiv.appendChild(squar)
    }
}
//function to convert the miladi date to shamsi
function dateToShamsi(miladi_y,miladi_m,miladi_d){

    let mY = miladi_y-1600
    let mM = miladi_m-1
    let mD = miladi_d-1
    let miladi_day_no = 365*mY+div(mY+3,4)-div(mY+99,100)+div(mY+399,400);

    for (let i=0; i < mM; ++i){
        miladi_day_no += miladi_month_days[i]
    }

    if (mM>1 && ((mY%4==0 && mY%100!=0) || (mY%400==0))){
        /* leap and after Feb */
        ++miladi_day_no
    }

    miladi_day_no += mD

    let shamsi_day_no = miladi_day_no-79
    let shamsi_np = div(shamsi_day_no, 12053)

    shamsi_day_no %= 12053

    let sY = 979+33*shamsi_np+4*div(shamsi_day_no,1461)

    shamsi_day_no %= 1461;
    if (shamsi_day_no >= 366) {
        sY += div(shamsi_day_no-1, 365);
        shamsi_day_no = (shamsi_day_no-1)%365;
    }

    let day = 0;
    for (let i = 0; i < 11 && shamsi_day_no >= shamsi_month_days[i]; ++i) {
        shamsi_day_no -= shamsi_month_days[i];
        day = i+1 
    }
    let sM = day + 1
    selectedMonth = sM
    selectedYear = sY
    currentSMonth = sM
    currentSYear = sY
    let sD = shamsi_day_no+1
    currentSDay = sD
    return [sY,sM,sD]
}

//function to convert the shamsi date to miladi
function dateToMiladi(y,m,d){
    let shY = y-979
    let shM = m-1
    let shD = d-1
    let sh_day_no = 365*shY + div(shY,33)*8 + div(((shY%33)+3),4)

    for (let i=0; i < shM; ++i){
        sh_day_no += shamsi_month_days[i]
    }
    
    sh_day_no += shD
    let m_day_no = sh_day_no+79

    let miY = 1600+400*div(m_day_no,146097)
    m_day_no =m_day_no%146097
    let leap=1

    if (m_day_no >= 36525) {
        m_day_no =m_day_no-1
        //36524 = 365*100 + 100/4 - 100/100
        miY +=100* div(m_day_no,36524)
        m_day_no=m_day_no % 36524

        if(m_day_no>=365){
            m_day_no = m_day_no+1
        }
        else{
            leap=0
        }
    }
    miY += 4*div(m_day_no,1461)
    m_day_no %=1461
    if(m_day_no>=366)
    {
        leap=0
        m_day_no=m_day_no-1
        miY += div(m_day_no,365)
        m_day_no=m_day_no %365
    }
    let i=0
    let tmp=0
    while (m_day_no>= (miladi_month_days[i]+tmp))
    {
        if(i==1 && leap==1){
            tmp=1
        }else{
            tmp=0
        }
        m_day_no -= miladi_month_days[i]+tmp
        i=i+1
    }
    let miM=i+1
    let miD=m_day_no+1
    return [miY,miM,miD]
    
}

 //funtion for render next month
 function loadNextMonth(){
    //clear old date
    clearOldData()

    if(selectedMonth == 12){//if the current month is 12, then the next month is the first month of next year
        selectedMonth = 1
        selectedYear = selectedYear+1
    }
    else{
        selectedMonth = selectedMonth+1
    }
    
    renderCalendar(selectedYear,selectedMonth,selectedDay)
 }
 //funtion for render the previous month
 function loadPreviousMonth(){
    //empty old date
    clearOldData()

    if(selectedMonth == 1){//if the current is first month
        selectedMonth = 12//the previous is the 12th month of last year
        selectedYear = selectedYear-1
    }
    else{
        selectedMonth = selectedMonth-1
    }
    
    renderCalendar(selectedYear,selectedMonth,selectedDay)
 }
 
//load modal for selecting year and month
function LoadModal(){
    yearDropdown.textContent=''
    monthDropdown.textContent=''
    let monthLabel = shamsi_months_da
    if(settings['labelLanguage']){
        if(settings['labelLanguage']=='pashto'){
            monthLabel = shamsi_months_pa
        }
        else if(settings['labelLanguage']=='farsi'){
            monthLabel = shamsi_months_fa
        }
    }
    for(let i=currentSYear-50;i<=currentSYear+10;i++){//loop between 50 years ago to next 10 year
        let option = document.createElement('option')
        option.setAttribute('value',i)
        if(i==selectedYear){
            option.setAttribute('selected','selected')
        }
        option.textContent=i
        yearDropdown.appendChild(option)
    }
    for (let i = 0;i<12;i++){
        let option = document.createElement('option')
        option.setAttribute('value',i+1)
        if(i+1==selectedMonth){
            option.setAttribute('selected','selected')
        }
        option.textContent=monthLabel[i]
        monthDropdown.appendChild(option)
    }
    modal.style.display= "block";
}
 
// When the user clicks anywhere outside of the modal, close it
window.onclick = function(event) {
    if (event.target == modal) {
        modal.style.display= "none";
    }
    
}
//render datepicker whenever user clicks on an input with class datepicker-farsi
window.addEventListener('mouseup', function(e) {
    if(e.target.classList == 'datepicker-farsi'){
        e.target.addEventListener('click',loadDatepicker(e.target))
    }
});
//render the calendar base on date from modal
function renderModal(){
    //take the value from select boxes
    let modalYear = yearDropdown.value
    let modalMonth = monthDropdown.value
    //clear old date
    clearOldData()
    //convet the value to integer
    selectedYear = parseInt(modalYear)
    selectedMonth = parseInt(modalMonth)

    modal.style.display= "none";
    renderCalendar(modalYear,modalMonth,1)
}

//when user click on input text with class datepicker-farsi, render the datepicker
function loadDatepicker(e){
    if(e.value==''){//if input text is empty
        renderToday() /* render datepicker as default */
    }
    else{//if input field has already date inside
        //empty old date
        clearOldData()
        let seperator = settings["dateSeparator"] ? settings["dateSeparator"] : "/"
        
        //take the date from input
        let textValue = e.value.split(seperator)
        
        let y,m,d
        if(settings["format"]){
            let format = settings["format"].split("/")
            if(format[0]==='yyyy' && format[1]==='mm' && format[2] === 'dd'){
                y = textValue[0]
                m = textValue[1]
                d = textValue[2]
            }else if(format[0]==='yyyy' && format[1]==='dd' && format[2] === 'mm'){
                y = textValue[0]
                m = textValue[2]
                d = textValue[1]
            }else if(format[0]==='dd' && format[1]==='mm' && format[2] === 'yyyy'){
                y = textValue[2]
                m = textValue[1]
                d = textValue[0]
            }
            else if(format[0]==='dd' && format[1]==='yyyy' && format[2] === 'mm'){
                y = textValue[1]
                m = textValue[2]
                d = textValue[0]
            }
            else if(format[0]==='mm' && format[1]==='dd' && format[2] === 'yyyy'){
                y = textValue[2]
                m = textValue[0]
                d = textValue[1]
            }
            else if(format[0]==='mm' && format[1]==='yyyy' && format[2] === 'dd'){
                y = textValue[1]
                m = textValue[0]
                d = textValue[2]
            }
        }else{//default format
            y = textValue[0]
            m = textValue[1]
            d = textValue[2]
        }
        selectedDay = d//to say the renderCalendar which day should be selected
        renderCalendar(y,m,d)
    }
    textBox = e//the input field dom
    let top = e.getBoundingClientRect().top
    let height = e.getBoundingClientRect().height
    let left = e.getBoundingClientRect().left
    container.style.left=left+'px'
    container.style.top=top+height+'px'
    container.style.display='block'
    document.addEventListener('mouseup', function(event) {
        if (!container.contains(event.target)) {
            modal.style.display = 'none';
            container.style.display = 'none';
        }
    });
}
//put the date inside input text
function putDate(day){
    let month = selectedMonth < 10 ? '0'+selectedMonth : selectedMonth
    day = day < 10 ? '0'+day : day
    
    let seperator = settings["dateSeparator"] ? settings["dateSeparator"] : "/"
    
    if(settings["format"]){
        let format = settings["format"].split("/")
        if(format[0]==='yyyy' && format[1]==='mm' && format[2] === 'dd'){
            textBox.value = selectedYear+seperator+month+seperator+day
        }else if(format[0]==='yyyy' && format[1]==='dd' && format[2] === 'mm'){
            textBox.value = selectedYear+seperator+day+seperator+month
        }else if(format[0]==='dd' && format[1]==='mm' && format[2] === 'yyyy'){
            textBox.value = day+seperator+month+seperator+selectedYear
        }
        else if(format[0]==='dd' && format[1]==='yyyy' && format[2] === 'mm'){
            textBox.value = day+seperator+selectedYear+seperator+month
        }
        else if(format[0]==='mm' && format[1]==='dd' && format[2] === 'yyyy'){
            textBox.value = month+seperator+day+seperator+selectedYear
        }
        else if(format[0]==='mm' && format[1]==='yyyy' && format[2] === 'dd'){
            textBox.value = month+seperator+selectedYear+seperator+day
        }
    }
    else{//default format
        textBox.value = selectedYear+seperator+month+seperator+day
    }
    if(settings["autoClose"]===false){
        container.style.display='block'
    }
    else{
        container.style.display='none'
    }
}
//doing MOD %, by arrow function
const div = (a, b) => Math.floor(a / b)

//clear old rendered data
function clearOldData(){
    shamsiDateSpan.innerHTML=''
    daysDiv.innerHTML=''
}
//check if miladi year is leap
const miladiIsLeap = year => ((year%4) == 0 && ((year%100) != 0 || (year%400) == 0))
//check if shamsi date is leap  
const shamsiIsLeap = year =>{
    year = (year - 474) % 128;
    year = ((year >= 30) ? 0 : 29) + year;
    year = year -Math.floor(year/33) - 1;
    return ((year % 4) == 0);
}
