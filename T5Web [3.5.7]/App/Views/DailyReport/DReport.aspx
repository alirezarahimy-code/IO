<%@ Page Language="C#" Inherits="System.Web.Mvc.ViewPage<dynamic>" %>

<%@ Import Namespace="System.Drawing.Printing" %>
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

                public int pageNumbers = 0;

                private void Page_Load(object sender, System.EventArgs e)
                {
                    ReportViewer1.ShowPageNavigationControls = true;
                    ReportViewer1.ShowBackButton = false;
                    ReportViewer1.ShowFindControls = false;
                    ReportViewer1.ShowRefreshButton = false;
                    ReportViewer1.ShowZoomControl = false;
                    var t = Request.Url.AbsolutePath;


                    int page = Request.Params["page"].AsInt();
                    string fromDate = Request.Params["fromDate"];
                    string toDate = Request.Params["toDate"];
                    string selectedBadges = Request.Params["selectedBadges"];


                    ReportViewer1.LocalReport.LoadReportDefinition(T5V3Web.Controllers.DailyReportController.GetSchema());
                    ReportViewer1.LocalReport.DataSources.Clear();


                    if (!string.IsNullOrWhiteSpace(selectedBadges))
                    {
                        pageNumbers = selectedBadges.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Length;
                        ReportViewer1.LocalReport.DataSources.Add(new Microsoft.Reporting.WebForms.ReportDataSource("dsMonthly",
                            T5V3Web.Controllers.DailyReportController.GetData(selectedBadges, fromDate, toDate)));
                    }
                    else
                    {
                        prevPage.Attributes.Add("hidden", "hidden");
                        nextPage.Attributes.Add("hidden", "hidden");
                    }

                    ReportViewer1.LocalReport.SetParameters(new Microsoft.Reporting.WebForms.ReportParameter("prmFromDate", fromDate));
                    ReportViewer1.LocalReport.SetParameters(new Microsoft.Reporting.WebForms.ReportParameter("prmToDate", toDate));
                    ReportViewer1.LocalReport.SetParameters(new Microsoft.Reporting.WebForms.ReportParameter("prmCompanyName", T5V3Web.Misc.GlobalHelper.LicenseName));

                    System.Globalization.PersianCalendar pc = new System.Globalization.PersianCalendar();
                    string dateStr = pc.GetYear(DateTime.Now) + "" + pc.GetMonth(DateTime.Now).ToString("00") + "" + pc.GetDayOfMonth(DateTime.Now).ToString("00") + "" + DateTime.Now.ToString("HHmmss");
                    ReportViewer1.LocalReport.DisplayName = "DailyReport" + dateStr;


                    ReportViewer1.CurrentPage = page;
                    ReportViewer1.LocalReport.Refresh();
                }

                private void nextPage_OnClick(object sender, EventArgs e)
                {
                    if (ReportViewer1.CurrentPage < pageNumbers)
                    {
                        ReportViewer1.CurrentPage++;
                        ReportViewer1.LocalReport.Refresh();
                    }
                }

                private void prevPage_Click(object sender, EventArgs e)
                {
                    if (ReportViewer1.CurrentPage > 1)
                    {
                        ReportViewer1.CurrentPage--;
                        ReportViewer1.LocalReport.Refresh();
                    }
                }
            </script>
        </div>
        <asp:ScriptManager ID="ScriptManager1" runat="server">
        </asp:ScriptManager>
        <rsweb:ReportViewer ID="ReportViewer1" ShowPrintButton="true" runat="server" ExportContentDisposition="AlwaysInline" Width="100%" Height="100%" SizeToReportContent="true" AsyncRendering="false"></rsweb:ReportViewer>
        <footer>
            <div class="mt-10 mb-20">
                <asp:Button runat="server" OnClick="prevPage_Click" ID="prevPage" Text="صفحه قبل" OnClientClick=""/>
                <asp:Button runat="server" OnClick="nextPage_OnClick" ID="nextPage" Text="صفحه بعد" />
            </div>
        </footer>
    </form>

</body>
</html>
