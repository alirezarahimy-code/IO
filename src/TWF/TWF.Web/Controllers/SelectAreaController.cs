using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using MyTime3.Content;
using MyTime3.Models;
using Personnel;
using Personnel.Model;
using T5V3.DB;
using T5V3.Model;
using TWF.Domain.Models;

namespace MyTime3.Controllers
{
    public class SelectAreaController : Controller
    {
        private static readonly List<AreaModel> _testAreas = new List<AreaModel>();
        private static int _nextTestAreaId = 1;

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Index()
        {
            int loggedInUserInfoTitleID = GlobalHelper.GetLoggedInUserInfoTitleID(HttpContext);
            GlobalHelper.GetLoggedInBadgeNumber(HttpContext);
            ViewBag.Personnel = GetPersonnel(loggedInUserInfoTitleID);
            return View();
        }

        private List<PersonnelModel> GetPersonnel(int titleID)
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                var subTitles = adapter.GetSubordinateTitleIds(titleID, TwfWorkflowType.Record);
                return adapter.GetEmployeesByTitleIds(subTitles)
                    .Select(u => new PersonnelModel(u.BadgeNumber, u.FirstName, u.LastName))
                    .ToList();
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            List<PersonnelModel> list = new List<PersonnelModel>();

            List<int> titleIds = t5DBInstance.WorkflowDA.GetAll(titleID)
                .Where(w => w.Operation == WorkflowOperations.InsertRecord)
                .Select(w => w.TitleID)
                .ToList();

            titleIds.AddRange(
                personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly()
                    .Where(k => k.Value == titleID)
                    .Select(k => k.Key));

            foreach (Personnel.Model.UserInfo item in personnelSystem.DB.UserInfos.GetAllByTitle(titleIds))
            {
                list.Add(new PersonnelModel(item.BadgeNumber, item.FirstName, item.LastName));
            }
            return list;
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public JsonResult GetAreas()
        {
            long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(HttpContext);

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                var list = _testAreas.Where(a => a.ManagerID == loggedInBadgeNumber).ToList();
                foreach (AreaModel item in list)
                {
                    var emp = adapter.GetEmployeeByBadge(item.UserID);
                    item.UserName = emp != null ? emp.FullName : item.UserID.ToString();
                }
                return Json(list);
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            List<AreaModel> all = t5DBInstance.AreaDA.GetAll(loggedInBadgeNumber);

            foreach (AreaModel item in all)
            {
                // Fixed legacy bug: guard against null when GetByID(item.UserID) returns null
                Personnel.Model.UserInfo user = personnelSystem.DB.UserInfos.GetByID(item.UserID);
                item.UserName = user != null ? user.ToString() : item.UserID.ToString();
            }
            return Json(all);
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public JsonResult GetArea(int id)
        {
            long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(HttpContext);

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                AreaModel testData = _testAreas.FirstOrDefault(a => a.ManagerID == loggedInBadgeNumber && a.AreaID == id);
                return Json(testData);
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            AreaModel data = t5DBInstance.AreaDA.GetAll(loggedInBadgeNumber)
                .FirstOrDefault(a => a.AreaID == id);
            return Json(data);
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public JsonResult DeleteArea(int id)
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                int removed = _testAreas.RemoveAll(a => a.AreaID == id);
                return Json(new
                {
                    results = removed > 0 ? PersianString.SuccessOperation : PersianString.ErrorOperation
                });
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            GlobalHelper.GetLoggedInBadgeNumber(HttpContext);
            string err;
            return Json(new
            {
                results = t5DBInstance.AreaDA.Delete(id, out err)
                    ? PersianString.SuccessOperation
                    : PersianString.ErrorOperation
            });
        }

        [HttpPost]
        [AuthorizeFilter(Roles = "admin,user")]
        public JsonResult InsertNew(long badgeNumber, List<LatLng> points)
        {
            if (points == null || points.Count < 3)
            {
                return Json(new { results = PersianString.ErrorInInsertingData });
            }

            List<long> badgeList = new List<long>();
            if (badgeNumber == -1)
            {
                badgeList = GetPersonnel(GlobalHelper.GetLoggedInUserInfoTitleID(HttpContext))
                    .Select(p => p.BadgeNumber)
                    .ToList();
            }
            else
            {
                badgeList.Add(badgeNumber);
            }

            long managerBadge = GlobalHelper.GetLoggedInBadgeNumber(HttpContext);

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                foreach (long item in badgeList)
                {
                    AreaModel areaModel = new AreaModel
                    {
                        AreaID = _nextTestAreaId++,
                        ManagerID = managerBadge,
                        UserID = item,
                        ValidType = 0
                    };
                    for (int i = 0; i < points.Count; i++)
                    {
                        areaModel.Points.Add(new AreaPointModel
                        {
                            Index = i,
                            Latitude = points[i].lat,
                            Longitude = points[i].lng
                        });
                    }
                    _testAreas.Add(areaModel);
                }
                return Json(new { results = badgeList.Count > 0 ? PersianString.InsertSuccess : PersianString.ErrorInInsertingData });
            }

            bool flag = false;
            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            foreach (long item in badgeList)
            {
                AreaModel areaModel = new AreaModel
                {
                    ManagerID = managerBadge,
                    UserID = item,
                    ValidType = 0
                };

                for (int i = 0; i < points.Count; i++)
                {
                    AreaPointModel areaPointModel = new AreaPointModel
                    {
                        Index = i,
                        Latitude = points[i].lat,
                        Longitude = points[i].lng
                    };
                    areaModel.Points.Add(areaPointModel);
                }

                if (t5DBInstance.AreaDA.AddNew(areaModel, true, out string _, out int _))
                {
                    flag = true;
                }
            }

            return Json(new
            {
                results = flag ? PersianString.InsertSuccess : PersianString.ErrorInInsertingData
            });
        }

        [HttpPost]
        public JsonResult GetUserAreas(string userName, string password)
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                long? badge = DBInitializer.TestBackendAdapter.AuthenticateEmployee(userName, password);
                if (badge.HasValue && badge.Value > 0)
                {
                    return Json(_testAreas.Where(a => a.UserID == badge.Value).ToList());
                }
                return null;
            }

            long badgeNumber = DBInitializer.GetPersonnelSystem().DB.UserInfos.CheckUserPass(userName, password, true);
            if (badgeNumber > 0)
            {
                List<AreaModel> allForUser = DBInitializer.GetT5DBInstance().AreaDA.GetAllForUser(badgeNumber);
                return Json(allForUser);
            }
            return null;
        }
    }
}
