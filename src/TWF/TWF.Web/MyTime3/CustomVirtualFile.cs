using System.IO;
using System.Web.Hosting;

namespace MyTime3
{
    public class CustomVirtualFile : VirtualFile
    {
        private readonly byte[] _viewContent;

        public CustomVirtualFile(string virtualPath, byte[] viewContent)
            : base(virtualPath)
        {
            _viewContent = viewContent ?? new byte[0];
        }

        public override Stream Open()
        {
            return new MemoryStream(_viewContent, writable: false);
        }
    }
}
