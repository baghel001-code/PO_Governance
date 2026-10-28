using System.Text.Json.Nodes;
using Application.Services.V1.Platform;
using Domain.Entities.AppSettings;
using Domain.Entities.Store;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace POGovernance.Tests.Platform
{
    /// <summary>
    /// Two API instances (two AccountsService) on one store: sessions and users written by one are seen by the other,
    /// and neither signs out the other's users (the SQL Server agent's report: sessions were kept in memory and never reloaded).
    /// </summary>
    public class MultiInstanceAccountsTests
    {
        private sealed class Instances
        {
            public FakeStore Store { get; } = new();
            public FakeClock Clock { get; } = new();
            public PoGovSettings Cfg { get; } = new() { AdminEmail = "admin.user@x.com", LoginMode = LoginModes.Local, LoginFailureMinMs = 0 };
            public AccountsService A { get; }
            public AccountsService B { get; }

            public Instances()
            {
                A = New();
                B = New();
            }

            private AccountsService New()
            {
                var audit = new AuditService(Store, Clock, NullLogger<AuditService>.Instance);
                return new AccountsService(Store, Options.Create(Cfg), Clock, audit, new PlainHasher(), new ScrSource(Store));
            }

            public JsonObject StoredSessions => (JsonObject)(Store.Docs[DocNames.Sessions]);
        }

        [Fact]
        public async Task A_session_signed_in_on_one_instance_is_valid_on_the_other()
        {
            var x = new Instances();
            await x.A.InitializeAsync();
            await x.B.InitializeAsync();
            var (tok, err) = await x.A.LoginAsync("admin.user@x.com", "1234", "ip-a");
            Assert.Null(err);
            Assert.Equal("admin.user@x.com", (await x.B.SessionUserAsync(tok))?.Email);
        }

        [Fact]
        public async Task Logins_on_both_instances_keep_each_others_sessions()
        {
            var x = new Instances();
            await x.A.AddUserAsync("buyer.one@x.com", "", new[] { "buyer" }, "admin.user@x.com");
            await x.B.InitializeAsync();
            var (ta, _) = await x.A.LoginAsync("admin.user@x.com", "1234", "ip-a");
            var (tb, errB) = await x.B.LoginAsync("buyer.one@x.com", "1234", "ip-b");   // B must see the user A added
            Assert.Null(errB);
            // B's write must not drop A's session (and vice versa)
            Assert.True(x.StoredSessions.ContainsKey(ta!));
            Assert.True(x.StoredSessions.ContainsKey(tb!));
            var (ta2, _) = await x.A.LoginAsync("admin.user@x.com", "1234", "ip-a2");
            Assert.True(x.StoredSessions.ContainsKey(tb!));
            Assert.NotNull(await x.A.SessionUserAsync(tb));
            Assert.NotNull(await x.B.SessionUserAsync(ta));
            Assert.NotNull(await x.B.SessionUserAsync(ta2));
        }

        [Fact]
        public async Task Logout_on_one_instance_signs_out_only_that_session_everywhere()
        {
            var x = new Instances();
            await x.A.AddUserAsync("buyer.one@x.com", "", new[] { "buyer" }, "admin.user@x.com");
            var (ta, _) = await x.A.LoginAsync("admin.user@x.com", "1234", "ip-a");
            var (tb, _) = await x.B.LoginAsync("buyer.one@x.com", "1234", "ip-b");
            Assert.NotNull(await x.A.SessionUserAsync(ta));
            Assert.NotNull(await x.A.SessionUserAsync(tb));   // A has now read B's session

            await x.B.LogoutAsync(ta!);                       // signed out through instance B
            Assert.Null(await x.A.SessionUserAsync(ta));      // … A no longer accepts it
            Assert.NotNull(await x.A.SessionUserAsync(tb));   // … and B's user is still signed in on both
            Assert.NotNull(await x.B.SessionUserAsync(tb));

            await x.A.LogoutAsync(tb!);
            Assert.Null(await x.B.SessionUserAsync(tb));
            Assert.Empty(x.StoredSessions);
        }

        [Fact]
        public async Task Password_reset_on_one_instance_ends_the_sessions_known_to_the_other()
        {
            var x = new Instances();
            await x.A.AddUserAsync("buyer.one@x.com", "", new[] { "buyer" }, "admin.user@x.com");
            var (tb, _) = await x.B.LoginAsync("buyer.one@x.com", "1234", "ip-b");
            var (ta, _) = await x.B.LoginAsync("admin.user@x.com", "1234", "ip-b");
            await x.A.ResetPasswordAsync("buyer.one@x.com", "admin.user@x.com");   // A never saw tb before
            Assert.Null(await x.B.SessionUserAsync(tb));
            Assert.NotNull(await x.B.SessionUserAsync(ta));
            Assert.True((await x.B.GetUserAsync("buyer.one@x.com"))!.MustChange);
        }

        [Fact]
        public async Task Deactivating_a_user_on_one_instance_is_seen_by_the_other()
        {
            var x = new Instances();
            await x.A.AddUserAsync("buyer.one@x.com", "", new[] { "buyer" }, "admin.user@x.com");
            var (tb, _) = await x.B.LoginAsync("buyer.one@x.com", "1234", "ip-b");
            Assert.NotNull(await x.B.SessionUserAsync(tb));
            await x.A.UpdateUserAsync("buyer.one@x.com", new JsonObject { ["active"] = false }, "admin.user@x.com");
            Assert.Null(await x.B.SessionUserAsync(tb));
            // B adding a user afterwards must not resurrect the old (active) copy of buyer.one
            await x.B.AddUserAsync("buyer.two@x.com", "", new[] { "buyer" }, "admin.user@x.com");
            Assert.False((await x.A.GetUserAsync("buyer.one@x.com"))!.Active);
            Assert.NotNull(await x.A.GetUserAsync("buyer.two@x.com"));
        }

        [Fact]
        public async Task Last_seen_kept_by_both_instances_does_not_expire_an_active_session()
        {
            var x = new Instances();
            var (tok, _) = await x.A.LoginAsync("admin.user@x.com", "1234", "ip");
            for (var h = 0; h < 30; h += 2)
            {
                x.Clock.Now = x.Clock.Now.AddHours(2);
                var on = h % 4 == 0 ? x.A : x.B;          // the user's requests alternate between the instances
                Assert.NotNull(await on.SessionUserAsync(tok));
            }
        }
    }
}
