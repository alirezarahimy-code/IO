namespace MyTime3.Lock
{
	internal class CheckLockResult
	{
		public LockResult LockResult
		{
			get;
			set;
		}

		public int PersonnelCount
		{
			get;
			set;
		}

		public string UserName
		{
			get;
			set;
		}

		public bool IsLogoFree
		{
			get;
			set;
		}

		public string TimeApp
		{
			get;
			set;
		}

		public CheckLockResult()
		{
			LockResult = LockResult.NotFound;
			PersonnelCount = 0;
			UserName = "";
			IsLogoFree = false;
			TimeApp = "";
		}
	}
}
