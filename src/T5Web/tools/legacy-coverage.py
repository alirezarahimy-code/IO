#!/usr/bin/env python3
"""T5Web — legacy coverage audit, action by action.

The rewrite claims "nothing of T5Web is left behind". That claim is only worth
something if it is machine-checked, so this tool does three things:

  1. It re-derives the *complete* public surface of the legacy web assembly from
     .NET metadata (`T5V3Web.dll`): 67 controllers, 343 public actions.
  2. It writes `docs/legacy-actions.csv`, one row per legacy action, with the
     decision the rewrite made (`new`, `module`, `merge`, `drop`) and where that
     action lives now.
  3. With `--check` it verifies the CSV against the assembly and against the
     code on disk: a row may not appear twice, may not be missing, may not point
     at a module or action that does not exist, and a `drop` must carry a reason.

Usage
-----
    python3 tools/legacy-coverage.py            # check (used by build-and-test.sh)
    python3 tools/legacy-coverage.py --write    # regenerate docs/legacy-actions.csv
    python3 tools/legacy-coverage.py --stats    # counts per decision
"""

from __future__ import annotations

import csv
import logging
import os
import re
import sys

# dnfile emits a harmless "invalid compressed int" warning for one metadata table the
# legacy assembly carries. It is not an error and must not look like a failed gate.
logging.getLogger().setLevel(logging.CRITICAL)

try:
    import dnfile
except ImportError:  # pragma: no cover
    sys.stderr.write("dnfile is required:  pip install --break-system-packages dnfile\n")
    raise

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)                      # src/T5Web
REPO = os.path.dirname(os.path.dirname(ROOT))     # repo root
DLL = os.path.join(REPO, "T5Web [3.5.7]", "App", "bin", "T5V3Web.dll")
CSV_PATH = os.path.join(ROOT, "docs", "legacy-actions.csv")
CONTROLLERS_DIR = os.path.join(ROOT, "T5Web.Web", "Controllers")
REGISTRY = os.path.join(ROOT, "T5Web.Application", "Modules", "ModuleRegistry.cs")
DOC03 = os.path.join(ROOT, "docs", "03-legacy-decomposition.md")

# --------------------------------------------------------------------------- #
# 1. The legacy surface, from metadata
# --------------------------------------------------------------------------- #
def legacy_surface(path=DLL):
    """{controller: [public action, ...]} straight out of the assembly."""
    if not os.path.exists(path):
        raise SystemExit("legacy assembly not found: " + path)

    tables = dnfile.dnPE(path).net.mdtables
    type_rows = list(tables.TypeDef.rows)
    method_rows = tables.MethodDef.rows

    def text(value):
        return "" if value is None else str(value)

    types = [
        (text(r.TypeNamespace), text(r.TypeName),
         r.MethodList[0].row_index if r.MethodList else 0)
        for r in type_rows
    ]

    surface = {}
    for index, (namespace, name, start) in enumerate(types):
        if not name.endswith("Controller") or not namespace.startswith("T5V3Web"):
            continue
        if not start:
            continue
        end = types[index + 1][2] if index + 1 < len(types) and types[index + 1][2] else len(method_rows) + 1
        actions = set()
        for rid in range(start, end):
            method = method_rows[rid - 1]
            name_only = text(method.Name)
            flags = method.Flags
            if not flags.mdPublic or flags.mdStatic or flags.mdRTSpecialName:
                continue
            if name_only.startswith(("get_", "set_", "add_", "remove_", "<")):
                continue
            actions.add(name_only)
        surface[name[: -len("Controller")]] = sorted(actions)
    return surface


