namespace MyTime3.Lock
{
    public class CheckLockResult
    {
        public LockResult LockResult { get; set; }

        public int PersonnelCount { get; set; }

        public string UserName { get; set; }

        public bool IsLogoFree { get; set; }

        public string TimeApp { get; set; }

        public CheckLockResult()
        {
            LockResult = LockResult.OK;
            PersonnelCount = int.MaxValue;
            UserName = "Unrestricted";
            IsLogoFree = true;
            TimeApp = string.Empty;
        }
    }
}
