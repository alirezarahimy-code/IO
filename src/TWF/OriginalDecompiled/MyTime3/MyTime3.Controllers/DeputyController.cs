using MyTime3.Models;
using Personnel;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using T5V3.DB;

namespace MyTime3.Controllers
{
	public class DeputyController : Controller
	{
		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Index()
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			Deputy deputy = new Deputy();
			long deputy2 = t5DBInstance.PersonnelDeputyDA.GetDeputy(loggedInBadgeNumber);
			if (deputy2 != -1)
			{
				deputy.IsAnyDeputy = true;
				deputy.CurrentDeputy = personnelSystem.DB.UserInfos.GetByID(deputy2);
			}
			else
			{
				deputy.IsAnyDeputy = false;
			}
			List<int> validTitles = t5DBInstance.DeputyDA.GetAll(GlobalHelper.GetLoggedInUserInfoTitleID(base.HttpContext));
			List<long> badgeNumbers = (from d in personnelSystem.DB.UserInfos.GetAllUserInfoTitles()
			where validTitles.Contains(d.Value)
			select d.Key).ToList();
			deputy.ValidList = personnelSystem.DB.UserInfos.GetAll(badgeNumbers);
			return View(deputy);
		}

		[HttpPost]
		public JsonResult GetState(string userName, string password, string remove = "false", string deputy = "-1")
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			long num = personnelSystem.DB.UserInfos.CheckUserPass(userName, password, true);
			if (num > 0)
			{
				if (bool.Parse(remove))
				{
					t5DBInstance.PersonnelDeputyDA.DeleteDeputy(num);
				}
				if (long.Parse(deputy) > 0)
				{
					t5DBInstance.PersonnelDeputyDA.UpdateDeputyBadgeNumber(num, long.Parse(deputy));
				}
				Deputy deputy2 = new Deputy();
				long deputy3 = t5DBInstance.PersonnelDeputyDA.GetDeputy(num);
				if (deputy3 != -1)
				{
					deputy2.IsAnyDeputy = true;
					deputy2.CurrentDeputy = personnelSystem.DB.UserInfos.GetByID(deputy3);
				}
				else
				{
					deputy2.IsAnyDeputy = false;
				}
				List<int> validTitles = t5DBInstance.DeputyDA.GetAll(personnelSystem.DB.UserInfos.GetUserCurrentTitleID(num));
				List<long> badgeNumbers = (from d in personnelSystem.DB.UserInfos.GetAllUserInfoTitles()
				where validTitles.Contains(d.Value)
				select d.Key).ToList();
				deputy2.ValidList = personnelSystem.DB.UserInfos.GetAll(badgeNumbers);
				return Json(deputy2);
			}
			return null;
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Delete(bool returnToHome = false)
		{
			DBInitializer.GetT5DBInstance().PersonnelDeputyDA.DeleteDeputy(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext));
			if (returnToHome)
			{
				return RedirectToAction("Index", "Home");
			}
			return RedirectToAction("Index");
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Add(long? selectedBadgeNumber)
		{
			if (selectedBadgeNumber.HasValue)
			{
				DBInitializer.GetT5DBInstance().PersonnelDeputyDA.UpdateDeputyBadgeNumber(GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext), selectedBadgeNumber.Value);
			}
			return RedirectToAction("Index");
		}
	}
}