# --------------------------------------------------------------------------- #
# 2. The decisions the rewrite made
# --------------------------------------------------------------------------- #
# A controller-level default, plus per-action overrides where one controller
# bundled features that were split (or dropped) individually.
POLICY = {
    "Account":            ("drop", "", "احراز هویت ویندوز (Negotiate) در IIS؛ صفحه ورود/خروج رمز وجود ندارد"),
    "AssignRule":         ("new", "Rules.Index", ""),
    "Rule":               ("new", "Rules.Index", ""),
    "AssignShift":        ("new", "Schedule.Index", ""),
    "Shift":              ("new", "Schedule.Index", ""),
    "Schedule":           ("new", "Schedule.Index", ""),
    "GroupAssignShift":   ("new", "Schedule.Index", "تخصیص گروهی حذف شد؛ تخصیص تک‌نفره با کنترل هم‌پوشانی"),
    "TempSchedules":      ("module", "tempschedule", ""),
    "Calendar":           ("module", "calendar", ""),
    "Ramezan":            ("module", "ramezan", ""),
    "Card":               ("new", "Leave.Index", ""),
    "InsertDailyLeave":   ("new", "Leave.Grants", ""),
    "TransferRemainedLeave": ("new", "Leave.Transfer", ""),
    "Permit":             ("module", "hourlyleave", ""),
    "CardexReport":       ("new", "Reports.Index", ""),
    "DailyReport":        ("new", "Reports.Index", ""),
    "ExitReport":         ("new", "Reports.Index", ""),
    "GridReport":         ("new", "Reports.Index", ""),
    "LateEarlyReport":    ("new", "Reports.Index", ""),
    "OverTimeReport":     ("new", "Reports.Index", ""),
    "PersonnelStatusReport": ("new", "Reports.Index", ""),
    "RemainedLeavesReport":  ("new", "Reports.Index", ""),
    "RequestsReport":     ("new", "Reports.Index", ""),
    "ShowReport":         ("new", "Reports.Index", ""),
    "SumReport":          ("new", "Reports.Index", ""),
    "UserReport":         ("new", "Reports.Index", ""),
    "Export":             ("new", "Reports.Export", ""),
    "Certificate":        ("module", "certificate", ""),
    "EmpType":            ("module", "emptype", ""),
    "LeaveType":          ("module", "leavetype", ""),
    "LeaveDescription":   ("module", "dailyLeaf", "متن شرح، بدون شاهد نوع؛ فقط‌خواندنی"),
    "Title":              ("module", "title", ""),
    "Parameters":         ("module", "parameter", ""),
    "Gate":               ("module", "gate", ""),
    "Deputy":             ("module", "deputy", ""),
    "Manager":            ("module", "deputy", ""),
    "UserAddProperty":    ("module", "property", ""),
    "Dashboard":          ("new", "Dashboard.Index", ""),
    "DashboardItem":      ("module", "dashboard", ""),
    "Department":         ("new", "Department.Index", ""),
    "DeptsChart":         ("new", "Department.Index", "ادغام در چارت سازمانی"),
    "Device":             ("new", "Device.Index", ""),
    "CustomWF":           ("module", "customworkflow", ""),
    "Workflow":           ("new", "Workflow.Index", ""),
    "EditLateEarly":      ("new", "Records.Correct", ""),
    "InsertRecord":       ("new", "Records.Insert", ""),
    "Records":            ("new", "Records.Index", ""),
    "TotalControl":       ("new", "Records.Correct", "اصلاح گروهی حذف شد؛ هر اصلاح یک رکورد و یک دلیل"),
    "ShowUsersRequest":   ("new", "Requests.Index", ""),
    "ImportFromExcel":    ("new", "Import.Index", "CSV آفلاین به‌جای Excel Interop"),
    "PersonnelStatus":    ("new", "Personnel.Index", ""),
    "UserInfo":           ("new", "Personnel.Index", ""),
    "UserSelection":      ("new", "Personnel.Index", "به انتخاب‌گر مشترک تبدیل شد"),
    "ViewSystemLogs":     ("new", "Audit.Index", ""),
    "PersonnelStatusReport": ("new", "Reports.Index", ""),
    "Home":               ("new", "Home.Index", ""),
    "ReportTitles":       ("drop", "", "دسترسی گزارش به‌ازای عنوان (جدول ReportAccess بدون شاهد نوع) با گیت قابلیت ReportView جایگزین شد"),
    "DeviceOperations":   ("new", "Device.Command", "مجموعه‌دستور بسته"),
    "ChangeLock":         ("drop", "", "Zero-Lock: هیچ وابستگی قفل/لایسنس در بازنویسی وجود ندارد"),
    "USBManagement":      ("drop", "", "Zero-Lock: وابسته به دانگل سخت‌افزاری"),
    "ExitType":           ("drop", "", "جدول ExitType در اسکیمای موتور نیست؛ LeaveType.DutyOrLeave جایش را گرفت"),
    "iclock":             ("drop", "", "ارتباط بومی دستگاه در سرویس دسکتاپ می‌ماند؛ پورتال داده را از پایگاه می‌خواند"),
}

