using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using Personnel.Model;
using T5V3.DB;
using T5V3.DB.EntityManagers;
using T5V3.Misc;
using T5V3.Model;
using TWF.Domain.Models;

namespace MyTime3.Controllers
{
    public class PersonnelStatusController : Controller
    {
        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Index(string hjDate, int? rType)
        {
            PerosnnelStatusModel perosnnelStatusModel = new PerosnnelStatusModel();
            DateTime? parsedDate = DailyLeaveController.ParseHijriDate(hjDate);
            if (parsedDate.HasValue && rType.HasValue)
            {
                if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
                {
                    var adapter = DBInitializer.TestBackendAdapter;
                    int titleId = GlobalHelper.GetLoggedInUserInfoTitleID(HttpContext);
                    var subTitleIds = adapter.GetSubordinateTitleIds(titleId).ToList();
                    foreach (long depBadge in adapter.GetPrincipalsWhereEmployeeIsDeputy(GlobalHelper.GetLoggedInBadgeNumber(HttpContext)))
                    {
                        int depTitle = adapter.GetCurrentTitleIdForEmployee(depBadge);
                        if (depTitle > 0)
                        {
                            subTitleIds.AddRange(adapter.GetSubordinateTitleIds(depTitle));
                        }
                    }

                    foreach (var emp in adapter.GetEmployeesByTitleIds(subTitleIds.Distinct()))
                    {
                        int recCount = adapter.GetOperationalRecordsCount(emp.BadgeNumber, TwfWorkflowType.Record, parsedDate.Value.Date);
                        var records = new List<T5V3.Model.Record>();
                        for (int i = 0; i < recCount; i++)
                        {
                            records.Add(new T5V3.Model.Record
                            {
                                BadgeNumber = emp.BadgeNumber,
                                CheckTime = parsedDate.Value.Date.AddHours(8 + i * 4),
                                MachineNumber = 1,
                                Name = "ترمینال مرکزی"
                            });
                        }

                        var row = new PerosnnelStatusModel.Row
                        {
                            BadgeNumber = emp.BadgeNumber,
                            Name = emp.FullName,
                            Records = records,
                            State = recCount == 0
                                ? PersianString.AbsentWORecord
                                : (recCount % 2 == 0 ? PersianString.ExitEvenRecords : PersianString.PresentOddRecord)
                        };

                        int filter = rType.GetValueOrDefault();
                        if (filter == 1 ||
                            (filter == 2 && recCount == 0) ||
                            (filter == 3 && recCount % 2 == 1) ||
                            (filter == 4 && recCount > 0 && recCount % 2 == 0))
                        {
                            perosnnelStatusModel.Rows.Add(row);
                        }
                    }

                    perosnnelStatusModel.ForDate = parsedDate.Value;
                    perosnnelStatusModel.RType = rType.Value;
                    return View(perosnnelStatusModel);
                }

                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
                int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(HttpContext);
                List<Personnel.Model.TitleChart> childTitles = new List<Personnel.Model.TitleChart>();
                if (loggedInUserInfoTitleID != -1)
                {
                    childTitles = personnelSystem.DB.TitlesCharts.GetAll(loggedInUserInfoTitleID);
                }

                List<Personnel.Model.UserInfo> allByTitle = personnelSystem.DB.UserInfos.GetAllByTitle(
                    childTitles.Select(t => t.TitleID).ToList());

                foreach (long item in t5DBInstance.PersonnelDeputyDA.IsDeputy(GlobalHelper.GetLoggedInBadgeNumber(HttpContext)))
                {
                    int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(item);
                    if (userCurrentTitleID != -1)
                    {
                        childTitles = personnelSystem.DB.TitlesCharts.GetAll(userCurrentTitleID);
                        allByTitle.AddRange(personnelSystem.DB.UserInfos.GetAllByTitle(
                            childTitles.Select(t => t.TitleID).ToList()));
                    }
                }

                RecordDA recordDA = t5DBInstance.RecordDA;
                List<long> badgeNumbers = allByTitle.Select(u => u.BadgeNumber).Distinct().ToList();
                DateTime targetDate = parsedDate.Value.Date;
                List<T5V3.Model.Record> allRecords = recordDA.GetAll(badgeNumbers, targetDate, targetDate.AddDays(1.0));
                List<Personnel.Model.Device> allDevices = personnelSystem.DB.Devices.GetAll();

                foreach (T5V3.Model.Record rec in allRecords)
                {
                    Personnel.Model.Device device = allDevices.FirstOrDefault(d => d.MachineNumber == rec.MachineNumber);
                    if (device != null)
                    {
                        rec.Name = device.MachineName;
                    }
                }

                foreach (Personnel.Model.UserInfo user in allByTitle)
                {
                    PerosnnelStatusModel.Row row = new PerosnnelStatusModel.Row
                    {
                        BadgeNumber = user.BadgeNumber,
                        Name = user.ToString(),
                        Records = allRecords.Where(rc => rc.BadgeNumber == user.BadgeNumber).ToList()
                    };

                    if (row.Records.Count == 0)
                    {
                        row.State = PersianString.AbsentWORecord;
                    }
                    else if (row.Records.Count % 2 == 0)
                    {
                        row.State = PersianString.ExitEvenRecords;
                    }
                    else
                    {
                        row.State = PersianString.PresentOddRecord;
                    }

                    int filterType = rType.GetValueOrDefault();
                    if (filterType == 1 ||
                        (filterType == 2 && row.Records.Count == 0) ||
                        (filterType == 3 && row.Records.Count % 2 == 1) ||
                        (filterType == 4 && row.Records.Count > 0 && row.Records.Count % 2 == 0))
                    {
                        perosnnelStatusModel.Rows.Add(row);
                    }
                }

                perosnnelStatusModel.ForDate = parsedDate.Value;
                perosnnelStatusModel.RType = rType.Value;
            }
            return View(perosnnelStatusModel);
        }
    }
}
