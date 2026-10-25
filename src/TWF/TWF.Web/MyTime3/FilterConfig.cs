using System.Web.Mvc;
using MyTime3.Security;

namespace MyTime3
{
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            if (filters == null)
            {
                return;
            }

            filters.Add(new HandleErrorAttribute());

            // Shows one clear Persian page when the shared T5 schema does not match
            // this Aria build, instead of an "Invalid column name" SQL error mid-shift.
            filters.Add(new SchemaGuardFilter());
        }
    }
}
