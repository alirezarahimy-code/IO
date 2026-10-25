using MyTime3.Content;
using System;

namespace MyTime3.Models
{
	public class GeneralRequest
	{
		public string UserName
		{
			get;
			set;
		}

		public string Password
		{
			get;
			set;
		}

		public bool IsNew
		{
			get;
			set;
		}

		public long? FromUser
		{
			get;
			set;
		}

		public int RequestType
		{
			get;
			set;
		}

		public string RequestTypeStr
		{
			get
			{
				if (RequestType == 0)
				{
					return PersianString.DailyLeave;
				}
				if (RequestType == 1)
				{
					return PersianString.HourlyLeave;
				}
				if (RequestType == 2)
				{
					return PersianString.DailyDuty;
				}
				if (RequestType == 3)
				{
					return PersianString.HourlyDuty;
				}
				if (RequestType == 4)
				{
					return PersianString.InsertRecord;
				}
				if (RequestType == 5)
				{
					return PersianString.RequestLate;
				}
				if (RequestType == 6)
				{
					return PersianString.RequestEarly;
				}
				return PersianString.RequestOverTime;
			}
		}

		public long RequestID
		{
			get;
			set;
		}

		public int LeaveTypeID
		{
			get;
			set;
		}

		public int LeaveDescriptionsID
		{
			get;
			set;
		}

		public string Descriptions
		{
			get;
			set;
		}

		public string FromDateHijri
		{
			get;
			set;
		}

		public string ToDateHijri
		{
			get;
			set;
		}

		public string ForDateHijri
		{
			get;
			set;
		}

		public string FromTime
		{
			get;
			set;
		}

		public string ToTime
		{
			get;
			set;
		}

		public string ForTime
		{
			get;
			set;
		}

		public string OvertimeValue
		{
			get;
			set;
		}

		public double Longitude
		{
			get;
			set;
		}

		public double Latitude
		{
			get;
			set;
		}

		public string ImageData
		{
			get;
			set;
		}

		public string Message
		{
			get;
			set;
		}

		public GeneralRequest()
		{
			IsNew = true;
		}

		public AppExtraDataForRequest GetAppExtraData()
		{
			AppExtraDataForRequest appExtraDataForRequest = new AppExtraDataForRequest();
			if (ImageData != null)
			{
				appExtraDataForRequest.ImageData = Convert.FromBase64String(ImageData);
			}
			appExtraDataForRequest.latitude = Latitude;
			appExtraDataForRequest.longitude = Longitude;
			return appExtraDataForRequest;
		}
	}
}
