using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using MyTime3.Models;
using Personnel;
using T5V3.DB;

namespace MyTime3.Controllers
{
    public class DeputyController : Controller
    {
        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Index()
        {
            long loggedInBadgeNumber = GlobalHelper.GetLoggedInBadgeNumber(HttpContext);

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                return View(BuildTestDeputyModel(loggedInBadgeNumber));
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            Deputy deputy = new Deputy();
            long currentDeputyBadge = t5DBInstance.PersonnelDeputyDA.GetDeputy(loggedInBadgeNumber);
            if (currentDeputyBadge != -1)
            {
                deputy.IsAnyDeputy = true;
                deputy.CurrentDeputy = personnelSystem.DB.UserInfos.GetByID(currentDeputyBadge);
            }
            else
            {
                deputy.IsAnyDeputy = false;
            }

            List<int> validTitles = t5DBInstance.DeputyDA.GetAll(GlobalHelper.GetLoggedInUserInfoTitleID(HttpContext));
            List<long> badgeNumbers = personnelSystem.DB.UserInfos.GetAllUserInfoTitles()
                .Where(d => validTitles.Contains(d.Value))
                .Select(d => d.Key)
                .ToList();
            deputy.ValidList = personnelSystem.DB.UserInfos.GetAll(badgeNumbers);
            return View(deputy);
        }

        [HttpPost]
        public JsonResult GetState(string userName, string password, string remove = "false", string deputy = "-1")
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var adapter = DBInitializer.TestBackendAdapter;
                long? badge = adapter.AuthenticateEmployee(userName, password);
                if (badge.HasValue && badge.Value > 0)
                {
                    if (bool.TryParse(remove, out bool rm) && rm)
                    {
                        long? cur = adapter.GetActiveDeputyForPrincipal(badge.Value);
                        if (cur.HasValue)
                        {
                            adapter.RemovePersonnelDeputy(badge.Value, cur.Value);
                        }
                    }
                    if (long.TryParse(deputy, out long newDep) && newDep > 0)
                    {
                        adapter.AssignPersonnelDeputy(badge.Value, newDep);
                    }
                    return Json(BuildTestDeputyModel(badge.Value));
                }
                return null;
            }

            T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
            PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
            long badgeNumber = personnelSystem.DB.UserInfos.CheckUserPass(userName, password, true);
            if (badgeNumber > 0)
            {
                if (bool.TryParse(remove, out bool shouldRemove) && shouldRemove)
                {
                    t5DBInstance.PersonnelDeputyDA.DeleteDeputy(badgeNumber);
                }
                if (long.TryParse(deputy, out long newDeputyBadge) && newDeputyBadge > 0)
                {
                    t5DBInstance.PersonnelDeputyDA.UpdateDeputyBadgeNumber(badgeNumber, newDeputyBadge);
                }

                Deputy model = new Deputy();
                long currentDeputyBadge = t5DBInstance.PersonnelDeputyDA.GetDeputy(badgeNumber);
                if (currentDeputyBadge != -1)
                {
                    model.IsAnyDeputy = true;
                    model.CurrentDeputy = personnelSystem.DB.UserInfos.GetByID(currentDeputyBadge);
                }
                else
                {
                    model.IsAnyDeputy = false;
                }

                List<int> validTitles = t5DBInstance.DeputyDA.GetAll(personnelSystem.DB.UserInfos.GetUserCurrentTitleID(badgeNumber));
                List<long> badgeNumbers = personnelSystem.DB.UserInfos.GetAllUserInfoTitles()
                    .Where(d => validTitles.Contains(d.Value))
                    .Select(d => d.Key)
                    .ToList();
                model.ValidList = personnelSystem.DB.UserInfos.GetAll(badgeNumbers);
                return Json(model);
            }
            return null;
        }

        [HttpGet]
        [AuthorizeFilter(Roles = "admin,user")]
        public ActionResult Delete(bool returnToHome = false)
        {
            long loggedInBadge = GlobalHelper.GetLoggedInBadgeNumber(HttpContext);
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                long? cur = DBInitializer.TestBackendAdapter.GetActiveDeputyForPrincipal(loggedInBadge);
                if (cur.HasValue)
                {
                    DBInitializer.TestBackendAdapter.RemovePersonnelDeputy(loggedInBadge, cur.Value);
                }
            }
            else
            {
                DBInitializer.GetT5DBInstance().PersonnelDeputyDA.DeleteDeputy(loggedInBadge);
            }

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
                long loggedInBadge = GlobalHelper.GetLoggedInBadgeNumber(HttpContext);
                if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
                {
                    long? cur = DBInitializer.TestBackendAdapter.GetActiveDeputyForPrincipal(loggedInBadge);
                    if (cur.HasValue)
                    {
                        DBInitializer.TestBackendAdapter.RemovePersonnelDeputy(loggedInBadge, cur.Value);
                    }
                    DBInitializer.TestBackendAdapter.AssignPersonnelDeputy(loggedInBadge, selectedBadgeNumber.Value);
                }
                else
                {
                    DBInitializer.GetT5DBInstance().PersonnelDeputyDA.UpdateDeputyBadgeNumber(loggedInBadge, selectedBadgeNumber.Value);
                }
            }
            return RedirectToAction("Index");
        }

        private static Deputy BuildTestDeputyModel(long principalBadge)
        {
            var adapter = DBInitializer.TestBackendAdapter;
            Deputy model = new Deputy
            {
                ValidList = new List<Personnel.Model.UserInfo>()
            };

            long? activeDeputy = adapter.GetActiveDeputyForPrincipal(principalBadge);
            if (activeDeputy.HasValue && activeDeputy.Value > 0)
            {
                model.IsAnyDeputy = true;
                var depDto = adapter.GetEmployeeByBadge(activeDeputy.Value);
                if (depDto != null)
                {
                    model.CurrentDeputy = new Personnel.Model.UserInfo
                    {
                        BadgeNumber = depDto.BadgeNumber,
                        FirstName = depDto.FirstName,
                        LastName = depDto.LastName
                    };
                }
            }
            else
            {
                model.IsAnyDeputy = false;
            }

            int titleId = adapter.GetCurrentTitleIdForEmployee(principalBadge);
            var allowedTitleIds = adapter.GetAllowedDeputyTitleIds(titleId);
            foreach (var emp in adapter.GetEmployeesByTitleIds(allowedTitleIds))
            {
                if (emp.BadgeNumber != principalBadge)
                {
                    model.ValidList.Add(new Personnel.Model.UserInfo
                    {
                        BadgeNumber = emp.BadgeNumber,
                        FirstName = emp.FirstName,
                        LastName = emp.LastName
                    });
                }
            }
            return model;
        }
    }
}
