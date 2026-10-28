using System.Security.Claims;
using Domain.Constants;
using Microsoft.AspNetCore.Components.Authorization;

namespace Admin.Services
{
    /// <summary>
    /// The signed-in person for this browser tab (Blazor circuit) and which set of screens they have
    /// open — the Planner view or the Category view (v1.1.4 "⇄ Switch Role").
    /// </summary>
    public sealed class UserSessionState
    {
        private readonly AuthenticationStateProvider _auth;
        private ClaimsPrincipal? _user;

        public UserSessionState(AuthenticationStateProvider auth) => _auth = auth;

        public event Action? Changed;

        public string Email => _user?.FindFirst(CustomClaimTypes.Email)?.Value ?? "";
        public string Name => _user?.FindFirst(ClaimTypes.Name)?.Value ?? "";
        public IReadOnlyList<string> Roles => _user?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? new List<string>();
        public bool MustChange => _user?.FindFirst(CustomClaimTypes.MustChange)?.Value == "true";
        /// <summary>Signs in with Employee ID + HRMS password (the password is changed in MyVishal, not here).</summary>
        public bool IsHrmsLogin => _user?.FindFirst(CustomClaimTypes.LoginType)?.Value == "hrms";
        public string EmpCode => _user?.FindFirst(CustomClaimTypes.EmpCode)?.Value ?? "";

        public bool Has(params string[] roles) => roles.Any(r => Roles.Contains(r));
        public bool IsAdmin => Has(Domain.Constants.Roles.Admin);
        /// <summary>Planner or admin.</summary>
        public bool IsStaff => Has(Domain.Constants.Roles.Admin, Domain.Constants.Roles.Planner);
        public bool IsCatHead => Has(Domain.Constants.Roles.CatHead);
        public bool CanApprove => IsStaff || IsCatHead;

        public string RoleLabel => string.Join(" · ", Roles.Select(r => Domain.Constants.Roles.Labels.TryGetValue(r, out var l) ? l : r));

        /// <summary>"planner" or "category". Staff start in the Planner view, everyone else in Category.</summary>
        public string View { get; private set; } = "category";

        public async Task InitAsync()
        {
            if (_user != null) return;
            var state = await _auth.GetAuthenticationStateAsync();
            _user = state.User;
            View = IsStaff ? "planner" : "category";
        }

        public void SwitchView(string view)
        {
            if (view == "planner" && !IsStaff) return;
            View = view;
            Changed?.Invoke();
        }
    }
}
