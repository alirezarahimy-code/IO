<%@ Page Language="C#" Inherits="System.Web.Mvc.ViewPage<dynamic>" %>

<%@ Register Assembly="Microsoft.ReportViewer.WebForms, Version=10.0.0.0, Culture=neutral, PublicKeyToken=B03F5F7F11D50A3A"
    Namespace="Microsoft.Reporting.WebForms" TagPrefix="rsweb" %>

<!DOCTYPE html>

<html>
<head id="Head1" runat="server">
    <title></title>
</head>
<body dir="rtl">
    <form id="form1" runat="server">
        <div>
            <script runat="server">

                private void Page_Load(object sender, System.EventArgs e)
                {
                    ReportViewer1.ShowPageNavigationControls = false;
                    ReportViewer1.ShowBackButton = false;
                    ReportViewer1.ShowFindControls = false;
                    ReportViewer1.ShowRefreshButton = false;
                    ReportViewer1.ShowZoomControl = false;

                    int page = 1;
                    string fromDate = "";
                    string toDate = "";
                    string selectedBadges = "";
                    string dutyDaily = "";
                    string leaveDaily = "";
                    string dutyHourly = "";
                    string leaveHourly = "";

                    if (Request.QueryString.GetValues("page") != null)
                        page = int.Parse(Request.QueryString.GetValues("page")[0]);
                    if (Request.QueryString.GetValues("fromDate") != null)
                        fromDate = Request.QueryString.GetValues("fromDate")[0];
                    if (Request.QueryString.GetValues("toDate") != null)
                        toDate = Request.QueryString.GetValues("toDate")[0];
                    if (Request.QueryString.GetValues("selectedBadges") != null)
                        selectedBadges = Request.QueryString.GetValues("selectedBadges")[0];
                    if (Request.QueryString.GetValues("dutyDaily") != null)
                        dutyDaily = Request.QueryString.GetValues("dutyDaily")[0];
                    if (Request.QueryString.GetValues("leaveDaily") != null)
                        leaveDaily = Request.QueryString.GetValues("leaveDaily")[0];
                    if (Request.QueryString.GetValues("dutyHourly") != null)
                        dutyHourly = Request.QueryString.GetValues("dutyHourly")[0];
                    if (Request.QueryString.GetValues("leaveHourly") != null)
                        leaveHourly = Request.QueryString.GetValues("leaveHourly")[0];

                    ReportViewer1.LocalReport.LoadReportDefinition(T5V3Web.Controllers.ExitReportController.GetSchema());
                    ReportViewer1.LocalReport.DataSources.Add(new Microsoft.Reporting.WebForms.ReportDataSource("dsLeaves",
                        T5V3Web.Controllers.ExitReportController.GetData(selectedBadges, fromDate, toDate, dutyDaily, leaveDaily, dutyHourly, leaveHourly)));

                    ReportViewer1.LocalReport.SetParameters(new Microsoft.Reporting.WebForms.ReportParameter("prmFromDate", fromDate));
                    ReportViewer1.LocalReport.SetParameters(new Microsoft.Reporting.WebForms.ReportParameter("prmToDate", toDate));
                    ReportViewer1.LocalReport.SetParameters(new Microsoft.Reporting.WebForms.ReportParameter("prmCompanyName", T5V3Web.Misc.GlobalHelper.LicenseName));

                    System.Globalization.PersianCalendar pc = new System.Globalization.PersianCalendar();
                    string dateStr = pc.GetYear(DateTime.Now) + "" + pc.GetMonth(DateTime.Now).ToString("00") + "" + pc.GetDayOfMonth(DateTime.Now).ToString("00") + "" + DateTime.Now.ToString("HHmmss");
                    ReportViewer1.LocalReport.DisplayName = "LeavesReport" + dateStr;

                    ReportViewer1.CurrentPage = page;
                    ReportViewer1.LocalReport.Refresh();
                }

            </script>
        </div>
        <asp:ScriptManager ID="ScriptManager1" runat="server">
        </asp:ScriptManager>
        <rsweb:ReportViewer ID="ReportViewer1" ShowPrintButton="true" runat="server" ExportContentDisposition="AlwaysInline" Width="100%" Height="100%" SizeToReportContent="true" AsyncRendering="false"></rsweb:ReportViewer>
    </form>
</body>
</html>
