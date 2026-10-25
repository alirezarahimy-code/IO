using System.Globalization;
using System.Resources;

namespace MyTime3.Content
{
    public class Noti5
    {
        private static ResourceManager resourceMan;
        private static CultureInfo resourceCulture;

        public static ResourceManager ResourceManager
        {
            get
            {
                if (resourceMan == null)
                {
                    resourceMan = new ResourceManager("MyTime3.Content.Noti5", typeof(Noti5).Assembly);
                }
                return resourceMan;
            }
        }

        public static CultureInfo Culture
        {
            get => resourceCulture;
            set => resourceCulture = value;
        }

        public static byte[] Noti5Setup
        {
            get
            {
                try
                {
                    return (ResourceManager.GetObject("Noti5Setup", resourceCulture) as byte[]) ?? new byte[0];
                }
                catch
                {
                    return new byte[0];
                }
            }
        }
    }
}
