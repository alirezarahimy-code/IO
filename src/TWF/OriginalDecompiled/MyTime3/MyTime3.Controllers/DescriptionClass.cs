using System;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Web.Configuration;

namespace MyTime3.Controllers
{
	public class DescriptionClass
	{
		public long BadgeNumber
		{
			get;
			set;
		}

		public DateTime FromTime
		{
			get;
			set;
		}

		public string Descriptions
		{
			get;
			set;
		}

		public static string GetDescriptionInDate(long bg, DateTime fromDate)
		{
			//IL_0037: Unknown result type (might be due to invalid IL or missing references)
			//IL_003c: Expected O, but got Unknown
			//IL_003f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0044: Expected O, but got Unknown
			//IL_0046: Unknown result type (might be due to invalid IL or missing references)
			//IL_0051: Unknown result type (might be due to invalid IL or missing references)
			//IL_0058: Unknown result type (might be due to invalid IL or missing references)
			//IL_0063: Unknown result type (might be due to invalid IL or missing references)
			//IL_006a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0074: Unknown result type (might be due to invalid IL or missing references)
			//IL_007f: Expected O, but got Unknown
			//IL_0085: Unknown result type (might be due to invalid IL or missing references)
			//IL_008f: Unknown result type (might be due to invalid IL or missing references)
			//IL_009a: Expected O, but got Unknown
			string text = WebConfigurationManager.get_AppSettings()["T5CnnStr"].Replace("metadata=res://*/EntityClasses.mdlT5V3.csdl|res://*/EntityClasses.mdlT5V3.ssdl|res://*/EntityClasses.mdlT5V3.msl;provider=System.Data.SqlClient;provider connection string=\"", "");
			string text2 = text.Remove(text.Length - 1);
			string text3 = "";
			string text4 = "SELECT Descriptions FROM [Request] WHERE [BadgeNumber]=@BG and [FromTime]=@FT";
			SqlConnection val = new SqlConnection(text2);
			try
			{
				SqlCommand val2 = new SqlCommand(text4, val);
				val2.get_Parameters().Add("@BG", SqlDbType.BigInt);
				val2.get_Parameters().Add("@FT", SqlDbType.DateTime);
				((DbParameter)val2.get_Parameters().get_Item("@BG")).Value = bg;
				((DbParameter)val2.get_Parameters().get_Item("@FT")).Value = fromDate;
				try
				{
					((DbConnection)val).Open();
					return (string)((DbCommand)val2).ExecuteScalar();
				}
				catch (Exception)
				{
					return "NoData";
				}
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}

		public static string GetDescriptionInOld(long bg, DateTime fromDate)
		{
			//IL_0037: Unknown result type (might be due to invalid IL or missing references)
			//IL_003c: Expected O, but got Unknown
			//IL_003f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0044: Expected O, but got Unknown
			//IL_0046: Unknown result type (might be due to invalid IL or missing references)
			//IL_0051: Unknown result type (might be due to invalid IL or missing references)
			//IL_0058: Unknown result type (might be due to invalid IL or missing references)
			//IL_0063: Unknown result type (might be due to invalid IL or missing references)
			//IL_006a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0074: Unknown result type (might be due to invalid IL or missing references)
			//IL_007f: Expected O, but got Unknown
			//IL_0085: Unknown result type (might be due to invalid IL or missing references)
			//IL_008f: Unknown result type (might be due to invalid IL or missing references)
			//IL_009a: Expected O, but got Unknown
			string text = WebConfigurationManager.get_AppSettings()["T5CnnStr"].Replace("metadata=res://*/EntityClasses.mdlT5V3.csdl|res://*/EntityClasses.mdlT5V3.ssdl|res://*/EntityClasses.mdlT5V3.msl;provider=System.Data.SqlClient;provider connection string=\"", "");
			string text2 = text.Remove(text.Length - 1);
			string text3 = "";
			string text4 = "SELECT Descriptions FROM [Request] WHERE [BadgeNumber]=@BG and [FromTime]=@FT and [IsDaily] = 1";
			SqlConnection val = new SqlConnection(text2);
			try
			{
				SqlCommand val2 = new SqlCommand(text4, val);
				val2.get_Parameters().Add("@BG", SqlDbType.BigInt);
				val2.get_Parameters().Add("@FT", SqlDbType.DateTime);
				((DbParameter)val2.get_Parameters().get_Item("@BG")).Value = bg;
				((DbParameter)val2.get_Parameters().get_Item("@FT")).Value = fromDate;
				try
				{
					((DbConnection)val).Open();
					return (string)((DbCommand)val2).ExecuteScalar();
				}
				catch (Exception)
				{
					return "NoData";
				}
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
	}
}
