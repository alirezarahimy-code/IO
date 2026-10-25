using System;
using System.Collections.Generic;
using System.Linq;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;

namespace T5Web.Data
{
    /// <summary>
    /// In-memory photo archive for the tests and the offline demo.
    ///
    /// It mirrors <see cref="SqlPhotoStore"/> exactly where the screens depend on it:
    /// the search/term filter, the "who has no picture" flip, the byte cap and the
    /// content sniffing. That way T31 asserts the behaviour of the real rule, not of a
    /// stub that always says yes.
    /// </summary>
    public sealed class InMemoryPhotoStore : IPhotoStore
    {
        private readonly Dictionary<long, PhotoImage> _images = new Dictionary<long, PhotoImage>();
        private readonly Dictionary<long, PhotoRow> _rows = new Dictionary<long, PhotoRow>();

        public InMemoryPhotoStore Person(long badge, string name, string department)
        {
            _rows[badge] = new PhotoRow
            {
                BadgeNumber = badge,
                PersonName = name,
                DeptName = department,
                Bytes = 0
            };
            return this;
        }

        public InMemoryPhotoStore WithPhoto(long badge, byte[] content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }
            _images[badge] = new PhotoImage
            {
                BadgeNumber = badge,
                Content = content,
                ContentType = SqlPhotoStore.Sniff(content)
            };
            PhotoRow row;
            if (_rows.TryGetValue(badge, out row))
            {
                row.Bytes = content.Length;
            }
            else
            {
                _rows[badge] = new PhotoRow
                {
                    BadgeNumber = badge,
                    PersonName = "پرسنل " + badge,
                    Bytes = content.Length
                };
            }
            return this;
        }

        public IReadOnlyList<PhotoRow> Search(string term, int? departmentId, bool missingOnly, int take)
        {
            string search = (term ?? string.Empty).Trim();
            IEnumerable<PhotoRow> query = _rows.Values;
            if (missingOnly)
            {
                query = query.Where(r => !r.HasPhoto);
            }
            // The department filter is a Personnel-catalog concern (Department.DepartmentID);
            // PhotoRow only carries the name, so the in-memory store filters on what it has:
            // nothing. The query/term/missingOnly behaviour - the part the screens depend on -
            // is mirrored exactly. The SQL store does filter by [UserInfo].[DeptID].
            if (search.Length > 0)
            {
                query = query.Where(r => r.PersonName.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                                         r.BadgeNumber.ToString().IndexOf(search, StringComparison.Ordinal) >= 0);
            }

            int limit = take <= 0 ? 60 : take;
            return query.OrderBy(r => r.BadgeNumber).Take(limit).ToList();
        }

        public PhotoImage GetImage(long badgeNumber)
        {
            PhotoImage image;
            if (!_images.TryGetValue(badgeNumber, out image))
            {
                return null;
            }
            if (image.Content == null || image.Content.Length == 0 || image.Content.Length > SqlPhotoStore.MaxImageBytes)
            {
                return null;
            }
            return image;
        }

        public int CountWithPhoto()
        {
            return _rows.Values.Count(r => r.HasPhoto);
        }

        public int CountWithoutPhoto()
        {
            return _rows.Values.Count(r => !r.HasPhoto);
        }
    }
}
