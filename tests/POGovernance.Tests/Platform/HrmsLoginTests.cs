using System.Text.Json.Nodes;
using Application.Interfaces.V1;
using Application.Services.V1.Platform;
using Domain.Entities.AppSettings;
using Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace POGovernance.Tests.Platform
{
    /// <summary>
    /// HRMS sign-in (PoGovSettings:LoginMode = Hrms): Employee ID + HRMS password, only for employees the admin registered.
    /// The QPS gap — any HRMS employee could sign in and got a login created — must not exist here.
    /// </summary>
    public class HrmsLoginTests
    {
        private const string AdminEmail = "admin.user@x.com";

        /// <summary>HRMS stand-in: a password per employee code, and a log of every call.</summary>
        private sealed class FakeHrms : IHrmsClient
        {
            public readonly Dictionary<string, (HrmsEmployee Emp, string Password)> People = new(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Calls = new();
            public bool Down;
            public bool LockedCode104;
            public string? AnswerForCode;   // HRMS answers about another employee

            public void Add(string code, string name, string email, string password, bool active = true, bool resigned = false) =>
                People[code] = (new HrmsEmployee
                {
                    EmpCode = code, EmpName = name, EmpMailId = email, EmpDeptName = "Merchandising", EmpDesig = "Buyer",
                    EmpLocation = "Gurgaon HO", EmpStatus = active, IsResign = resigned, EmpGrade = 5, EmpGradeDesc = "M1"
                }, password);

            public Task<HrmsResult> VerifyPasswordAsync(string empCode, string password, CancellationToken ct = default)
            {
                Calls.Add("login:" + empCode);
                if (Down) return Task.FromResult(HrmsResult.Unavailable("down"));
                if (LockedCode104) return Task.FromResult(new HrmsResult(HrmsStatus.LockedInHrms));
                if (!People.TryGetValue(empCode, out var p) || p.Password != password) return Task.FromResult(new HrmsResult(HrmsStatus.Invalid));
                var emp = p.Emp;
                if (AnswerForCode != null) emp = new HrmsEmployee { EmpCode = AnswerForCode, EmpStatus = true };
                return Task.FromResult(new HrmsResult(HrmsStatus.Ok, emp));
            }

            public Task<HrmsResult> GetEmployeeAsync(string empCode, CancellationToken ct = default)
            {
                Calls.Add("lookup:" + empCode);
                if (Down) return Task.FromResult(HrmsResult.Unavailable("down"));
                return Task.FromResult(People.TryGetValue(empCode, out var p) ? new HrmsResult(HrmsStatus.Ok, p.Emp) : new HrmsResult(HrmsStatus.NotFound));
            }
        }

        private sealed class Kit
        {
            public FakeStore Store { get; } = new();
            public FakeClock Clock { get; } = new();
            public FakeHrms Hrms { get; } = new();
            public PoGovSettings Cfg { get; } = new() { AdminEmail = AdminEmail, LoginMode = LoginModes.Hrms, LoginFailureMinMs = 0 };
            public AccountsService Accounts { get; }

            public Kit(Action<PoGovSettings>? configure = null)
            {
                configure?.Invoke(Cfg);
                var audit = new AuditService(Store, Clock, NullLogger<AuditService>.Instance);
                Accounts = new AccountsService(Store, Options.Create(Cfg), Clock, audit, new PlainHasher(), new ScrSource(Store), Hrms);
                Hrms.Add("102345", "Ravi Kumar", "Ravi.Kumar@x.com", "Hrms#1");
                Hrms.Add("200001", "Sunita Rao", "sunita.rao@x.com", "Hrms#2");
                Hrms.Add("300003", "Left Already", "left@x.com", "Hrms#3", active: false, resigned: true);
                Hrms.Add("400004", "No Mail", "", "Hrms#4");
            }

            public Task<Application.DTOs.Platform.LoginResult> Login(string id, string pw) => Accounts.LoginAsync(id, pw, "10.0.0.1");
        }

        [Fact]
        public async Task An_employee_who_is_not_registered_cannot_sign_in_and_HRMS_is_not_called()
        {
            var k = new Kit();
            var (tok, err) = await k.Login("102345", "Hrms#1");   // right HRMS password, but not registered here
            Assert.Null(tok);
            Assert.Equal(AccountsService.BadEmployeeLogin, err);
            Assert.Empty(k.Hrms.Calls);
            Assert.Null(await k.Accounts.GetUserAsync("ravi.kumar@x.com"));   // nothing is created by a sign-in
            Assert.Contains(k.Store.Audit, a => a.Action == "login-fail" && a.Detail!.Contains("not a registered employee"));
        }

        [Fact]
        public async Task A_registered_employee_signs_in_with_Employee_ID_and_HRMS_password()
        {
            var k = new Kit();
            var u = await k.Accounts.AddEmployeeAsync(" 102345 ", new[] { "buyer" }, AdminEmail);
            Assert.Equal("ravi.kumar@x.com", u.Email);   // the HRMS email is the login key
            Assert.Equal("102345", u.EmpCode);
            Assert.Equal("Ravi Kumar", u.Name);
            Assert.Equal("Merchandising", u.Hr!.Department);
            Assert.Equal("", u.Pw);                      // no local password for employees
            Assert.False(u.MustChange);

            var (tok, err) = await k.Login("102345", "Hrms#1");
            Assert.Null(err);
            Assert.NotNull(tok);
            Assert.Equal("ravi.kumar@x.com", (await k.Accounts.SessionUserAsync(tok))!.Email);
            Assert.Equal(new[] { "lookup:102345", "login:102345" }, k.Hrms.Calls);

            var pub = k.Accounts.Public((await k.Accounts.GetUserAsync("ravi.kumar@x.com"))!);
            Assert.Equal("hrms", pub.LoginType);
            Assert.False(pub.NeedsEmpCode);
        }

        [Fact]
        public async Task Email_sign_in_is_refused_for_employees_even_with_the_right_HRMS_password()
        {
            var k = new Kit();
            await k.Accounts.AddEmployeeAsync("102345", new[] { "buyer" }, AdminEmail);
            k.Hrms.Calls.Clear();
            var (tok, err) = await k.Login("ravi.kumar@x.com", "Hrms#1");
            Assert.Null(tok);
            Assert.Equal("Sign in with your Employee ID and HRMS password.", err);
            Assert.Empty(k.Hrms.Calls);
        }

        [Fact]
        public async Task Wrong_HRMS_password_counts_and_locks_locally_then_HRMS_is_not_asked_any_more()
        {
            var k = new Kit();
            await k.Accounts.AddEmployeeAsync("102345", new[] { "buyer" }, AdminEmail);
            k.Hrms.Calls.Clear();
            for (var i = 0; i < 4; i++) Assert.Equal(AccountsService.BadEmployeeLogin, (await k.Login("102345", "bad")).Error);
            Assert.Equal("Too many wrong attempts — locked for 15 minutes.", (await k.Login("102345", "bad")).Error);
            Assert.Equal(5, k.Hrms.Calls.Count);
            var (tok, err) = await k.Login("102345", "Hrms#1");   // locked: refused before HRMS
            Assert.Null(tok);
            Assert.StartsWith("Too many wrong attempts. Try again in", err);
            Assert.Equal(5, k.Hrms.Calls.Count);
        }

        [Fact]
        public async Task HRMS_lockout_down_resigned_and_wrong_employee_answers()
        {
            var k = new Kit();
            await k.Accounts.AddEmployeeAsync("102345", new[] { "buyer" }, AdminEmail);

            k.Hrms.LockedCode104 = true;
            Assert.Contains("Unlock it in MyVishal", (await k.Login("102345", "Hrms#1")).Error);
            k.Hrms.LockedCode104 = false;

            k.Hrms.Down = true;
            Assert.Contains("temporarily unavailable", (await k.Login("102345", "Hrms#1")).Error);
            k.Hrms.Down = false;
            Assert.Equal(0, (await k.Accounts.GetUserAsync("ravi.kumar@x.com"))!.Failed);   // not the user's fault

            k.Hrms.AnswerForCode = "999999";
            Assert.Equal(AccountsService.BadEmployeeLogin, (await k.Login("102345", "Hrms#1")).Error);
            k.Hrms.AnswerForCode = null;

            k.Hrms.Add("102345", "Ravi Kumar", "ravi.kumar@x.com", "Hrms#1", active: true, resigned: true);
            Assert.Equal("Your HRMS record is not active. Contact the administrator.", (await k.Login("102345", "Hrms#1")).Error);
        }

        [Fact]
        public async Task An_inactive_login_is_refused_without_asking_HRMS()
        {
            var k = new Kit();
            await k.Accounts.AddEmployeeAsync("102345", new[] { "buyer" }, AdminEmail);
            await k.Accounts.UpdateUserAsync("ravi.kumar@x.com", new JsonObject { ["active"] = false }, AdminEmail);
            k.Hrms.Calls.Clear();
            Assert.Equal(AccountsService.BadEmployeeLogin, (await k.Login("102345", "Hrms#1")).Error);
            Assert.Empty(k.Hrms.Calls);
        }

        [Fact]
        public async Task Registration_takes_the_details_from_HRMS_and_refuses_unknown_resigned_and_duplicate_employees()
        {
            var k = new Kit();
            await Assert.ThrowsAsync<AppValidationException>(() => k.Accounts.AddEmployeeAsync("777777", new[] { "buyer" }, AdminEmail));
            var resigned = await Assert.ThrowsAsync<AppValidationException>(() => k.Accounts.AddEmployeeAsync("300003", new[] { "buyer" }, AdminEmail));
            Assert.Contains("resigned", resigned.Message);
            await k.Accounts.AddEmployeeAsync("102345", new[] { "buyer" }, AdminEmail);
            var dup = await Assert.ThrowsAsync<AppValidationException>(() => k.Accounts.AddEmployeeAsync("102345", new[] { "planner" }, AdminEmail));
            Assert.Contains("already registered", dup.Message);
            await Assert.ThrowsAsync<AppValidationException>(() => k.Accounts.AddEmployeeAsync("200001", Array.Empty<string>(), AdminEmail));

            // no email in HRMS: the admin's email is used
            var noMail = await Assert.ThrowsAsync<AppValidationException>(() => k.Accounts.AddEmployeeAsync("400004", new[] { "buyer" }, AdminEmail));
            Assert.Contains("HRMS has no email", noMail.Message);
            var u = await k.Accounts.AddEmployeeAsync("400004", new[] { "buyer" }, AdminEmail, "no.mail@x.com");
            Assert.Equal("no.mail@x.com", u.Email);

            k.Hrms.Down = true;
            var down = await Assert.ThrowsAsync<AppException>(() => k.Accounts.AddEmployeeAsync("200001", new[] { "buyer" }, AdminEmail));
            Assert.Equal(503, down.Status);
        }

        [Fact]
        public async Task Adding_by_email_is_refused_in_HRMS_mode()
        {
            var k = new Kit();
            var e = await Assert.ThrowsAsync<AppValidationException>(() => k.Accounts.AddUserAsync("someone@x.com", "Some One", new[] { "buyer" }, AdminEmail));
            Assert.Contains("Employee ID", e.Message);
        }

        [Fact]
        public async Task An_SCR_login_needs_an_Employee_ID_and_is_linked_by_registering_or_linking()
        {
            var k = new Kit();
            await k.Accounts.InitializeAsync();
            var rows = new List<DecisionEngine.Model.SheetRow>
            {
                Platform.Row(("Brand", "B1"), ("MC CD", "M1"), ("MRP", 299), ("Level 1 Email", "ravi.kumar@x.com"), ("Level 3 Email", "sunita.rao@x.com"))
            };
            await k.Accounts.SyncFromScrAsync(rows, AdminEmail);
            var scr = (await k.Accounts.GetUserAsync("ravi.kumar@x.com"))!;
            Assert.True(k.Accounts.Public(scr).NeedsEmpCode);
            Assert.Equal(AccountsService.BadEmployeeLogin, (await k.Login("102345", "Hrms#1")).Error);   // not linked yet
            Assert.Equal("Sign in with your Employee ID and HRMS password.", (await k.Login("ravi.kumar@x.com", "1234")).Error);

            // registering the employee links the SCR login and keeps its roles
            var u = await k.Accounts.AddEmployeeAsync("102345", new[] { "planner" }, AdminEmail);
            Assert.Equal(new[] { "planner", "buyer" }, u.Roles);
            Assert.Null((await k.Login("102345", "Hrms#1")).Error);

            // linking an existing login directly
            var s = await k.Accounts.LinkEmployeeAsync("sunita.rao@x.com", "200001", AdminEmail);
            Assert.Equal("200001", s.EmpCode);
            Assert.Null((await k.Login("200001", "Hrms#2")).Error);
            var taken = await Assert.ThrowsAsync<AppValidationException>(() => k.Accounts.LinkEmployeeAsync("sunita.rao@x.com", "102345", AdminEmail));
            Assert.Contains("already registered", taken.Message);
        }

        [Fact]
        public async Task HRMS_logins_have_no_local_password_to_reset_or_change()
        {
            var k = new Kit();
            await k.Accounts.AddEmployeeAsync("102345", new[] { "buyer" }, AdminEmail);
            await Assert.ThrowsAsync<AppValidationException>(() => k.Accounts.ResetPasswordAsync("ravi.kumar@x.com", AdminEmail));
            Assert.Contains("MyVishal", await k.Accounts.ChangePasswordAsync("ravi.kumar@x.com", "x", "Newpass123"));
            // the empty stored password never matches, not even through a local sign-in path
            k.Cfg.LoginMode = LoginModes.Local;
            Assert.Equal("Email or password is incorrect", (await k.Login("ravi.kumar@x.com", "")).Error);
        }

        [Fact]
        public async Task The_built_in_admin_keeps_email_sign_in_only_while_allowed()
        {
            var k = new Kit();
            Assert.Null((await k.Login("Admin.User@x.com", "1234")).Error);
            Assert.False(k.Accounts.Public((await k.Accounts.GetUserAsync(AdminEmail))!).NeedsEmpCode);

            var off = new Kit(c => c.AllowAdminLocalLogin = false);
            Assert.Equal("Sign in with your Employee ID and HRMS password.", (await off.Login(AdminEmail, "1234")).Error);
        }

        [Fact]
        public async Task AdminEmpCode_registers_the_built_in_admin_as_an_HRMS_login()
        {
            var k = new Kit(c => c.AdminEmpCode = "200001");
            await k.Accounts.InitializeAsync();
            var admin = (await k.Accounts.GetUserAsync(AdminEmail))!;
            Assert.Equal("200001", admin.EmpCode);
            Assert.Null((await k.Login("200001", "Hrms#2")).Error);
            Assert.Equal("Sign in with your Employee ID and HRMS password.", (await k.Login(AdminEmail, "1234")).Error);
        }

        [Fact]
        public async Task Failures_take_at_least_the_configured_time()
        {
            var k = new Kit(c => c.LoginFailureMinMs = 300);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await k.Login("555555", "x");
            Assert.True(sw.ElapsedMilliseconds >= 280, $"{sw.ElapsedMilliseconds} ms");
        }
    }
}
