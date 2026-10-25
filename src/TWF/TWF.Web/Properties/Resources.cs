using System.Drawing;
using System.Globalization;
using System.Resources;

namespace MyTime3.Properties
{
    public class Resources
    {
        private static ResourceManager resourceMan;
        private static CultureInfo resourceCulture;

        public static ResourceManager ResourceManager
        {
            get
            {
                if (resourceMan == null)
                {
                    resourceMan = new ResourceManager("MyTime3.Properties.Resources", typeof(Resources).Assembly);
                }
                return resourceMan;
            }
        }

        public static CultureInfo Culture
        {
            get => resourceCulture;
            set => resourceCulture = value;
        }

        public static Bitmap logo
        {
            get
            {
                try
                {
                    return (ResourceManager.GetObject("logo", resourceCulture) as Bitmap) ?? new Bitmap(1, 1);
                }
                catch
                {
                    return new Bitmap(1, 1);
                }
            }
        }
    }
}
