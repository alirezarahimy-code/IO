using System;
using System.IO;
using System.Web;
using System.Web.Hosting;

namespace MyTime3
{
    /// <summary>
    /// Rewritten CustomVirtualPathProvider.
    /// Fixes the infinite recursion bug in legacy FileExists(virtualPath) and serves standard
    /// unencrypted Razor views directly from disk.
    /// </summary>
    public class CustomVirtualPathProvider : VirtualPathProvider
    {
        public override bool FileExists(string virtualPath)
        {
            string physicalPath = ResolvePhysicalPath(virtualPath);
            if (!string.IsNullOrWhiteSpace(physicalPath) && File.Exists(physicalPath))
            {
                return true;
            }
            return Previous != null && Previous.FileExists(virtualPath);
        }

        public override VirtualFile GetFile(string virtualPath)
        {
            string physicalPath = ResolvePhysicalPath(virtualPath);
            if (!string.IsNullOrWhiteSpace(physicalPath) && File.Exists(physicalPath))
            {
                return new CustomVirtualFile(virtualPath, File.ReadAllBytes(physicalPath));
            }
            return Previous?.GetFile(virtualPath);
        }

        private static string ResolvePhysicalPath(string virtualPath)
        {
            if (string.IsNullOrWhiteSpace(virtualPath))
                return string.Empty;

            try
            {
                string clean = virtualPath.Replace('~', ' ').Trim().Replace('/', Path.DirectorySeparatorChar);
                string root = HttpContext.Current?.Server?.MapPath("/") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(root))
                    return clean;

                return Path.Combine(root.TrimEnd('\\', '/'), clean.TrimStart('\\', '/'));
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
