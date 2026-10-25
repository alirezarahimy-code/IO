using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using Personnel.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using T5V3.DB;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class ShowUsersRequestController : Controller
	{
		[HttpGet]
		[AuthorizeFilter(Roles = "admin")]
		public ActionResult Index(long badge = -1L)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			List<int> validIDs = t5DBInstance.EmpLeavesDA.GetAll(GlobalHelper.GetLoggedInUserInfo(base.HttpContext).EmpTypeID);
			base.ViewBag.LeaveTypes = (from l in t5DBInstance.LeaveTypeDA.GetAll()
			where validIDs.Contains(l.LeaveTypeID)
			select l).ToList();
			base.ViewBag.LeaveDescriptions = t5DBInstance.LeaveDescriptionDA.GetAll();
			base.ViewBag.Personnel = AddPersonnelToViewBag(base.HttpContext);
			List<PersonnelModel> list = AddPersonnelToViewBag(base.HttpContext);
			if (list.Count > 0)
			{
				list.RemoveAt(0);
				base.ViewBag.Personnel = list;
			}
			return View(badge);
		}

		public static List<PersonnelModel> AddPersonnelToViewBag(HttpContextBase httpContext)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			List<PersonnelModel> list = new List<PersonnelModel>();
			int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(httpContext);
			List<Personnel.Model.TitleChart> source = new List<Personnel.Model.TitleChart>();
			if (loggedInUserInfoTitleID != -1)
			{
				source = personnelSystem.DB.TitlesCharts.GetAll(loggedInUserInfoTitleID);
			}
			List<Personnel.Model.UserInfo>.Enumerator enumerator = personnelSystem.DB.UserInfos.GetAllByTitle((from t in source
			select t.TitleID).ToList()).GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					Personnel.Model.UserInfo current = enumerator.Current;
					list.Add(new PersonnelModel(current.BadgeNumber, current.FirstName, current.LastName));
				}
			}
			finally
			{
				((IDisposable)enumerator).Dispose();
			}
			foreach (long item in t5DBInstance.PersonnelDeputyDA.IsDeputy(GlobalHelper.GetLoggedInBadgeNumber(httpContext)))
			{
				int userCurrentTitleID = personnelSystem.DB.UserInfos.GetUserCurrentTitleID(item);
				if (userCurrentTitleID != -1)
				{
					source = personnelSystem.DB.TitlesCharts.GetAll(userCurrentTitleID);
					enumerator = personnelSystem.DB.UserInfos.GetAllByTitle((from t in source
					select t.TitleID).ToList()).GetEnumerator();
					try
					{
						while (enumerator.MoveNext())
						{
							Personnel.Model.UserInfo current3 = enumerator.Current;
							list.Add(new PersonnelModel(current3.BadgeNumber, current3.FirstName, current3.LastName));
						}
					}
					finally
					{
						((IDisposable)enumerator).Dispose();
					}
				}
			}
			list.Insert(0, new PersonnelModel(GlobalHelper.GetLoggedInBadgeNumber(httpContext), PersianString.Myself, ""));
			return list;
		}

		[HttpPost]
		public ContentResult GetForUser()
		{
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0011: Unknown result type (might be due to invalid IL or missing references)
			ContentResult contentResult = Content("");
			string value = HttpContext.get_Current().get_Request().get_Params()["badg"];
			new ShowUsersRequestController().getStatus(Convert.ToInt64(value));
			contentResult.ContentType = "text/html; charset=UTF-8";
			if (TitleChartClass.user != null)
			{
				contentResult = Content("<div><div class='card-header text-right'><h3>مشخصات کاربر</h3></div><div class='card'><table class='table table-hover table-bordered table-responsive table-striped'><thead><tr><td>نام</td><td>نام خانوادگی</td><td>سمت</td><td>سمت بالا دست</td></tr></thead><tbody><tr><td>" + TitleChartClass.user.FirstName + "</td><td>" + TitleChartClass.user.LastName + "</td><td>" + TitleChartClass.user.TitleChartName + "</td><td>" + TitleChartClass.user.UpperTitleChartName + "</td></tr></tbody></table></div></div>");
			}
			return contentResult;
		}

		public void getStatus(long badg)
		{
			int userCurrentTitleID = DBInitializer.GetPersonnelSystem().DB.UserInfos.GetUserCurrentTitleID(badg);
			TitleChartClass.user = TitleChartClass.GetUserListInfoById(badg).FirstOrDefault();
			TitleChartModel titleChartById = TitleChartClass.GetTitleChartById(userCurrentTitleID);
			if (titleChartById != null)
			{
				if (titleChartById.UpperTitleID != -1)
				{
					TitleChartClass.user.UpperTitleChartName = TitleChartClass.GetTitleChartById(titleChartById.UpperTitleID).TitleName;
				}
				TitleChartClass.user.TitleChartName = titleChartById.TitleName;
			}
		}
	}
}
