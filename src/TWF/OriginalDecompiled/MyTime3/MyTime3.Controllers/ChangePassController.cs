using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Linq;
using T5V3.DB.EntityClasses;

namespace MyTime3.Controllers
{
	public class ChangePassController
	{
		public class PassCheckProps
		{
			public long Badge
			{
				get;
				set;
			}

			public DateTime LastChangeDate
			{
				get;
				set;
			}
		}

		public static bool ChangePassCheck(long badge, out int message)
		{
			bool num = (bool)SettingsController.LoadValue("ChangePassRestriction", typeof(bool), false);
			bool flag = (bool)SettingsController.LoadValue("ChangePassMonthly", typeof(bool), false);
			int changePassLimit = (int)SettingsController.LoadValue("ChangePassLimit", typeof(int), 3);
			bool flag2 = (bool)SettingsController.LoadValue("ChangePassLimit", typeof(bool), false);
			if (num)
			{
				if (flag2 && !CheckPassForce(badge))
				{
					message = 1;
					return false;
				}
				if (flag && !CheckPassMonthly(badge, changePassLimit))
				{
					message = 2;
					return false;
				}
			}
			message = 0;
			return true;
		}

		private static bool CheckPassMonthly(long badge, int changePassLimit)
		{
			//IL_0033: Unknown result type (might be due to invalid IL or missing references)
			//IL_0038: Expected O, but got Unknown
			//IL_0054: Unknown result type (might be due to invalid IL or missing references)
			//IL_0059: Expected O, but got Unknown
			using (Entities entities = new Entities(ConfigurationManager.get_AppSettings()["T5CnnStr"]))
			{
				return entities.Database.SqlQuery<PassCheckProps>("SELECT * FROM [PassCheckProps] WHERE [Badge] = @B1 and [LastChangeDate] >= @D1", new object[2]
				{
					(object)new SqlParameter("@B1", (object)badge),
					(object)new SqlParameter("@D1", (object)DateTime.Now.AddMonths(-changePassLimit))
				}).Any();
			}
		}

		private static bool CheckPassForce(long badge)
		{
			//IL_0033: Unknown result type (might be due to invalid IL or missing references)
			//IL_0038: Expected O, but got Unknown
			using (Entities entities = new Entities(ConfigurationManager.get_AppSettings()["T5CnnStr"]))
			{
				return entities.Database.SqlQuery<PassCheckProps>("SELECT * FROM [PassCheckProps] WHERE [Badge] = @B1", new object[1]
				{
					(object)new SqlParameter("@B1", (object)badge)
				}).Any();
			}
		}

		public static void PassChanged(long badge)
		{
			//IL_0039: Unknown result type (might be due to invalid IL or missing references)
			//IL_003e: Expected O, but got Unknown
			//IL_006a: Unknown result type (might be due to invalid IL or missing references)
			//IL_006f: Expected O, but got Unknown
			//IL_0081: Unknown result type (might be due to invalid IL or missing references)
			//IL_0086: Expected O, but got Unknown
			using (Entities entities = new Entities(ConfigurationManager.get_AppSettings()["T5CnnStr"]))
			{
				entities.Database.ExecuteSqlCommand(entities.Database.SqlQuery<object>("SELECT * FROM [PassCheckProps] WHERE [Badge] = @B1", new object[1]
				{
					(object)new SqlParameter("@B1", (object)badge)
				}).Any() ? "Update [PassCheckProps] SET [LastChangeDate] = @D1 WHERE [Badge] = @B1" : "INSERT INTO [PassCheckProps] ([Badge],[LastChangeDate]) Values (@B1,@D1)", (object)new SqlParameter("@B1", (object)badge), (object)new SqlParameter("@D1", (object)DateTime.Now));
			}
		}
	}
}
