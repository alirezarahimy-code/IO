using System.Collections.Generic;
using T5Web.Domain.Models;

namespace T5Web.Domain.Interfaces
{
    /// <summary>
    /// Generic table access for the module screens.
    ///
    /// Two implementations:
    ///   * SqlModuleStore        - Dapper, identifiers taken from the descriptor and
    ///                             re-validated, values always parameterised;
    ///   * InMemoryModuleStore   - the test double, which enforces the same key rules.
    ///
    /// The store never sees a raw user-supplied identifier: a descriptor is a compile-
    /// time object, and every column name in it is verified against the schema manifest
    /// by test T16 before the build is allowed to pass.
    /// </summary>
    public interface IModuleStore
    {
        ModulePage Query(ModuleDescriptor descriptor, string term, int page, int pageSize);

        ModuleRow Get(ModuleDescriptor descriptor, IReadOnlyDictionary<string, string> key);

        /// <summary>Inserts or updates depending on <paramref name="isNew"/>.</summary>
        OperationResult Save(ModuleDescriptor descriptor, ModuleRow row, bool isNew);

        OperationResult Delete(ModuleDescriptor descriptor, IReadOnlyDictionary<string, string> key);

        /// <summary>
        /// Key/label pairs for a picker, taken from the descriptor itself (its key plus
        /// <see cref="ModuleDescriptor.LabelColumn"/>). No caller ever passes a table or
        /// column name as a string, so there is no path from a request to an identifier.
        /// </summary>
        IReadOnlyList<KeyValuePair<string, string>> Options(ModuleDescriptor descriptor);
    }
}
