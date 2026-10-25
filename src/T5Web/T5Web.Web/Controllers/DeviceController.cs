using System.Collections.Generic;
using System.Web.Mvc;
using T5Web.Domain;
using T5Web.Domain.Models;
using T5Web.Web.Security;

namespace T5Web.Web.Controllers
{
    [AdminGate(AdminFeature.DeviceView, AllowReadOnly = true)]
    public sealed class DeviceController : PortalController
    {
        public ActionResult Index(int? gateId)
        {
            IReadOnlyList<DeviceRow> devices = PortalServices.Repository.GetDevices(gateId);
            ViewBag.Gates = PortalServices.Repository.GetGates();
            ViewBag.GateId = gateId;
            ViewBag.CanEdit = CurrentSession != null && CurrentSession.CanEdit(AdminFeature.DeviceEdit);
            return View(devices);
        }

        [AdminGate(AdminFeature.DeviceEdit)]
        public ActionResult Edit(int id)
        {
            IReadOnlyList<DeviceRow> devices = PortalServices.Repository.GetDevices(null);
            DeviceRow row = Find(devices, id);
            if (row == null)
            {
                return HttpNotFound("دستگاه یافت نشد.");
            }
            ViewBag.Gates = PortalServices.Repository.GetGates();
            return View(row);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AdminGate(AdminFeature.DeviceEdit)]
        public ActionResult Edit(DeviceRow row)
        {
            // UpdateDevice performs the IP/port validation so the same rules apply to
            // the future API surface, not only to this form.
            OperationResult result = PortalServices.Repository.UpdateDevice(row);
            if (!result.Succeeded)
            {
                ViewBag.Gates = PortalServices.Repository.GetGates();
                ViewBag.Error = result.Message;
                return View(row);
            }

            Audit(AdminFeature.DeviceEdit, "ویرایش دستگاه", "دستگاه " + row.MachineNumber,
                row.MachineName + " / " + row.Endpoint);
            TempData["Flash"] = "تنظیمات دستگاه «" + row.MachineName + "» ثبت شد.";
            return RedirectToAction("Index");
        }

        private static DeviceRow Find(IReadOnlyList<DeviceRow> rows, int machineNumber)
        {
            foreach (DeviceRow row in rows)
            {
                if (row.MachineNumber == machineNumber)
                {
                    return row;
                }
            }
            return null;
        }
    }
}
