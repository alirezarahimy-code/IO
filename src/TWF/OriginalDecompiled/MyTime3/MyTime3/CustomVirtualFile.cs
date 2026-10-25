using System.IO;

namespace MyTime3
{
	public class CustomVirtualFile
	{
		private byte[] viewContent;

		public CustomVirtualFile(string virtualPath, byte[] viewContent)
			: this(virtualPath)
		{
			this.viewContent = viewContent;
		}

		public override Stream Open()
		{
			return new MemoryStream(viewContent);
		}
	}
}
