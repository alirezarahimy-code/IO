namespace MyTime3.Lock
{
    public enum LockResult
    {
        OK,
        NotFound,
        Blocked,
        Expires,
        TimeChanged,
        BadData
    }
}
