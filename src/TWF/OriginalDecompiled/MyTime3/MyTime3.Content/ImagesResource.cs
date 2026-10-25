using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace MyTime3.Content
{
	[GeneratedCode("System.Resources.Tools.StronglyTypedResourceBuilder", "17.0.0.0")]
	[DebuggerNonUserCode]
	[CompilerGenerated]
	public class ImagesResource
	{
		private static ResourceManager resourceMan;

		private static CultureInfo resourceCulture;

		[EditorBrowsable(EditorBrowsableState.Advanced)]
		public static ResourceManager ResourceManager
		{
			get
			{
				if (resourceMan == null)
				{
					resourceMan = new ResourceManager("MyTime3.Content.ImagesResource", typeof(ImagesResource).Assembly);
				}
				return resourceMan;
			}
		}

		[EditorBrowsable(EditorBrowsableState.Advanced)]
		public static CultureInfo Culture
		{
			get
			{
				return resourceCulture;
			}
			set
			{
				resourceCulture = value;
			}
		}

		public static Bitmap Arm => ResourceManager.GetObject("Arm", resourceCulture);

		public static Bitmap Arm2 => ResourceManager.GetObject("Arm2", resourceCulture);

		public static Bitmap baner5 => ResourceManager.GetObject("baner5", resourceCulture);

		public static Bitmap baner93_11 => ResourceManager.GetObject("baner93_11", resourceCulture);

		public static Bitmap baner93_12 => ResourceManager.GetObject("baner93_12", resourceCulture);

		public static Bitmap baner93_3 => ResourceManager.GetObject("baner93_3", resourceCulture);

		public static Bitmap logo => ResourceManager.GetObject("logo", resourceCulture);

		public static Bitmap logo1 => ResourceManager.GetObject("logo1", resourceCulture);

		internal ImagesResource()
		{
		}
	}
}
