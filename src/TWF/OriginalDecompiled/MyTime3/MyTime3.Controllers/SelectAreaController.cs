using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using Personnel.Model;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using T5V3.DB;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class SelectAreaController : Controller
	{
		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Index()
		{
			int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(base.HttpContext);
			GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			base.ViewBag.Personnel = GetPersonnel(loggedInUserInfoTitleID);
			return View();
		}

		private List<PersonnelModel> GetPersonnel(int titleID)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			List<PersonnelModel> list = new List<PersonnelModel>();
			List<int> list2 = (from w in t5DBInstance.WorkflowDA.GetAll(titleID)
			where w.Operation == WorkflowOperations.InsertRecord
			select w.TitleID).ToList();
			list2.AddRange((from k in personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly()
			where k.Value == titleID
			select k.Key).ToList());
			foreach (Personnel.Model.UserInfo item in personnelSystem.DB.UserInfos.GetAllByTitle(list2))
			{
				list.Add(new PersonnelModel(item.BadgeNumber, item.FirstName, item.LastName));
			}
			return list;
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public JsonResult GetAreas()
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			List<AreaModel> all = t5DBInstance.AreaDA.GetAll(loggedInBadgeNumber);
			foreach (AreaModel item in all)
			{
				item.UserName = personnelSystem.DB.UserInfos.GetByID(item.UserID).ToString();
			}
			return Json(all);
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public JsonResult GetArea(int id)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			AreaModel data = (from a in t5DBInstance.AreaDA.GetAll(loggedInBadgeNumber)
			where a.AreaID == id
			select a).FirstOrDefault();
			return Json(data);
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public JsonResult DeleteArea(int id)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			string err;
			return Json(new
			{
				results = (t5DBInstance.AreaDA.Delete(id, out err) ? PersianString.SuccessOperation : PersianString.ErrorOperation)
			});
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public JsonResult InsertNew(long badgeNumber, List<LatLng> points)
		{
			bool flag = false;
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			List<long> list = new List<long>();
			if (badgeNumber == -1)
			{
				list = (from p in GetPersonnel(GlobalHelper.GetLoggedInUserInfoTitleID(base.HttpContext))
				select p.BadgeNumber).ToList();
			}
			else
			{
				list.Add(badgeNumber);
			}
			foreach (long item in list)
			{
				AreaModel areaModel = new AreaModel();
				areaModel.ManagerID = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
				areaModel.UserID = item;
				areaModel.ValidType = 0;
				for (int i = 0; i < points.Count; i++)
				{
					AreaPointModel areaPointModel = new AreaPointModel();
					areaPointModel.Index = i;
					areaPointModel.Latitude = points[i].lat;
					areaPointModel.Longitude = points[i].lng;
					areaModel.Points.Add(areaPointModel);
				}
				if (t5DBInstance.AreaDA.AddNew(areaModel, true, out string _, out int _))
				{
					flag = true;
				}
			}
			return Json(new
			{
				results = (flag ? PersianString.InsertSuccess : PersianString.ErrorInInsertingData)
			});
		}

		[HttpPost]
		public JsonResult GetUserAreas(string userName, string password)
		{
			long num = DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(userName, password, true);
			if (num > 0)
			{
				List<AreaModel> allForUser = DBInitializer.GetT5DBInstance().AreaDA.GetAllForUser(num);
				return Json(allForUser);
			}
			return null;
		}
	}
}