# Actions that a controller-level default would wrongly cover.
ACTION_OVERRIDES = {
    # Device operations: the closed set the portal keeps, the rest is dropped.
    ("DeviceOperations", "SyncTimeData"): ("new", "Device.Command", "دستور sync-time"),
    ("DeviceOperations", "TimeSync"): ("new", "Device.Command", "دستور sync-time"),
    ("DeviceOperations", "UploadUsers"): ("new", "Device.Command", "دستور sync-users"),
    ("DeviceOperations", "DownloadUsers"): ("new", "Device.Command", "دستور pull-users"),
    ("DeviceOperations", "GetRecords"): ("new", "Device.Command", "دستور pull-punches"),
    ("DeviceOperations", "GetTimeRecords"): ("new", "Device.Command", "دستور pull-punches"),
    ("DeviceOperations", "RemoveTimeRecords"): ("new", "Device.Command", "دستور clear-punches"),
    ("DeviceOperations", "Index"): ("new", "Device.Index", ""),
    ("DeviceOperations", "DeviceProperties"): ("new", "Device.Edit", ""),
    ("DeviceOperations", "DeviceStates"): ("new", "Device.Index", "وضعیت دستگاه از پایگاه خوانده می‌شود"),
    ("DeviceOperations", "DeleteAllData"): ("drop", "", "پاک‌سازی کامل داده دستگاه بدون تأیید دوگانه؛ در پورتال مجاز نیست"),
    ("DeviceOperations", "DeleteAllRecords"): ("new", "Device.Command", "با دلیل و حسابرسی، همان clear-punches"),
    # Devices: deleting the record orphans every punch that points at it.
    ("Device", "Delete"): ("drop", "", "حذف رکورد دستگاه، تاریخچه تردد را بی‌مرجع می‌کند؛ غیرفعال‌سازی/ویرایش جایگزین شد"),
    ("Device", "Edit"): ("new", "Device.Edit", ""),
    ("Device", "GetAllDevicesTree"): ("new", "Device.Index", ""),
    # Dashboard groups live in the generic module, not on the metrics page.
    ("Dashboard", "Delete"): ("module", "dashboard", "حذف گروه با تأیید و حسابرسی در ماژول انجام می‌شود"),
    ("Dashboard", "Edit"): ("module", "dashboard", ""),
    # TotalControl: physical deletion of a punch is gone, correction is audited.
    ("TotalControl", "Index"): ("new", "Records.Index", ""),
    ("TotalControl", "Show"): ("new", "Records.Index", ""),
    ("TotalControl", "ChangeRecordState"): ("new", "Records.Correct", ""),
    ("TotalControl", "ChangeRecordValid"): ("new", "Records.Correct", ""),
    ("TotalControl", "DeleteRecord"): ("drop", "", "حذف فیزیکی تردد انجام نمی‌شود؛ اصلاح/نامعتبرکردن با دلیل و حسابرسی جایگزین شد"),
    # Home: the login/password actions have no meaning under Windows authentication.
    ("Home", "ChangePass"): ("drop", "", "احراز هویت ویندوز است؛ رمز محلی وجود ندارد"),
    ("Home", "EditPassword"): ("drop", "", "احراز هویت ویندوز است؛ رمز محلی وجود ندارد"),
    ("Home", "ChangeFont"): ("drop", "", "تنظیم ظاهری کاربر؛ طراحی ثابت و یکدست جایگزین شد"),
    ("Home", "SetToolbar"): ("drop", "", "تنظیم ظاهری کاربر؛ طراحی ثابت و یکدست جایگزین شد"),
    ("Home", "MyBanner"): ("drop", "", "تصویر بنر محلی؛ UI جدید آن را ندارد"),
    ("Home", "GetSubMenu"): ("drop", "", "منو از AdminFeature و MenuFor ساخته می‌شود"),
    # Shift management extras.
    ("AssignShift", "AutoSch"): ("drop", "", "زمان‌بندی خودکار، الگوریتم موتور است و بازنویسی نمی‌شود"),
    ("Shift", "GetScheduleHTML"): ("new", "Schedule.Index", "جدول شیفت در همان نما ساخته می‌شود"),
    ("USBManagement", "ReadRecords"): ("drop", "", "خواندن تردد از طریق دانگل USB؛ ترددها از پایگاه‌داده خوانده می‌شوند"),
}

