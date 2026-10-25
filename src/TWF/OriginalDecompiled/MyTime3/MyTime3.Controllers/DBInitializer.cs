using Personnel;
using System.Web.Configuration;
using T5V3.DB;

namespace MyTime3.Controllers
{
	public static class DBInitializer
	{
		public static T5V3DBManager GetT5DBInstance()
		{
			return new T5V3DBManager(WebConfigurationManager.get_AppSettings()["T5CnnStr"]);
		}

		public static PersonnelSystem GetPersonnelSystem()
		{
			return new PersonnelSystem(WebConfigurationManager.get_AppSettings()["PersonnelCnnStr"]);
		}
	}
}
