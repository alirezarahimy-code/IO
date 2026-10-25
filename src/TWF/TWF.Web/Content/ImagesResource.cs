using System.Drawing;
using System.Globalization;
using System.Resources;

namespace MyTime3.Content
{
    public class ImagesResource
    {
        private static ResourceManager resourceMan;
        private static CultureInfo resourceCulture;

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

        public static CultureInfo Culture
        {
            get => resourceCulture;
            set => resourceCulture = value;
        }

        private static Bitmap GetBitmapOrFallback(string name)
        {
            try
            {
                return (ResourceManager.GetObject(name, resourceCulture) as Bitmap) ?? new Bitmap(1, 1);
            }
            catch
            {
                return new Bitmap(1, 1);
            }
        }

        public static Bitmap Arm => GetBitmapOrFallback("Arm");
        public static Bitmap Arm2 => GetBitmapOrFallback("Arm2");
        public static Bitmap baner5 => GetBitmapOrFallback("baner5");
        public static Bitmap baner93_11 => GetBitmapOrFallback("baner93_11");
        public static Bitmap baner93_12 => GetBitmapOrFallback("baner93_12");
        public static Bitmap baner93_3 => GetBitmapOrFallback("baner93_3");
        public static Bitmap logo => GetBitmapOrFallback("logo");
        public static Bitmap logo1 => GetBitmapOrFallback("logo1");
    }
}