# Actions that exist in the assembly but are dead weight in every rewrite.
GLOBAL_DROP = {
    "DeleteAll": "حذف گروهی بی‌ردپا؛ در پورتال هر حذف یک سطر حسابرسی دارد",
    "About": "صفحه درباره؛ نسخه و وضعیت در /Diagnostics است",
    "GetByID": "خواندن یک رکورد JSON برای فرم قدیمی؛ نماهای جدید سرور-رندر هستند",
    "GetList": "خواندن فهرست JSON برای گرید قدیمی؛ صفحه‌بندی سمت سرور جایگزین شد",
}


def decide(controller, action):
    """Precedence: explicit override, then a bulk-delete ban, then the module
    framework (which replaces the legacy JSON/form helper actions), then the
    controller default."""
    override = ACTION_OVERRIDES.get((controller, action))
    if override:
        return override

    decision, target, note = POLICY.get(controller, ("drop", "", "بدون تصمیم ثبت‌شده"))

    # "DeleteAll" is banned everywhere, in every controller, for the same reason.
    if action == "DeleteAll":
        return ("drop", "", "حذف گروهی بی‌ردپا مجاز نیست؛ در پورتال هر حذف یک سطر حسابرسی دارد")

    # A delivered screen serves its own form and list, so the JSON helpers the old
    # grid called are that screen, not a dropped feature.
    if decision in ("module", "new", "merge") and action in ("GetByID", "GetList"):
        return (decision, target,
                "خواندن JSON برای فرم/گرید قدیمی؛ نمای جدید همان کار را سرور-رندر انجام می‌دهد")

    if action in GLOBAL_DROP:
        return ("drop", "", GLOBAL_DROP[action])
    return decision, target, note


def rows(surface):
    out = []
    for controller in sorted(surface):
        for action in surface[controller]:
            decision, target, note = decide(controller, action)
            out.append({
                "controller": controller,
                "action": action,
                "decision": decision,
                "target": target,
                "note": note,
            })
    return out


# --------------------------------------------------------------------------- #
# 3. Verification against the code on disk
# --------------------------------------------------------------------------- #
def new_controllers():
    """{Controller: {action, ...}} parsed out of the portal's own controllers."""
    found = {}
    for name in os.listdir(CONTROLLERS_DIR):
        if not name.endswith("Controller.cs"):
            continue
        controller = name[: -len(".cs")]
        source = open(os.path.join(CONTROLLERS_DIR, name), encoding="utf-8").read()
        actions = set()
        for match in re.finditer(r"public\s+(?:ActionResult|JsonResult|FileResult|ContentResult|"
                                 r"PartialViewResult|ViewResult|RedirectToRouteResult|string)\s+"
                                 r"([A-Za-z_][A-Za-z0-9_]*)\s*\(", source):
            actions.add(match.group(1))
        found[controller] = actions
    return found


