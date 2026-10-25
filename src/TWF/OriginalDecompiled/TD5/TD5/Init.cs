using Dapper;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace TD5
{
	public class Init
	{
		public static bool Connected
		{
			get;
			private set;
		}

		public static bool Start(string personnel, string t5)
		{
			//IL_013a: Unknown result type (might be due to invalid IL or missing references)
			//IL_013f: Expected O, but got Unknown
			//IL_018d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0192: Expected O, but got Unknown
			if (string.IsNullOrWhiteSpace(personnel))
			{
				return false;
			}
			Repo.PersonnelConnection = "";
			Repo.PersonnelConnection += GetValueByName(personnel, "Data Source");
			Repo.PersonnelConnection += GetValueByName(personnel, "Initial Catalog");
			Repo.PersonnelConnection += GetValueByName(personnel, "password");
			Repo.PersonnelConnection += GetValueByName(personnel, "user id");
			Repo.PersonnelConnection = Repo.PersonnelConnection.Replace("\"", "");
			if (!string.IsNullOrWhiteSpace(personnel))
			{
				Repo.T5Connection = "";
				Repo.T5Connection += GetValueByName(t5, "Data Source");
				Repo.T5Connection += GetValueByName(t5, "Initial Catalog");
				Repo.T5Connection += GetValueByName(t5, "password");
				Repo.T5Connection += GetValueByName(t5, "user id");
				Repo.T5Connection = Repo.T5Connection.Replace("\"", "");
				try
				{
					using (IDbConnection cnn = new SqlConnection(Repo.PersonnelConnection))
					{
						try
						{
							if (!cnn.Query("SELECT * from [sys].[databases] where [name] = @DBNAME", new
							{
								DBNAME = GetInitialCatalog(personnel)
							}, null, true, null, null).Any())
							{
								return false;
							}
						}
						catch
						{
							return false;
						}
					}
					using (IDbConnection cnn2 = new SqlConnection(Repo.PersonnelConnection))
					{
						try
						{
							if (!cnn2.Query("SELECT * from [sys].[databases] where [name] = @DBNAME", new
							{
								DBNAME = GetInitialCatalog(t5)
							}, null, true, null, null).Any())
							{
								return false;
							}
						}
						catch
						{
							return false;
						}
					}
					Connected = true;
					return true;
				}
				catch
				{
					Connected = false;
					return false;
				}
			}
			return false;
		}

		private static string GetValueByName(string buffer, string Name)
		{
			string[] array = buffer.Split('&', '?', '~', ';');
			if (array.Length == 0)
			{
				return null;
			}
			string[] array2 = array;
			foreach (string text in array2)
			{
				if (text.IndexOf(Name + "=") >= 0)
				{
					return Name + "=" + text.Substring(text.IndexOf(Name + "=") + Name.Length + 1) + ";";
				}
			}
			return null;
		}

		private static string GetInitialCatalog(string buffer)
		{
			string[] array = buffer.Split('&', '?', '~', ';');
			if (array.Length == 0)
			{
				return null;
			}
			string[] array2 = array;
			foreach (string text in array2)
			{
				if (text.IndexOf("Initial Catalog=") >= 0)
				{
					return text.Substring(text.IndexOf("Initial Catalog=") + "Initial Catalog".Length + 1) ?? "";
				}
			}
			return null;
		}
	}
}
