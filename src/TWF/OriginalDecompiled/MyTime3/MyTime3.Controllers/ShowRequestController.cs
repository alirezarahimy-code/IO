using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using Personnel.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using T5V3.DB;
using T5V3.DB.EntityManagers;
using T5V3.Misc;
using T5V3.Model;

namespace MyTime3.Controllers
{
	public class ShowRequestController : Controller
	{
		public string name
		{
			get;
			set;
		}

		[HttpGet]
		[AuthorizeFilter(Roles = "admin,user")]
		public ActionResult Index(string fromDate = null, string toDate = null)
		{
			T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			string text = "";
			ShowRequestModel showRequestModel = new ShowRequestModel();
			if (fromDate != null || toDate != null)
			{
				DateTime? t2 = DateConverter.ToChristian(fromDate);
				DateTime? t3 = DateConverter.ToChristian(toDate);
				if (t2.HasValue && t3.HasValue && t2 <= t3)
				{
					showRequestModel.fromDate = fromDate;
					showRequestModel.toDate = toDate;
					RequestDA requestDA = t5DBInstance.RequestDA;
					long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(base.HttpContext);
					DateTime value = t2.Value;
					DateTime date = value.Date;
					value = t3.Value;
					List<T5V3.Model.Request>.Enumerator enumerator = requestDA.GetAll(loggedInBadgeNumber, date, value.Date).GetEnumerator();
					try
					{
						while (enumerator.MoveNext())
						{
							T5V3.Model.Request current = enumerator.Current;
							MyTime3.Models.Request request = new MyTime3.Models.Request();
							request.RequestT5 = current;
							List<Personnel.Model.TitleChart> all = personnelSystem.DB.TitlesCharts.GetAll();
							foreach (var item in from acc in t5DBInstance.RequestAcceptDA.GetByID(current.RequestID)
							join tl in all on acc.AccepterTitleID equals tl.TitleID
							select new
							{
								tl.TitleName,
								acc.IsCancel
							})
							{
								if (item.IsCancel)
								{
									request.CanceledTitle = item.TitleName;
								}
								else if (request.AcceptedTitles == "" || request.AcceptedTitles == null)
								{
									request.AcceptedTitles = item.TitleName;
								}
								else
								{
									MyTime3.Models.Request request2 = request;
									request2.AcceptedTitles = request2.AcceptedTitles + "-" + item.TitleName;
								}
							}
							List<KeyValuePair<long, int>> waitForTitles = t5DBInstance.RequestDA.GetRequestAccepters(personnelSystem.DB.UserInfos.GetAllUserInfoTitles(), personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly(), current.RequestID);
							if (waitForTitles.Count > 0)
							{
								IEnumerable<Personnel.Model.TitleChart> source = from t in all
								where t.TitleID == waitForTitles[0].Value
								select t;
								if (source.Count() > 0)
								{
									request.WaitForTitle = source.First().TitleName;
									name = source.First().TitleName;
								}
							}
							if (request.Descriptions.Contains("بازگشت داده شده"))
							{
								MyTime3.Models.Request request3 = request;
								request3.WaitForTitle = request3.WaitForTitle + " بازگشت داده شده توسط " + name + DateConverter.ToHijriDate(DateTime.Now);
							}
							showRequestModel.Requests.Add(request);
						}
					}
					finally
					{
						((IDisposable)enumerator).Dispose();
					}
					if ((bool)SettingsController.LoadValue("RequestForUsers", typeof(bool), true))
					{
						foreach (PersonnelModel item2 in RequestController.AddPersonnelToViewBag(base.HttpContext))
						{
							RequestDA requestDA2 = t5DBInstance.RequestDA;
							long badgeNumber = item2.BadgeNumber;
							value = t2.Value;
							DateTime date2 = value.Date;
							value = t3.Value;
							enumerator = requestDA2.GetAll(badgeNumber, date2, value.Date).GetEnumerator();
							try
							{
								while (enumerator.MoveNext())
								{
									T5V3.Model.Request current4 = enumerator.Current;
									MyTime3.Models.Request request4 = new MyTime3.Models.Request();
									request4.RequestT5 = current4;
									List<Personnel.Model.TitleChart> all2 = personnelSystem.DB.TitlesCharts.GetAll();
									foreach (var item3 in from acc in t5DBInstance.RequestAcceptDA.GetByID(current4.RequestID)
									join tl in all2 on acc.AccepterTitleID equals tl.TitleID
									select new
									{
										tl.TitleName,
										acc.IsCancel
									})
									{
										if (item3.IsCancel)
										{
											request4.CanceledTitle = item3.TitleName;
										}
										else if (request4.AcceptedTitles == "" || request4.AcceptedTitles == null)
										{
											request4.AcceptedTitles = item3.TitleName;
										}
										else
										{
											MyTime3.Models.Request request5 = request4;
											request5.AcceptedTitles = request5.AcceptedTitles + "-" + item3.TitleName;
										}
									}
									List<KeyValuePair<long, int>> waitForTitles2 = t5DBInstance.RequestDA.GetRequestAccepters(personnelSystem.DB.UserInfos.GetAllUserInfoTitles(), personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly(), current4.RequestID);
									if (waitForTitles2.Count > 0)
									{
										IEnumerable<Personnel.Model.TitleChart> source2 = from t in all2
										where t.TitleID == waitForTitles2[0].Value
										select t;
										if (source2.Count() > 0)
										{
											request4.WaitForTitle = source2.First().TitleName;
										}
									}
									if (!(from r in showRequestModel.Requests
									select r.RequestID).ToList().Contains(request4.RequestID))
									{
										showRequestModel.Requests.Add(request4);
									}
								}
							}
							finally
							{
								((IDisposable)enumerator).Dispose();
							}
						}
					}
				}
				else
				{
					text = PersianString.BadDateFormat;
				}
			}
			base.ViewBag.Message = text;
			foreach (MyTime3.Models.Request request7 in showRequestModel.Requests)
			{
				request7.RequestT5.RequesterName = personnelSystem.DB.UserInfos.GetByID(request7.BadgeNumber).ToString();
				if (request7.RequestT5.Descriptions.Contains("بازگشت داده شده"))
				{
					MyTime3.Models.Request request6 = request7;
					request6.WaitForTitle = request6.WaitForTitle + " بازگشت داده شده توسط " + name + DateConverter.ToHijriDate(DateTime.Now);
				}
			}
			return View(showRequestModel);
		}

