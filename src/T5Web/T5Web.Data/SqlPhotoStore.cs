using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;

namespace T5Web.Data
{
    /// <summary>
    /// The photo archive over the Personnel catalog.
    ///
    /// Evidence for every column this class touches:
    ///   * <c>[UserInfo].[Photo]</c>      - <c>image</c>, vendor DDL (Personnel.sql);
    ///   * <c>[UserInfo].[FirstName]</c>, <c>[LastName]</c>, <c>[DeptID]</c>, <c>[BadgeNumber]</c>;
    ///   * <c>[Department].[DepartmentID]</c> / <c>[DepartmentName]</c>.
    ///
    /// A statement never spans the two catalogs (docs/09, BUG-30): the department name
    /// is completed by <see cref="PersonnelDirectory"/> - here both sides happen to live
    /// in Personnel, so the join is legal, but the code keeps the same one-catalog rule
    /// as everywhere else so a future split cannot silently break it.
    /// </summary>
    public sealed class SqlPhotoStore : IPhotoStore
    {
        /// <summary>
        /// Largest picture the portal will stream. A terminal bitmap is normally tens of
        /// KB; anything above this is a mistake in the database, and refusing it is
        /// cheaper than letting one request allocate a hundred megabytes.
        /// </summary>
        public const int MaxImageBytes = 4 * 1024 * 1024;

        private readonly Func<IDbConnection> _personnelFactory;
        private readonly PersonnelDirectory _personnel;

        public SqlPhotoStore(Func<IDbConnection> connectionFactory, PersonnelDirectory personnel = null)
        {
            if (connectionFactory == null)
            {
                throw new ArgumentNullException(nameof(connectionFactory));
            }
            _personnelFactory = connectionFactory;
            _personnel = personnel ?? new PersonnelDirectory(connectionFactory);
        }

        /// <summary>
        /// Opens the Personnel catalog. Named like every other personnel read in the
        /// solution so tools/engine-relations.py can prove the statement runs on the right
        /// connection (docs/09 layer 2).
        /// </summary>
        private IDbConnection OpenPersonnel()
        {
            IDbConnection connection = _personnelFactory();
            connection.Open();
            return connection;
        }

        public IReadOnlyList<PhotoRow> Search(string term, int? departmentId, bool missingOnly, int take)
        {
            string search = (term ?? string.Empty).Trim();
            const string sql = @"
SELECT TOP (@Take)
    u.[BadgeNumber] AS BadgeNumber,
    LTRIM(RTRIM(ISNULL(u.[FirstName], N'') + N' ' + ISNULL(u.[LastName], N''))) AS PersonName,
    ISNULL(u.[DeptID], 0) AS DeptId,
    ISNULL(DATALENGTH(u.[Photo]), 0) AS Bytes
FROM [UserInfo] u
WHERE (@MissingOnly = 0 OR u.[Photo] IS NULL OR DATALENGTH(u.[Photo]) = 0)
  AND (@DeptId = 0 OR u.[DeptID] = @DeptId)
  AND (@Term = N'' OR u.[FirstName] LIKE @Term OR u.[LastName] LIKE @Term
       OR CAST(u.[BadgeNumber] AS nvarchar(20)) LIKE @Term)
ORDER BY u.[BadgeNumber];";

            List<PhotoRecord> raw;
            using (IDbConnection connection = OpenPersonnel())
            {
                raw = connection.Query<PhotoRecord>(sql, new
                {
                    Take = Clamp(take, 1, 500, 60),
                    MissingOnly = missingOnly ? 1 : 0,
                    DeptId = departmentId.HasValue && departmentId.Value > 0 ? departmentId.Value : 0,
                    Term = search.Length == 0 ? string.Empty : "%" + search + "%"
                }).ToList();
            }

            IReadOnlyDictionary<int, string> departments = _personnel.DepartmentNames();
            return raw.Select(r => new PhotoRow
            {
                BadgeNumber = r.BadgeNumber,
                PersonName = (r.PersonName ?? string.Empty).Trim(),
                DeptName = departments.ContainsKey(r.DeptId) ? departments[r.DeptId] : string.Empty,
                Bytes = r.Bytes
            }).ToList();
        }

        public PhotoImage GetImage(long badgeNumber)
        {
            if (badgeNumber <= 0)
            {
                return null;
            }

            const string sql = @"
SELECT TOP 1 [BadgeNumber] AS BadgeNumber, [Photo] AS Content
FROM [UserInfo]
WHERE [BadgeNumber] = @Badge;";

            using (IDbConnection connection = OpenPersonnel())
            {
                PhotoBlob blob = connection.Query<PhotoBlob>(sql, new { Badge = badgeNumber }).FirstOrDefault();
                if (blob == null || blob.Content == null || blob.Content.Length == 0 ||
                    blob.Content.Length > MaxImageBytes)
                {
                    return null;
                }
                return new PhotoImage
                {
                    BadgeNumber = blob.BadgeNumber,
                    Content = blob.Content,
                    ContentType = Sniff(blob.Content)
                };
            }
        }

        public int CountWithPhoto()
        {
            const string sql = @"SELECT COUNT(1) FROM [UserInfo] WHERE [Photo] IS NOT NULL AND DATALENGTH([Photo]) > 0;";
            using (IDbConnection connection = OpenPersonnel())
            {
                return connection.ExecuteScalar<int>(sql);
            }
        }

        public int CountWithoutPhoto()
        {
            const string sql = @"SELECT COUNT(1) FROM [UserInfo] WHERE [Photo] IS NULL OR DATALENGTH([Photo]) = 0;";
            using (IDbConnection connection = OpenPersonnel())
            {
                return connection.ExecuteScalar<int>(sql);
            }
        }

        /// <summary>
        /// The column is an untyped image blob, so the terminal may have stored a JPEG,
        /// a PNG or a 24-bit BMP. Sniffing the magic bytes is the only honest way to tell
        /// the browser the truth - a wrong Content-Type makes the picture invisible.
        /// </summary>
        public static string Sniff(byte[] content)
        {
            if (content == null || content.Length < 4)
            {
                return "application/octet-stream";
            }
            if (content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
            {
                return "image/jpeg";
            }
            if (content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47)
            {
                return "image/png";
            }
            if (content[0] == 0x47 && content[1] == 0x49 && content[2] == 0x46)
            {
                return "image/gif";
            }
            if (content[0] == 0x42 && content[1] == 0x4D)
            {
                return "image/bmp";
            }
            return "application/octet-stream";
        }

        private static int Clamp(int value, int min, int max, int fallback)
        {
            if (value <= 0)
            {
                return fallback;
            }
            return value < min ? min : (value > max ? max : value);
        }

        private sealed class PhotoRecord
        {
            public long BadgeNumber { get; set; }
            public string PersonName { get; set; }
            public int DeptId { get; set; }
            public int Bytes { get; set; }
        }

        private sealed class PhotoBlob
        {
            public long BadgeNumber { get; set; }
            public byte[] Content { get; set; }
        }
    }
}
