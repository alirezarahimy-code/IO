using System;

namespace TD5.Model
{
    public class Request
    {
        public long RequestID { get; set; }

        public long BadgeNumber { get; set; }

        public int WofkflowType { get; set; }

        public int WorkflowType
        {
            get => WofkflowType;
            set => WofkflowType = value;
        }

        public DateTime CreateDate { get; set; }

        public DateTime ForDate { get; set; }

        public int LeaveID { get; set; }

        public int LeaveDescriptionID { get; set; }

        public DateTime FromTime { get; set; }

        public DateTime ToTime { get; set; }

        public DateTime RecordTime { get; set; }

        public int OverTimeType { get; set; }

        public double OverTimeAllowValue { get; set; }

        public bool IsSuspend { get; set; }

        public string Descriptions { get; set; }

        public bool IsDaily { get; set; }

        public double? Latitude { get; set; }

        public double? Longitude { get; set; }

        public byte[] ImageData { get; set; }

        public string ImageData64
        {
            get
            {
                try
                {
                    if (ImageData == null || ImageData.Length == 0)
                    {
                        return string.Empty;
                    }
                    return Convert.ToBase64String(ImageData);
                }
                catch
                {
                    return string.Empty;
                }
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    ImageData = null;
                }
                else
                {
                    ImageData = Convert.FromBase64String(value);
                }
            }
        }
    }
}
