using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using T5Web.Application.Services;
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
            ViewBag.CanSync = CurrentSession != null && CurrentSession.CanEdit(AdminFeature.DeviceSync);
            ViewBag.Commands = new DeviceCommandService().Available();
            // The queue is the transport: the portal records the intent, the desktop agent
            // drains it. Showing it here is what makes the button honest.
            ViewBag.Queue = PortalServices.Repository.GetDeviceCommands(20);
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

        /// <summary>
        /// Issues one of the closed-set device commands. A raw command string is not
        /// accepted, and no lock/licence command exists in this build at all.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AdminGate(AdminFeature.DeviceSync)]
        public ActionResult Command(int machineNumber, string command)
        {
            IReadOnlyList<DeviceRow> devices = PortalServices.Repository.GetDevices(null);
            DeviceRow device = Find(devices, machineNumber);

            DeviceCommand issued;
            OperationResult result = new DeviceCommandService().Validate(command, device, out issued);
            if (!result.Succeeded)
            {
                TempData["Flash"] = result.Message;
                return RedirectToAction("Index");
            }

            // The transport to the hardware lives in the desktop service (it holds the
            // device sockets). The portal writes a durable queue row and the audit entry;
            // it never pretends the device already answered (BUG-26).
            var ticket = new DeviceCommandTicket
            {
                MachineNumber = device.MachineNumber,
                Command = issued.Code,
                RequestedBy = CurrentSession == null ? "anonymous" : CurrentSession.DisplayName + " (" + CurrentSession.BadgeNumber + ")",
                RequestedUtc = DateTime.UtcNow,
                Note = issued.NoteFa
            };
            OperationResult queued = PortalServices.Repository.EnqueueDeviceCommand(ticket);
            if (!queued.Succeeded)
            {
                TempData["Flash"] = queued.Message;
                return RedirectToAction("Index");
            }

            Audit(AdminFeature.DeviceSync, "دستور دستگاه", device.MachineName + " (#" + device.MachineNumber + ")",
                "ticket=" + ticket.Id + "; command=" + issued.Code + "; note=" + issued.NoteFa);
            TempData["Flash"] = "دستور «" + issued.TitleFa + "» برای دستگاه " + device.MachineName +
                                " در صف ثبت شد (شماره " + ticket.Id + ")؛ سرویس دستگاه آن را برمی‌دارد.";
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
