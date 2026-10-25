using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using Personnel.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using T5V3.DB;
using T5V3.DB.EntityManagers;
using T5V3.Misc;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class PersonnelStatusController : Controller
	{
		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Index(string hjDate, int? rType)
		{
			PerosnnelStatusModel perosnnelStatusModel = new PerosnnelStatusModel();
			DateTime? nullable = DateConverter.ToChristian(hjDate);
			if (nullable.HasValue && rType.HasValue)
			{
				T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
				PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
				int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(base.HttpContext);
				List<Personnel.Model.TitleChart> source = new List<Personnel.Model.TitleChart>();
				if (loggedInUserInfoTitleID != -1)
				{
					source = personnelSystem.DB.TitlesCharts.GetAll(loggedInUserInfoTitleID);
				}
				List<Personnel.Model.UserInfo> allByTitle = personnelSystem.DB.UserInfos.GetAllByTitle((from t in source
				select t.TitleID).ToList());
				foreach (long item in t5DBInstance.PersonnelDeputyDA.IsDeputy(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext)))
				{
					int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(item);
					if (userCurrentTitleID != -1)
					{
						source = personnelSystem.DB.TitlesCharts.GetAll(userCurrentTitleID);
						allByTitle.AddRange(personnelSystem.DB.UserInfos.GetAllByTitle((from t in source
						select t.TitleID).ToList()));
					}
				}
				RecordDA recordDA = t5DBInstance.RecordDA;
				List<long> badgeNumbers = (from u in allByTitle
				select u.BadgeNumber).ToList();
				DateTime dateTime = nullable.Value;
				DateTime date = dateTime.Date;
				dateTime = nullable.Value;
				dateTime = dateTime.Date;
				List<T5V3.Model.Record> all = recordDA.GetAll(badgeNumbers, date, dateTime.AddDays(1.0));
				List<Personnel.Model.Device> all2 = personnelSystem.DB.Devices.GetAll();
				foreach (T5V3.Model.Record item2 in all)
				{
					Personnel.Model.Device device = (from d in all2
					where d.MachineNumber == item2.MachineNumber
					select d).FirstOrDefault();
					if (device != null)
					{
						item2.Name = device.MachineName;
					}
				}
				foreach (Personnel.Model.UserInfo item3 in allByTitle)
				{
					PerosnnelStatusModel.Row row = new PerosnnelStatusModel.Row();
					row.BadgeNumber = item3.BadgeNumber;
					row.Name = item3.ToString();
					row.Records = (from rc in all
					where rc.BadgeNumber == item3.BadgeNumber
					select rc).ToList();
					if (row.Records.Count == 0)
					{
						row.State = PersianString.AbsentWORecord;
					}
					else if (row.Records.Count % 2 == 0)
					{
						row.State = PersianString.ExitEvenRecords;
					}
					else if (row.Records.Count % 2 == 1)
					{
						row.State = PersianString.PresentOddRecord;
					}
					if (rType.GetValueOrDefault() == 1 || (rType.GetValueOrDefault() == 2 && row.Records.Count == 0) || (rType.GetValueOrDefault() == 3 && row.Records.Count % 2 == 1) || (rType.GetValueOrDefault() == 4 && row.Records.Count > 0 && row.Records.Count % 2 == 0))
					{
						perosnnelStatusModel.Rows.Add(row);
					}
				}
				perosnnelStatusModel.ForDate = nullable.Value;
				perosnnelStatusModel.RType = rType.Value;
			}
			return View(perosnnelStatusModel);
		}
	}
}
