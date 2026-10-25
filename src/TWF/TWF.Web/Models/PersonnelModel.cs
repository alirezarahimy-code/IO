namespace MyTime3.Models
{
	public class PersonnelModel
	{
		public long BadgeNumber
		{
			get;
			set;
		}

		public string FirstName
		{
			get;
			set;
		}

		public string LastName
		{
			get;
			set;
		}

		/// <summary>Operator-facing label: "first last" (used by request/deputy pickers).</summary>
		public string DisplayName
		{
			get
			{
				string full = ((FirstName ?? string.Empty) + " " + (LastName ?? string.Empty)).Trim();
				return string.IsNullOrEmpty(full) ? BadgeNumber.ToString() : full;
			}
		}

		public PersonnelModel(long badgeNumber, string firstName, string lastName)
		{
			BadgeNumber = badgeNumber;
			FirstName = firstName;
			LastName = lastName;
		}
	}
}
