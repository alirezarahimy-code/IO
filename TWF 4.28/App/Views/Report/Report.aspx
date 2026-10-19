<%@ Page Language="C#" Inherits="System.Web.Mvc.ViewPage<dynamic>" %>

<%@ Register Assembly="Microsoft.ReportViewer.WebForms, Version=10.0.0.0, Culture=neutral, PublicKeyToken=B03F5F7F11D50A3A"
    Namespace="Microsoft.Reporting.WebForms" TagPrefix="rsweb" %>

<!DOCTYPE html>

<html>
<head id="Head1" runat="server">




    <script src="../../Scripts/jquery-1.11.2.js"></script>
    <script>
        (function ($) {
            $.redirect = function (target, values, method) {
                method = (method && method.toUpperCase() == 'GET') ? 'GET' : 'POST';
                if (!values) {
                    var obj = $.parse_url(target);
                    target = obj.url;
                    values = obj.params;
                }
                var form = $('<form>', {
                    attr: {
                        method: method,
                        action: target
                    }
                });

                for (var i in values) {
                    if (Object.prototype.toString.call(values[i]) === '[object Array]') {
                        for (var j in values[i]) {
                            $('<input>', {
                                attr: {
                                    type: 'hidden',
                                    name: i,
                                    value: values[i][j]
                                }
                            }).appendTo(form);
                        }

                    }
                    else {
                        $('<input>', {
                            attr: {
                                type: 'hidden',
                                name: i,
                                value: values[i]
                            }
                        }).appendTo(form);
                    }
                }
                $('body').append(form);
                console.log(form);
                form.submit();
            };
            $.parse_url = function (url) {
                if (url.indexOf('?') == -1)
                    return { url: url, params: {} }
                var parts = url.split('?');
                var url = parts[0];
                var query_string = parts[1];
                var return_obj = {};
                var elems = query_string.split('&');
                var obj = {};
                for (var i in elems) {
                    var elem = elems[i];
                    var pair = elem.split('=');
                    obj[pair[0]] = pair[1];
                }
                return_obj.url = url;
                return_obj.params = obj;
                return return_obj;
            }
        })(jQuery);
    </script>


    <script>
        function filter() {
            $.redirect('/Report', {}, 'GET');
        }

        function pre() {
            $.redirect('/Report/Report' + $('#lblPre').text(), {}, 'Post');
        }

        function nex() {
            $.redirect('/Report/Report' + $('#lblNex').text(), {}, 'Post');
        }

    </script>

    <title></title>
</head>
<body dir="rtl">


    <form id="form1" runat="server">
        <div>
            <script runat="server">

                private void Page_Load(object sender, EventArgs e)
                {
                    string[] valids = { "ReportID", "isSelf", "SelectedBadgeNumbers", "exportType", "FromDateHijri", "ToDateHijri", "Page", "action" };

                    ReportViewer1.ShowPageNavigationControls = false;
                    if ((bool)MyTime3.Controllers.SettingsController.LoadValue("ReportExport", typeof(bool), true))
                    {
                        ReportViewer1.ShowExportControls = true; 
                    }
                    else
                        ReportViewer1.ShowExportControls = false; 
           
                    Page.Title = string.Format("{1} - {0}", MyTime3.Content.PersianString.bode5, MyTime3.Content.PersianString.Report);

                    byte[] b = new byte[Request.InputStream.Length];
                    Request.InputStream.Read(b, 0, b.Length);
                    string par = Request.Params.ToString();

                    string vals = "?";

                    //Session["tempRep"] = Request.Params.ToString();
                    int page = 1;
                    //if (!int.TryParse((string)Session["rptPage"], out page))
                    //page = 1;

                    foreach (string s in par.Split(new char[] { '&' }))
                    {
                        string[] spl = s.Split(new char[] { '=' });
                        if (spl.Length == 2)
                        {
                            if (valids.Contains(spl[0]))
                                vals += spl[0] + "=" + spl[1] + "&";
                            if (spl[0] == "Page")
                                page = Convert.ToInt32(spl[1]);
                        }
                    }
                    vals = vals.Substring(0, vals.Length - 1);
                    ReportViewer1.LocalReport.LoadReportDefinition(MyTime3.Controllers.ReportController.GetSchema(
                        Request.Cookies.Get("mytimeauth").Value, par));
                    ReportViewer1.LocalReport.DataSources.Add(new ReportDataSource("DataSet1",
                        MyTime3.Controllers.ReportController.GetData(Request.Cookies.Get("mytimeauth").Value, par)));

                    string nexPageUrl = vals;// Request.Url.ToString();
                    string prePageUrl = vals;// Request.Url.ToString();
                    if (nexPageUrl.IndexOf("Page=") >= 0)
                        nexPageUrl = nexPageUrl.Replace("Page=" + page, "Page=" + (page + 1));
                    else
                        nexPageUrl += "&Page=2";
                    if (prePageUrl.IndexOf("Page=") >= 0)
                        prePageUrl = prePageUrl.Replace("Page=" + page, "Page=" + Math.Max(1, (page - 1)).ToString());
                    else
                        prePageUrl += "&Page=1";

                    lblPre.Text = prePageUrl;
                    lblNex.Text = nexPageUrl;
                    // HyperLink1.NavigateUrl = prePageUrl;
                    //HyperLink2.NavigateUrl = nexPageUrl;

                    System.Globalization.PersianCalendar pc = new System.Globalization.PersianCalendar();
                    string dateStr = pc.GetYear(DateTime.Now) + "" + pc.GetMonth(DateTime.Now).ToString("00") + "" + pc.GetDayOfMonth(DateTime.Now).ToString("00") + "" + DateTime.Now.ToString("HHmmss");
                    ReportViewer1.LocalReport.DisplayName = "Report" + dateStr;
                    ReportViewer1.CurrentPage = page;
                    ReportViewer1.LocalReport.Refresh();
                }


           
            </script>
            <input id="btnpre" type="button" onclick="pre()" value="صفحه قبل" />
            <asp:Label ID="lblPre" Style="display: none; visibility: collapse" runat="server"></asp:Label>

            <input type="button" onclick="nex()" value="صفحه بعد" />
            <asp:Label ID="lblNex" Style="display: none; visibility: collapse" runat="server"></asp:Label>

            &nbsp;&nbsp;
        <%--    <input type="button" onclick="filter()" value="بازگشت به صفحه اصلی" />--%>

            <%--<asp:HyperLink ID="HyperLink1" runat="server" BackColor="#00FFCC" BorderColor="Black" BorderStyle="Solid" BorderWidth="1px">صفحه قبل</asp:HyperLink>
            <asp:HyperLink ID="HyperLink2" runat="server" BackColor="#00FFCC" BorderColor="Black" BorderStyle="Solid" BorderWidth="1px">صفحه بعد</asp:HyperLink>
            <asp:HyperLink ID="HyperLink3" runat="server" BackColor="#00FFCC" BorderColor="Black" BorderStyle="Solid" BorderWidth="1px" NavigateUrl="/Report">انتخاب فیلتر</asp:HyperLink>--%>
        </div>

        <asp:ScriptManager ID="ScriptManager1" runat="server">
        </asp:ScriptManager>
        <rsweb:ReportViewer ID="ReportViewer1" ShowPrintButton="true" runat="server" ExportContentDisposition="AlwaysInline" Width="100%" Height="100%" SizeToReportContent="true" AsyncRendering="false"></rsweb:ReportViewer>

       


    </form>
</body>
</html>
