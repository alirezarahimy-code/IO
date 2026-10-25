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

		public PersonnelModel(long badgeNumber, string firstName, string lastName)
		{
			BadgeNumber = badgeNumber;
			FirstName = firstName;
			LastName = lastName;
		}
	}
}
