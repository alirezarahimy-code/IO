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
            ViewBag.Transport = Transport();
            ViewBag.Commands = new DeviceCommandService().Available();
            // The queue is a record of intent. It is only a *transport* when an agent
            // outside this repository drains it, which is what T5WebDeviceTransport says.
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
            DeviceTransport transport = Transport();
            OperationResult result = new DeviceCommandService().Validate(command, device, transport, out issued);
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

            // Dispatch, when this installation has somewhere to dispatch to. The queue row
            // stays either way: it is the record of what was asked for, and a failed send
            // must not vanish.
            string dispatch = transport.Kind == DeviceTransportKind.Link5
                ? Send(transport.CommandUrl(issued.Code) + "?machineNumber=" + device.MachineNumber)
                : transport.Kind == DeviceTransportKind.Queue
                    ? "در صف؛ عامل خارجی آن را برمی‌دارد"
                    : "بدون ارسال";

            Audit(AdminFeature.DeviceSync, "دستور دستگاه", device.MachineName + " (#" + device.MachineNumber + ")",
                "ticket=" + ticket.Id + "; command=" + issued.Code + "; transport=" + transport.Raw +
                "; dispatch=" + dispatch + "; note=" + issued.NoteFa);

            TempData["Flash"] = transport.Kind == DeviceTransportKind.Link5 && !dispatch.StartsWith("OK", StringComparison.Ordinal)
                ? "دستور «" + issued.TitleFa + "» در صف ثبت شد (شماره " + ticket.Id + ") ولی ارسال به سرویس دستگاه ناموفق بود: " + dispatch
                : "دستور «" + issued.TitleFa + "» برای دستگاه " + device.MachineName +
                  " ثبت شد (شماره " + ticket.Id + ")؛ " + dispatch + ".";
            return RedirectToAction("Index");
        }

        /// <summary>The configured device link, read per request so an operator can fix it live.</summary>
        private static DeviceTransport Transport()
        {
            return DeviceTransport.Parse(
                System.Configuration.ConfigurationManager.AppSettings[DeviceTransport.Key]);
        }

        /// <summary>
        /// Posts one catalogued operation to the device service, exactly as the legacy
        /// console did, and returns a line for the audit and the flash message.
        /// </summary>
        private static string Send(string url)
        {
            try
            {
                var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = 15000;
                request.Proxy = null;
                using (var response = (System.Net.HttpWebResponse)request.GetResponse())
                {
                    return "OK " + (int)response.StatusCode;
                }
            }
            catch (Exception ex)
            {
                return "خطا: " + ex.Message;
            }
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
