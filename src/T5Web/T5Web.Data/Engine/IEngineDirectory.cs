using System.Collections.Generic;
using T5Web.Domain.Models;

namespace T5Web.Data.Engine
{
    /// <summary>
    /// Read-only access to organisational master data, provided by the engine.
    ///
    /// Implementations:
    ///   * PersonnelEngineDirectory - production, calls Personnel.DB.*DA from the pinned
    ///     engine build (single source of truth for the schema);
    ///   * SqlSchemaDirectory       - fallback used only when the engine assemblies are
    ///     absent (unit tests, or a station where only the database is reachable).
    ///
    /// Reads throw <see cref="EngineAccessException"/> rather than returning an empty
    /// list: an empty chart and a broken chart look identical in the UI, and the
    /// operator would conclude that the organisation has no departments.
    /// </summary>
    public interface IEngineDirectory
    {
        IReadOnlyList<DepartmentNode> GetDepartments();

        IReadOnlyList<TitleNode> GetTitles();

        IReadOnlyList<KeyValuePair<int, string>> GetEmpTypes();

        IReadOnlyList<DeviceRow> GetDevices();
    }
}
