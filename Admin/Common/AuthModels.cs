namespace Admin.Common
{
    public sealed class LoginInput
    {
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public sealed class ChangePasswordInput
    {
        public string Old { get; set; } = "";
        public string New { get; set; } = "";
        public string Confirm { get; set; } = "";
    }
}
