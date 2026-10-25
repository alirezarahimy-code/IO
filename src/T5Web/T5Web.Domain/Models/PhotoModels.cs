using System;
using System.Collections.Generic;
using T5Web.Domain.Models;

namespace T5Web.Domain.Models
{
    /// <summary>
    /// One row of the personnel photo archive.
    ///
    /// The desktop client (T5V3, frmPhoto / frmLastPhotos / frmPhotosReport) stores the
    /// picture inside <c>[UserInfo].[Photo]</c>, a SQL Server <c>image</c> column - the
    /// vendor DDL is the evidence. The portal therefore reads the bytes from the
    /// Personnel catalog instead of a share such as <c>PhotosPath=E:\PHOTO</c>, which
    /// the legacy desktop only used as a scratch folder.
    ///
    /// Only the size is selected by the list query: pulling every image into memory to
    /// render a grid is exactly the kind of thing that makes a portal tip over on a
    /// 4,000-person site.
    /// </summary>
    public sealed class PhotoRow
    {
        public long BadgeNumber { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public string DeptName { get; set; } = string.Empty;
        public int Bytes { get; set; }

        public bool HasPhoto
        {
            get { return Bytes > 0; }
        }

        public string SizeFa
        {
            get
            {
                if (Bytes <= 0)
                {
                    return "—";
                }
                return Bytes >= 1024
                    ? Math.Round(Bytes / 1024.0, 0).ToString("0") + " KB"
                    : Bytes + " B";
            }
        }
    }

    /// <summary>One picture, ready to stream back to the browser.</summary>
    public sealed class PhotoImage
    {
        public long BadgeNumber { get; set; }
        public byte[] Content { get; set; }
        public string ContentType { get; set; } = "image/jpeg";

        public string FileName
        {
            get { return "photo-" + BadgeNumber + Extension; }
        }

        public string Extension
        {
            get
            {
                switch ((ContentType ?? string.Empty).ToLowerInvariant())
                {
                    case "image/png":
                        return ".png";
                    case "image/gif":
                        return ".gif";
                    case "image/bmp":
                        return ".bmp";
                    default:
                        return ".jpg";
                }
            }
        }
    }
}

namespace T5Web.Domain.Interfaces
{
    /// <summary>
    /// The personnel photo archive (T5V3 desktop: frmPhoto, frmLastPhotos,
    /// frmPhotosReport).
    ///
    /// Reads only. Nothing in the portal writes <c>[UserInfo].[Photo]</c>: the picture
    /// belongs to the device enrolment flow (fingerprint/face capture happens on the
    /// terminal and is written by the engine), so a web form that could replace it
    /// would silently desynchronise the terminal cache.
    /// </summary>
    public interface IPhotoStore
    {
        /// <summary>
        /// People and their picture size. <paramref name="missingOnly"/> flips the query
        /// to "who has no picture yet", which is the list an operator actually works
        /// through before an enrolment round.
        /// </summary>
        IReadOnlyList<PhotoRow> Search(string term, int? departmentId, bool missingOnly, int take);

        /// <summary>The picture bytes, or null when the person is unknown or has none.</summary>
        PhotoImage GetImage(long badgeNumber);

        int CountWithPhoto();

        int CountWithoutPhoto();
    }
}
