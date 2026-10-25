<%@ Page Language="C#" Inherits="System.Web.Mvc.ViewPage<dynamic>" %>
<!DOCTYPE html>
<html dir="rtl" lang="fa">
<head id="Head1" runat="server">
    <meta charset="utf-8" />
    <title>گزارش کارکرد - سامانه آریا</title>
    <link href="/Content/twf-industrial-ui.css" rel="stylesheet" />
</head>
<body dir="rtl" style="padding:16px;">
    <form id="form1" runat="server">
        <div class="twf-card">
            <div class="twf-card-header">
                <h3 class="twf-card-title"><%= MyTime3.Content.PersianString.Report %></h3>
                <button type="button" class="twf-btn" onclick="window.print()">چاپ گزارش</button>
            </div>
            <div class="twf-card-body">
                <%
                    string authCookie = Request.Cookies.Get("mytimeauth") != null ? Request.Cookies.Get("mytimeauth").Value : "";
                    string par = Request.Params != null ? Request.Params.ToString() : "";
                    System.Data.DataTable dt = MyTime3.Controllers.ReportController.GetData(authCookie, par);
                %>
                <div class="twf-table-wrap">
                    <table class="twf-table">
                        <thead>
                            <tr>
                                <% if (dt != null) { foreach (System.Data.DataColumn col in dt.Columns) { %>
                                    <th><%= System.Web.HttpUtility.HtmlEncode(col.ColumnName) %></th>
                                <% } } %>
                            </tr>
                        </thead>
                        <tbody>
                            <% if (dt != null) { foreach (System.Data.DataRow row in dt.Rows) { %>
                                <tr>
                                    <% foreach (object item in row.ItemArray) { %>
                                        <td class="twf-num"><%= System.Web.HttpUtility.HtmlEncode(item != null ? item.ToString() : "") %></td>
                                    <% } %>
                                </tr>
                            <% } } %>
                        </tbody>
                    </table>
                </div>
            </div>
        </div>
    </form>
</body>
</html>