		[HttpPost]
		public JsonResult GetRequests(string userName, string password, int nMonthAgo)
		{
			PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
			ShowRequestModel showRequestModel = new ShowRequestModel();
			long num = personnelSystem.DB.UserInfos.CheckUserPass(userName, password, true);
			if (num > 0)
			{
				T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
				PersianCalendar persianCalendar = new PersianCalendar();
				DateTime dateTime = persianCalendar.AddMonths(persianCalendar.ToDateTime(persianCalendar.GetYear(DateTime.Now), persianCalendar.GetMonth(DateTime.Now), 1, 0, 0, 0, 0), -nMonthAgo);
				DateTime toDate = persianCalendar.AddMonths(dateTime, 1);
				foreach (T5V3.Model.Request item in t5DBInstance.RequestDA.GetAll(num, dateTime, toDate))
				{
					MyTime3.Models.Request request = new MyTime3.Models.Request();
					request.RequestT5 = item;
					List<Personnel.Model.TitleChart> all = personnelSystem.DB.TitlesCharts.GetAll();
					foreach (var item2 in from acc in t5DBInstance.RequestAcceptDA.GetByID(item.RequestID)
					join tl in all on acc.AccepterTitleID equals tl.TitleID
					select new
					{
						tl.TitleName,
						acc.IsCancel
					})
					{
						if (item2.IsCancel)
						{
							request.CanceledTitle = item2.TitleName;
						}
						else if (request.AcceptedTitles == "" || request.AcceptedTitles == null)
						{
							request.AcceptedTitles = item2.TitleName;
						}
						else
						{
							MyTime3.Models.Request request2 = request;
							request2.AcceptedTitles = request2.AcceptedTitles + "-" + item2.TitleName;
						}
					}
					List<KeyValuePair<long, int>> waitForTitles = t5DBInstance.RequestDA.GetRequestAccepters(personnelSystem.DB.UserInfos.GetAllUserInfoTitles(), personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly(), item.RequestID);
					if (waitForTitles.Count > 0)
					{
						IEnumerable<Personnel.Model.TitleChart> source = from t in all
						where t.TitleID == waitForTitles[0].Value
						select t;
						if (source.Count() > 0)
						{
							request.WaitForTitle = source.First().TitleName;
						}
					}
					showRequestModel.Requests.Add(request);
				}
			}
			return Json(showRequestModel.Requests);
		}
	}
}
