using System.Web.Configuration;
using Personnel;
using T5V3.DB;
using TWF.Domain.Interfaces;

namespace MyTime3.Controllers
{
    /// <summary>
    /// Rewritten DBInitializer supporting both production T5V3/Personnel databases
    /// and deterministic in-memory test mode for automated verification.
    /// </summary>
    public static class DBInitializer
    {
        public static bool IsTestMode { get; private set; }

        public static IT5BackendAdapter TestBackendAdapter { get; private set; }

        public static IAppSettingsService TestSettingsService { get; private set; }

        public static ITwfDataRepository TestDataRepository { get; private set; }

        public static void EnableTestMode(
            IT5BackendAdapter backendAdapter,
            IAppSettingsService settingsService = null,
            ITwfDataRepository dataRepository = null)
        {
            TestBackendAdapter = backendAdapter;
            TestSettingsService = settingsService;
            TestDataRepository = dataRepository;
            IsTestMode = backendAdapter != null;
        }

        public static void DisableTestMode()
        {
            IsTestMode = false;
            TestBackendAdapter = null;
            TestSettingsService = null;
            TestDataRepository = null;
        }

        public static T5V3DBManager GetT5DBInstance()
        {
            return new T5V3DBManager(WebConfigurationManager.AppSettings["T5CnnStr"]);
        }

        public static PersonnelSystem GetPersonnelSystem()
        {
            return new PersonnelSystem(WebConfigurationManager.AppSettings["PersonnelCnnStr"]);
        }
    }
}
