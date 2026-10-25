namespace MyTime3.Lock
{
	internal enum LockResult
	{
		OK,
		NotFound,
		Blocked,
		Expires,
		TimeChanged,
		BadData
	}
}
