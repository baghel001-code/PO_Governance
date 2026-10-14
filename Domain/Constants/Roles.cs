namespace Domain.Constants
{
    /// <summary>Application roles (accounts.py ROLES, in this order).</summary>
    public static class Roles
    {
        public const string Admin = "admin";
        public const string Planner = "planner";
        public const string CatHead = "cathead";
        public const string Buyer = "buyer";

        public static readonly string[] All = { Admin, Planner, CatHead, Buyer };

        public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
        {
            [Admin] = "Admin", [Planner] = "Planner", [CatHead] = "Category Head", [Buyer] = "Buyer"
        };

        /// <summary>Planner or admin ("staff": sees everything).</summary>
        public const string Staff = Admin + "," + Planner;
        public const string AnyRequester = Buyer + "," + CatHead + "," + Planner + "," + Admin;
    }
}
