using CaptchaMvc.HtmlHelpers;
using CaptchaMvc.Models;
using MyTime3.Authorization;
using MyTime3.Content;
using MyTime3.Lock;
using MyTime3.Models;
using Personnel;
using System;
using System.Collections.Generic;
using System.Web.Configuration;
using System.Web.Mvc;
using T5V3.DB;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class LoginController : Controller
	{
		[HttpGet]
		public ActionResult Login()
		{
			if (!GlobalHelper.LockIsOK)
			{
				base.ViewBag.LockErrorText = PersianString.LockError;
				return RedirectToAction("CheckLock");
			}
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			if (GlobalHelper.PersonnelCount == 0)
			{
				base.ViewBag.LockErrorText = PersianString.PersonnelCountLimit;
				return RedirectToAction("CheckLock");
			}
			if (personnelSystem.DB.UserInfos.GetCount() > GlobalHelper.PersonnelCount)
			{
				base.ViewBag.LockErrorText = PersianString.PersonnelCountLimit;
				return RedirectToAction("CheckLock");
			}
			bool result = false;
			if (!bool.TryParse(WebConfigurationManager.get_AppSettings()["Captcha"], out result))
			{
				result = false;
			}
			base.ViewBag.Captcha = result;
			return View();
		}

		[HttpGet]
		public ActionResult CheckLock()
		{
			if (DBInitializer.GetPersonnelSystem().DB.UserInfos.GetCount() > GlobalHelper.PersonnelCount)
			{
				base.ViewBag.LockErrorText = PersianString.PersonnelCountLimit;
			}
			if (!GlobalHelper.LockIsOK)
			{
				base.ViewBag.LockErrorText = "";
			}
			return View();
		}

		[HttpGet]
		public ActionResult RetryCheck()
		{
			CheckLockResult checkLockResult = LockCheker.Check();
			GlobalHelper.LockIsOK = (checkLockResult.LockResult == LockResult.OK);
			GlobalHelper.PersonnelCount = checkLockResult.PersonnelCount;
			GlobalHelper.FulLogoSize = checkLockResult.IsLogoFree;
			GlobalHelper.TimeApp = checkLockResult.TimeApp;
			return RedirectToAction("Login");
		}

		[HttpPost]
		public JsonResult TestUserPass(LoginModel model)
		{
			bool result = false;
			if (DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(model.UserName, model.Password, true) > 0)
			{
				result = true;
			}
			return Json(new
			{
				result
			});
		}

		[HttpPost]
		public ActionResult Login(LoginModel log)
		{
			bool result = false;
			if (!bool.TryParse(WebConfigurationManager.get_AppSettings()["Captcha"], out result))
			{
				result = false;
			}
			base.ViewBag.Captcha = result;
			if (this.IsCaptchaValid("Captcha is not valid", Array.Empty<ParameterModel>()) || !result)
			{
				long num = DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(log.UserName, log.Password, true);
				if (num > 0)
				{
					T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
					if (t5DBInstance.UserPermissionDA.IsAnyMainManager(new List<long>
					{
						num
					}))
					{
						LoginManager.Login(num, "admin", base.HttpContext);
					}
					else
					{
						LoginManager.Login(num, "user", base.HttpContext);
					}
					Log log2 = new Log
					{
						BadgeNumber = num,
						ComputerName = base.Request.get_UserHostAddress(),
						Descriptions = "MyTime3\r\n" + base.Request.get_UserAgent(),
						EventTime = DateTime.Now,
						EventType = EventType.Login,
						OperationType = OperationType.None,
						UserName = num.ToString()
					};
					t5DBInstance.SystemLogDA.AddNew(log2);
					if (!ChangePassController.ChangePassCheck(num, out int message) || log.Password.Length < 5)
					{
						return RedirectToAction("ChangePasswordForce", "Login", (object)new
						{
							Message = message,
							Force = true
						});
					}
					return RedirectToAction("Index", "Home");
				}
				base.ViewBag.Message = PersianString.InvalidUserNameOrPassword;
				log.Password = "";
				return View(log);
			}
			base.ViewBag.Message = PersianString.CaptchaError;
			log.Password = "";
			return View(log);
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult ChangePassword()
		{
			return View();
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult ChangePasswordForce()
		{
			return View();
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult ChangePasswordForce(ChangePasswordModel passes)
		{
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			if (loggedInBadgeNumber != -1)
			{
				if (personnelSystem.DB.UserInfos.GetByID(loggedInBadgeNumber).Password == passes.OldPassword)
				{
					if (passes.NewPassword == passes.ConfirmPassword)
					{
						if (personnelSystem.DB.UserInfos.EditPassword(loggedInBadgeNumber, passes.NewPassword))
						{
							base.ViewBag.Message = PersianString.InsertSuccess;
							ChangePassController.PassChanged(loggedInBadgeNumber);
							return RedirectToAction("Index", "Home");
						}
						base.ViewBag.Message = PersianString.ErrorInInsertingData;
					}
					else
					{
						base.ViewBag.Message = PersianString.ConfirmPasswordError;
					}
				}
				else
				{
					base.ViewBag.Message = PersianString.InvalidUserNameOrPassword;
				}
			}
			return View();
		}

		[HttpPost]
		public JsonResult Change(string userName, string password, string newPassword)
		{
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			long num = personnelSystem.DB.UserInfos.CheckUserPass(userName, password, true);
			if (num > 0)
			{
				if (personnelSystem.DB.UserInfos.EditPassword(num, newPassword))
				{
					ChangePassController.PassChanged(num);
					return Json(new
					{
						Message = "1"
					});
				}
				return Json(new
				{
					Message = "0"
				});
			}
			return null;
		}

		[HttpPost]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult ChangePassword(ChangePasswordModel passes)
		{
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
			if (loggedInBadgeNumber != -1)
			{
				if (personnelSystem.DB.UserInfos.GetByID(loggedInBadgeNumber).Password == passes.OldPassword)
				{
					if (passes.NewPassword == passes.ConfirmPassword)
					{
						if (personnelSystem.DB.UserInfos.EditPassword(loggedInBadgeNumber, passes.NewPassword))
						{
							base.ViewBag.Message = PersianString.InsertSuccess;
							ChangePassController.PassChanged(loggedInBadgeNumber);
						}
						else
						{
							base.ViewBag.Message = PersianString.ErrorInInsertingData;
						}
					}
					else
					{
						base.ViewBag.Message = PersianString.ConfirmPasswordError;
					}
				}
				else
				{
					base.ViewBag.Message = PersianString.InvalidUserNameOrPassword;
				}
			}
			return View();
		}

		public ActionResult Logout()
		{
			LoginManager.Logout(base.HttpContext);
			return RedirectToAction("Index", "Home");
		}
	}
}
