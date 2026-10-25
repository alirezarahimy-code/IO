namespace MyTime3.Models
{
    public class ChangePasswordModel
    {
        public string OldPassword { get; set; }

        public string NewPassword { get; set; }

        public string ConfirmPassword { get; set; }

        public string ConfirmNewPassword
        {
            get => ConfirmPassword;
            set => ConfirmPassword = value;
        }

        public string Message { get; set; }
    }
}