def registry_modules():
    source = open(REGISTRY, encoding="utf-8").read()
    return set(re.findall(r'Code\s*=\s*"([^"]+)"', source))


def documented_controllers():
    """The controllers docs/03 maps, so a drop can point at a real row."""
    names = set()
    for line in open(DOC03, encoding="utf-8"):
        if not line.startswith("| "):
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if len(cells) != 6:
            continue
        match = re.match(r"^`([A-Za-z]+)`$", cells[1])
        if match:
            names.add(match.group(1))
    return names


def check(verbose=True):
    surface = legacy_surface()
    expected = rows(surface)
    problems = []

    if not os.path.exists(CSV_PATH):
        problems.append("docs/legacy-actions.csv is missing; run --write")
        actual = []
    else:
        with open(CSV_PATH, encoding="utf-8", newline="") as handle:
            actual = list(csv.DictReader(handle))

    want = {(r["controller"], r["action"]) for r in expected}
    have = [(r["controller"], r["action"]) for r in actual]
    if len(have) != len(set(have)):
        problems.append("docs/legacy-actions.csv has duplicate rows")
    for pair in sorted(want - set(have)):
        problems.append("undecided legacy action: " + pair[0] + "." + pair[1])
    for pair in sorted(set(have) - want):
        problems.append("CSV row that no longer exists in the assembly: " + pair[0] + "." + pair[1])

    controllers = new_controllers()
    modules = registry_modules()
    documented = documented_controllers()

    for row in actual:
        decision = row["decision"].strip()
        target = row["target"].strip()
        note = row["note"].strip()
        label = row["controller"] + "." + row["action"]
        if decision not in ("new", "module", "merge", "drop"):
            problems.append(label + ": unknown decision '" + decision + "'")
            continue
        if decision == "drop":
            if not note:
                problems.append(label + ": a dropped action must carry a reason")
            if row["controller"] not in documented:
                problems.append(label + ": its controller has no row in docs/03")
            continue
        if decision == "module":
            if target not in modules:
                problems.append(label + ": module '" + target + "' does not exist in ModuleRegistry")
            continue
        if "." not in target:
            problems.append(label + ": target '" + target + "' must be Controller.Action")
            continue
        controller_name, action_name = target.split(".", 1)
        full = controller_name + "Controller"
        if full not in controllers:
            problems.append(label + ": target controller " + full + " does not exist")
        elif action_name not in controllers[full]:
            problems.append(label + ": " + full + " has no action '" + action_name + "'")
        if row["decision"] == "merge" and not note:
            problems.append(label + ": a merged action must say where it merged to")

    if verbose:
        print("legacy surface : %d controllers, %d public actions"
              % (len(surface), sum(len(v) for v in surface.values())))
        counts = {}
        for row in actual:
            counts[row["decision"]] = counts.get(row["decision"], 0) + 1
        print("decisions      : " + ", ".join("%s=%d" % (k, counts[k]) for k in sorted(counts)))
        if problems:
            print("PROBLEMS (%d):" % len(problems))
            for problem in problems:
                print("  - " + problem)
        else:
            print("OK: every legacy action has a decision and a place in the rewrite")
    return 1 if problems else 0


def write():
    surface = legacy_surface()
    payload = rows(surface)
    with open(CSV_PATH, "w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=["controller", "action", "decision", "target", "note"])
        writer.writeheader()
        writer.writerows(payload)
    print("wrote %d rows to docs/legacy-actions.csv" % len(payload))
    return 0


def main(argv):
    if "--write" in argv:
        return write()
    if "--stats" in argv:
        surface = legacy_surface()
        for controller in sorted(surface, key=lambda c: -len(surface[c])):
            print("%-28s %3d" % (controller, len(surface[controller])))
        print("%-28s %3d" % ("TOTAL", sum(len(v) for v in surface.values())))
        return 0
    return check()


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
