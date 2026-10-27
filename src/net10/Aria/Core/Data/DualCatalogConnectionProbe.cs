using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using TWF.Domain.Models;

namespace Aria.Core.Data
{
    /// <summary>
    /// A narrow, provider-neutral connection-reachability seam for Aria's two
    /// catalogs. Factories must open NEW Personnel and T5 connections separately.
    /// No provider, connection string, schema check or SQL server is shipped here.
    /// Never use this result as proof of SQL Server 2016/schema/role readiness.
    /// </summary>
    public sealed class DualCatalogConnectionProbe
    {
        private readonly Func<DbConnection> _personnel;
        private readonly Func<DbConnection> _t5;

        public DualCatalogConnectionProbe(Func<DbConnection> personnel, Func<DbConnection> t5)
        {
            _personnel = personnel ?? throw new ArgumentNullException(nameof(personnel));
            _t5 = t5 ?? throw new ArgumentNullException(nameof(t5));
        }

        public async Task<DatabaseHealthStatus> CheckAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = new DatabaseHealthStatus();
            result.IsPersonnelDbConnected = await CanOpenAsync(_personnel, cancellationToken).ConfigureAwait(false);
            if (!result.IsPersonnelDbConnected)
                result.PersonnelDbError = "اتصال پرسنلی در دسترس نیست.";

            cancellationToken.ThrowIfCancellationRequested();
            result.IsT5DbConnected = await CanOpenAsync(_t5, cancellationToken).ConfigureAwait(false);
            if (!result.IsT5DbConnected)
                result.T5DbError = "اتصال دادهٔ تردد در دسترس نیست.";
            // The DTO's IsHealthy ANDs both flags. This checks ONLY connection
            // state; schema, Windows identity, license, engine and queries are
            // separate release gates and are not silently inferred from it.
            return result;
        }

        private static async Task<bool> CanOpenAsync(Func<DbConnection> create, CancellationToken cancellationToken)
        {
            try
            {
                using (DbConnection connection = create())
                {
                    if (connection == null)
                        return false;
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    return connection.State == ConnectionState.Open;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Drivers can embed connection strings/server names in exceptions.
                // Never echo their messages into a health DTO or HTTP response.
                return false;
            }
        }
    }
}
