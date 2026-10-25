using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Web.Configuration;
using TINYLib;

namespace MyTime3.Lock
{
	internal class LockCheker
	{
		private static DateTime StringToDateTime(string d)
		{
			DateTime result = new DateTime(2000, 1, 1);
			string[] array = d.Split(new char[1]
			{
				'/'
			}, StringSplitOptions.RemoveEmptyEntries);
			try
			{
				if (array.Length < 3)
				{
					return result;
				}
				result = new PersianCalendar().ToDateTime(int.Parse(array[0]), int.Parse(array[1]), int.Parse(array[2]), 0, 0, 0, 0);
				return result;
			}
			catch
			{
				return result;
			}
		}

		private static string GetToday()
		{
			PersianCalendar persianCalendar = new PersianCalendar();
			return $"{persianCalendar.GetYear(DateTime.Now)}/{persianCalendar.GetMonth(DateTime.Now)}/{persianCalendar.GetDayOfMonth(DateTime.Now)}";
		}

		public static CheckLockResult Check()
		{
			string text = WebConfigurationManager.get_AppSettings()["LockIP"];
			bool flag = bool.Parse(WebConfigurationManager.get_AppSettings()["CheckT5Lock"]);
			CheckLockResult checkLockResult = new CheckLockResult();
			checkLockResult.LockResult = LockResult.NotFound;
			Tiny tiny = (Tiny)Activator.CreateInstance(Marshal.GetTypeFromCLSID(new Guid("0CA5A93D-F6BD-4398-809A-9F767276EF77")));
			if (text == "127.0.0.1")
			{
				tiny = (Tiny)Activator.CreateInstance(Marshal.GetTypeFromCLSID(new Guid("0CA5A93D-F6BD-4398-809A-9F767276EF77")));
				tiny.Initialize = true;
			}
			else
			{
				tiny = (Tiny)Activator.CreateInstance(Marshal.GetTypeFromCLSID(new Guid("0CA5A93D-F6BD-4398-809A-9F767276EF77")));
				tiny.ServerIP = text;
				tiny.NetWorkINIT = true;
			}
			bool flag2 = false;
			if (tiny.TinyErrCode == 0)
			{
				if (flag)
				{
					tiny.UserPassWord = "142F2357D240843E717DBF9D3C3D241";
				}
				else
				{
					tiny.UserPassWord = "907E939FBC60382EF4A9DF74CCA1D26E";
				}
				tiny.ShowTinyInfo = true;
				bool flag3 = false;
				if (tiny.TinyErrCode != 0)
				{
					tiny.NetWorkINIT = true;
					if (flag)
					{
						tiny.UserPassWord = "907E939FBC60382EF4A9DF74CCA1D26E";
					}
					else
					{
						tiny.UserPassWord = "142F2357D240843E717DBF9D3C3D241";
					}
					tiny.ShowTinyInfo = true;
					if (tiny.TinyErrCode == 0)
					{
						if (!flag)
						{
							flag2 = true;
						}
						flag3 = true;
					}
				}
				else
				{
					if (flag)
					{
						flag2 = true;
					}
					flag3 = true;
				}
				if (!flag3)
				{
					tiny = (Tiny)Activator.CreateInstance(Marshal.GetTypeFromCLSID(new Guid("0CA5A93D-F6BD-4398-809A-9F767276EF77")));
					tiny.ServerIP = text;
					tiny.NetWorkINIT = true;
					tiny.UserPassWord = "DEE723F852FAA81B102B1994CC97CD4D";
					tiny.ShowTinyInfo = true;
					if (tiny.TinyErrCode == 0)
					{
						flag2 = true;
						flag3 = true;
					}
				}
				if (flag3 && tiny.SpecialID.GetHashCode() == -1862267559)
				{
					string dataPartition = tiny.DataPartition;
					checkLockResult.PersonnelCount = tiny.Counter;
					if (dataPartition[0] == 'B')
					{
						checkLockResult.LockResult = LockResult.Blocked;
					}
					else
					{
						string[] array = dataPartition.Split(new char[1]
						{
							';'
						}, StringSplitOptions.RemoveEmptyEntries);
						if (array.Length >= 3)
						{
							string value = array[0];
							try
							{
								int num = Convert.ToInt32(value);
								DateTime dateTime = StringToDateTime(array[1]);
								DateTime dateTime2 = StringToDateTime(array[2]);
								if (num != 0)
								{
									DateTime dateTime3 = DateTime.Now;
									DateTime date = dateTime3.Date;
									dateTime3 = dateTime.AddDays((double)num);
									if (date > dateTime3.Date)
									{
										checkLockResult.LockResult = LockResult.Expires;
									}
									else
									{
										dateTime3 = DateTime.Now;
										if (dateTime3.Date < dateTime2.Date)
										{
											tiny.DataPartition = "B" + dataPartition.Substring(1);
											checkLockResult.LockResult = LockResult.TimeChanged;
										}
										else
										{
											checkLockResult.LockResult = LockResult.OK;
										}
									}
								}
								else
								{
									checkLockResult.LockResult = LockResult.OK;
								}
								string[] array2;
								if (flag2)
								{
									array2 = array;
									for (int i = 0; i < array2.Length; i++)
									{
										if (array2[i].ToLower() == "mytime")
										{
											checkLockResult.LockResult = LockResult.OK;
										}
									}
								}
								array2 = array;
								foreach (string text2 in array2)
								{
									if (text2.ToLower() == "fullsizelogo")
									{
										checkLockResult.IsLogoFree = true;
									}
									if (text2.ToLower().Contains("timeapp"))
									{
										checkLockResult.TimeApp = text2;
									}
								}
								string str = "";
								for (int j = 3; j < array.Length; j++)
								{
									str = str + array[j] + ";";
								}
								return checkLockResult;
							}
							catch
							{
								checkLockResult.LockResult = LockResult.BadData;
								return checkLockResult;
							}
						}
						checkLockResult.LockResult = LockResult.BadData;
					}
				}
			}
			return checkLockResult;
		}
	}
}
