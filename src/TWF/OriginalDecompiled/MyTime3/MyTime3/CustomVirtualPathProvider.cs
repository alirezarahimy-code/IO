using System.IO;
using System.Security.Cryptography;
using System.Web;
using System.Web.Hosting;

namespace MyTime3
{
	public class CustomVirtualPathProvider
	{
		private byte[] Decrypt(byte[] CypherText)
		{
			try
			{
				DESCryptoServiceProvider dESCryptoServiceProvider = new DESCryptoServiceProvider();
				dESCryptoServiceProvider.Key = new byte[8]
				{
					5,
					163,
					253,
					35,
					83,
					162,
					42,
					123
				};
				dESCryptoServiceProvider.IV = new byte[8]
				{
					237,
					14,
					38,
					22,
					15,
					13,
					246,
					123
				};
				MemoryStream memoryStream = new MemoryStream(CypherText);
				CryptoStream cryptoStream = new CryptoStream(memoryStream, dESCryptoServiceProvider.CreateDecryptor(), CryptoStreamMode.Read);
				byte[] result = default(byte[]);
				using (MemoryStream memoryStream2 = new MemoryStream())
				{
					byte[] array = new byte[1024];
					for (int num = cryptoStream.Read(array, 0, array.Length); num > 0; num = cryptoStream.Read(array, 0, array.Length))
					{
						memoryStream2.Write(array, 0, num);
					}
					cryptoStream.Flush();
					result = memoryStream2.ToArray();
				}
				cryptoStream.Close();
				memoryStream.Close();
				return result;
			}
			catch
			{
				return new byte[0];
			}
		}

		public override bool FileExists(string virtualPath)
		{
			return this.FileExists(virtualPath);
		}

		public override VirtualFile GetFile(string virtualPath)
		{
			//IL_001b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0020: Unknown result type (might be due to invalid IL or missing references)
			virtualPath = virtualPath.Replace('/', '\\').Replace("~", "");
			string text = HttpContext.get_Current().get_Server().MapPath("/");
			if (text.EndsWith("\\"))
			{
				text = text.Substring(0, text.Length - 1);
			}
			return new CustomVirtualFile(virtualPath, File.ReadAllBytes(text + virtualPath));
		}

		public CustomVirtualPathProvider()
			: this()
		{
		}
	}
}
