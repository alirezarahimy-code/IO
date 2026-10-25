using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using Personnel.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class LocationReportController : Controller
	{
		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Index()
		{
			LocationReportModel locationReportModel = new LocationReportModel();
			locationReportModel.IsSelf = true;
			AddPersonnelToViewBag();
			return View(locationReportModel);
		}

		[HttpPost]
		public JsonResult GetImage(string userName, string password, int id, int index)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			if (DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(userName, password, true) > 0)
			{
				return Json(new
				{
					Image = Convert.ToBase64String(t5DBInstance.UserPositionDataDA.GetImage(id, index))
				});
			}
			return null;
		}

		[HttpPost]
		public JsonResult GetReport(string userName, string password, string fromDateHijri, string toDateHijri, List<long> selectedBadgeNumbers, bool isSelf)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			bool flag = false;
			string message = "";
			long num = personnelSystem.DB.UserInfos.CheckUserPass(userName, password, true);
			if (num > 0)
			{
				List<long> list = new List<long>();
				if (isSelf)
				{
					list.Add(num);
				}
				else
				{
					foreach (long selectedBadgeNumber in selectedBadgeNumbers)
					{
						list.Add(selectedBadgeNumber);
					}
				}
				DateTime? nullable = DateConverter.ToChristian(fromDateHijri);
				DateTime? nullable2 = DateConverter.ToChristian(toDateHijri);
				if (!nullable.HasValue || !nullable2.HasValue || nullable2.Value < nullable.Value)
				{
					message = PersianString.BadDateFormat;
				}
				if (flag)
				{
					return Json(new
					{
						Message = message
					});
				}
				List<UserPosition> list2 = new List<UserPosition>();
				foreach (Position item in t5DBInstance.PositionDA.GetAll())
				{
					list2.AddRange(t5DBInstance.UserPositionDataDA.GetAll(list, nullable.Value, nullable2.Value.AddDays(1.0), item.PositionID));
				}
				List<Personnel.Model.UserInfo> all = personnelSystem.DB.UserInfos.GetAll(list);
				foreach (UserPosition item2 in list2)
				{
					foreach (UserPositionStep step in item2.Steps)
					{
						if (step.ImageData != null && step.ImageData.Length > 1)
						{
							step.ImageData = new byte[0];
							step.Image = true;
						}
						else
						{
							step.Image = false;
						}
					}
					item2.UserName = (from u in all
					where u.BadgeNumber == item2.BadgeNumber
					select u).First().ToString();
				}
				JsonResult jsonResult = Json(list2);
				jsonResult.MaxJsonLength = 2147483644;
				return jsonResult;
			}
			return null;
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Report(LocationReportModel model)
		{
			AddPersonnelToViewBag();
			bool flag = false;
			string errorMessage = "";
			List<long> list = new List<long>();
			if (model.IsSelf)
			{
				list.Add(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext));
			}
			else
			{
				foreach (string selectedBadgeNumber in model.SelectedBadgeNumbers)
				{
					list.Add(long.Parse(selectedBadgeNumber));
				}
			}
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			DateTime? nullable = DateConverter.ToChristian(model.FromDateHijri);
			DateTime? nullable2 = DateConverter.ToChristian(model.ToDateHijri);
			if (!nullable.HasValue || !nullable2.HasValue || nullable2.Value < nullable.Value)
			{
				flag = true;
				errorMessage = PersianString.BadDateFormat;
			}
			if (flag)
			{
				model.IsSelf = true;
				model.ErrorMessage = errorMessage;
			}
			else
			{
				model.data = t5DBInstance.UserPositionDataDA.GetAll(list, nullable.Value, nullable2.Value.AddDays(1.0), model.ReportID);
				List<Personnel.Model.UserInfo> all = DBInitializer.GetPersonnelSystem().DB.UserInfos.GetAll(list);
				foreach (UserPosition item in model.data)
				{
					item.UserName = (from u in all
					where u.BadgeNumber == item.BadgeNumber
					select u).First().ToString();
				}
			}
			return View("Index", model);
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public FileResult ShowPhoto(int id, int index)
		{
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			byte[] image = t5DBInstance.UserPositionDataDA.GetImage(id, index);
			int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(loggedInBadgeNumber);
			List<long> list = t5DBInstance.PersonnelDeputyDA.IsDeputy(loggedInBadgeNumber);
			List<int> list2 = new List<int>();
			foreach (long item in list)
			{
				int userCurrentTitleID2 = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(item);
				if (userCurrentTitleID2 != -1)
				{
					list2.Add(userCurrentTitleID2);
				}
			}
			if (userCurrentTitleID != -1 || list2.Count > 0)
			{
				return File(image, "image/png");
			}
			return null;
		}

		private void AddPersonnelToViewBag()
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			List<PersonnelModel> list = new List<PersonnelModel>();
			int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(base.HttpContext);
			List<Personnel.Model.TitleChart> source = new List<Personnel.Model.TitleChart>();
			if (loggedInUserInfoTitleID != -1)
			{
				source = personnelSystem.DB.TitlesCharts.GetAll(loggedInUserInfoTitleID);
			}
			foreach (Personnel.Model.UserInfo item in personnelSystem.DB.UserInfos.GetAllByTitle((from t in source
			select t.TitleID).ToList()))
			{
				if (item.DeptID > -1)
				{
					list.Add(new PersonnelModel(item.BadgeNumber, item.FirstName, item.LastName));
				}
			}
			base.ViewBag.Personnel = list;
			List<SelectListItem> list2 = new List<SelectListItem>();
			foreach (Position item2 in t5DBInstance.PositionDA.GetAll())
			{
				SelectListItem selectListItem = new SelectListItem();
				selectListItem.Text = item2.PositionName;
				selectListItem.Value = item2.PositionID.ToString();
				list2.Add(selectListItem);
			}
			base.ViewBag.Positions = list2;
		}
	}
}
