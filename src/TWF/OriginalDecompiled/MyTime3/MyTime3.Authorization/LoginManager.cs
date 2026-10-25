using MyTime3.Controllers;
using System;
using System.Web;

namespace MyTime3.Authorization
{
	public static class LoginManager
	{
		public static void Login(long badgeNumber, string role, HttpContextBase httpContext)
		{
			//IL_006e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0073: Expected O, but got Unknown
			//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
			int num = (int)SettingsController.LoadValue("CookiesTime", typeof(int), 20);
			bool num2 = (bool)SettingsController.LoadValue("CookiesSecure", typeof(int), false);
			DateTime now = DateTime.Now;
			HttpCookie val = new HttpCookie("mytimeauth", Encryption.Encrypt(string.Format("{0};{1};{2}", now.ToString("yyyy/MM/dd HH:mm:ss"), badgeNumber.ToString(), role)));
			HttpCookie obj = val;
			now = DateTime.Now;
			obj.set_Expires(now.AddMinutes((double)num));
			val.set_SameSite(2);
			if (num2)
			{
				val.set_Secure(true);
			}
			else
			{
				val.set_Secure(false);
			}
			val.set_HttpOnly(true);
			httpContext.get_Response().AppendCookie(val);
		}

		public static void Logout(HttpContextBase httpContext)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_0010: Unknown result type (might be due to invalid IL or missing references)
			//IL_0018: Unknown result type (might be due to invalid IL or missing references)
			//IL_001d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0027: Unknown result type (might be due to invalid IL or missing references)
			if ((int)httpContext.get_Request().get_Cookies().get_Item("mytimeauth") != 0)
			{
				httpContext.get_Response().get_Cookies().get_Item("mytimeauth")
					.set_Expires(DateTime.Now.AddDays(-1.0));
			}
		}

		public static string GetRole(HttpContextBase httpContext)
		{
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0016: Unknown result type (might be due to invalid IL or missing references)
			//IL_001b: Expected O, but got Unknown
			string result = "";
			try
			{
				HttpCookie val = httpContext.get_Request().get_Cookies().Get("mytimeauth");
				if (val == null)
				{
					return result;
				}
				result = Encryption.Decrypt(val.get_Value()).Split(new char[1]
				{
					';'
				})[2];
				return result;
			}
			catch
			{
				return result;
			}
		}

		public static bool IsExpired(HttpContextBase httpContext)
		{
			//IL_0003: Unknown result type (might be due to invalid IL or missing references)
			//IL_0008: Unknown result type (might be due to invalid IL or missing references)
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Expected O, but got Unknown
			bool result = true;
			try
			{
				HttpCookie val = httpContext.get_Request().get_Cookies().Get("mytimeauth");
				if (val == null)
				{
					return result;
				}
				result = (DateTime.Parse(Encryption.Decrypt(val.get_Value()).Split(new char[1]
				{
					';'
				})[0]) < DateTime.Now.AddHours(-8.0));
				return result;
			}
			catch
			{
				return result;
			}
		}

		public static string GetBadgeNumber(HttpContextBase httpContext)
		{
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0016: Unknown result type (might be due to invalid IL or missing references)
			//IL_001b: Expected O, but got Unknown
			string result = "";
			try
			{
				HttpCookie val = httpContext.get_Request().get_Cookies().Get("mytimeauth");
				if (val == null)
				{
					return result;
				}
				result = Encryption.Decrypt(val.get_Value()).Split(new char[1]
				{
					';'
				})[1];
				return result;
			}
			catch
			{
				return result;
			}
		}
	}
}
