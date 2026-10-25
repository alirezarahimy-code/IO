using Dapper;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace MyTime3.Models
{
	public class TitleChartClass
	{
		public static List<UserInfoModel> Users = new List<UserInfoModel>();

		public static string Cnn = ConfigurationManager.get_ConnectionStrings().get_Item("Personnel").get_ConnectionString();

		public static UserInfoModel user = new UserInfoModel();

		public static List<UserInfoModel> GetUserListInfoById(long id)
		{
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0010: Expected O, but got Unknown
			List<UserInfoModel> result = new List<UserInfoModel>();
			try
			{
				using (IDbConnection cnn = new SqlConnection(Cnn))
				{
					string sql = "SELECT * FROM [UserInfo] WHERE BadgeNumber = @BadgeNumber";
					result = cnn.Query<UserInfoModel>(sql, new
					{
						BadgeNumber = id
					}, null, true, null, null).ToList();
					return result;
				}
			}
			catch
			{
				return result;
			}
		}

		public static List<UserInfoTitlesModel> GetUserInfoTitlesByBg(long bg)
		{
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0010: Expected O, but got Unknown
			List<UserInfoTitlesModel> list = new List<UserInfoTitlesModel>();
			using (IDbConnection cnn = new SqlConnection(Cnn))
			{
				string sql = "SELECT * FROM [UserInfoTitles] WHERE BadgeNumber = @BG";
				return cnn.Query<UserInfoTitlesModel>(sql, new
				{
					BG = bg
				}, null, true, null, null).ToList();
			}
		}

		public static TitleChartModel GetTitleChartById(int id)
		{
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0010: Expected O, but got Unknown
			TitleChartModel titleChartModel = new TitleChartModel();
			using (IDbConnection cnn = new SqlConnection(Cnn))
			{
				string sql = "SELECT * FROM [TitlesChart] WHERE TitleID = @TitleID";
				return cnn.Query<TitleChartModel>(sql, new
				{
					TitleID = id
				}, null, true, null, null).FirstOrDefault();
			}
		}

		public static List<UserInfoModel> GetAllUserInfo()
		{
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0010: Expected O, but got Unknown
			List<UserInfoModel> list = new List<UserInfoModel>();
			using (IDbConnection cnn = new SqlConnection(Cnn))
			{
				string sql = "SELECT * FROM [UserInfo]";
				return cnn.Query<UserInfoModel>(sql, null, null, true, null, null).ToList();
			}
		}
	}
}
