using System;
using System.IO;
using System.Security.Cryptography;

namespace MyTime3.Authorization
{
	public static class Encryption
	{
		public static string Encrypt(string PlainText)
		{
			DESCryptoServiceProvider dESCryptoServiceProvider = new DESCryptoServiceProvider();
			dESCryptoServiceProvider.Key = new byte[8]
			{
				124,
				32,
				132,
				56,
				145,
				122,
				68,
				194
			};
			dESCryptoServiceProvider.IV = new byte[8]
			{
				12,
				75,
				32,
				56,
				43,
				78,
				99,
				222
			};
			MemoryStream memoryStream = new MemoryStream();
			CryptoStream cryptoStream = new CryptoStream(memoryStream, dESCryptoServiceProvider.CreateEncryptor(), CryptoStreamMode.Write);
			StreamWriter streamWriter = new StreamWriter(cryptoStream);
			streamWriter.WriteLine(PlainText);
			streamWriter.Close();
			cryptoStream.Close();
			byte[] array = memoryStream.ToArray();
			memoryStream.Close();
			string text = "";
			for (int i = 0; i < array.Length; i++)
			{
				text += array[i].ToString("000");
			}
			return text;
		}

		public static string Decrypt(string CypherText)
		{
			try
			{
				DESCryptoServiceProvider dESCryptoServiceProvider = new DESCryptoServiceProvider();
				byte[] array = new byte[CypherText.Length / 3];
				for (int i = 0; i < array.Length; i++)
				{
					array[i] = Convert.ToByte(CypherText.Substring(i * 3, 3));
				}
				dESCryptoServiceProvider.Key = new byte[8]
				{
					124,
					32,
					132,
					56,
					145,
					122,
					68,
					194
				};
				dESCryptoServiceProvider.IV = new byte[8]
				{
					12,
					75,
					32,
					56,
					43,
					78,
					99,
					222
				};
				MemoryStream memoryStream = new MemoryStream(array);
				CryptoStream cryptoStream = new CryptoStream(memoryStream, dESCryptoServiceProvider.CreateDecryptor(), CryptoStreamMode.Read);
				StreamReader streamReader = new StreamReader(cryptoStream);
				string text = "";
				while (!streamReader.EndOfStream)
				{
					text += streamReader.ReadLine();
				}
				streamReader.Close();
				cryptoStream.Close();
				memoryStream.Close();
				return text;
			}
			catch
			{
				return "";
			}
		}
	}
}
