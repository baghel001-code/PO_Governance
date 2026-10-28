using System.Text.Json.Nodes;
using Admin.Auth;
using Admin.Auth.Captcha;
using Application.Services.V1.Platform;
using Domain.Constants;
using Domain.Entities.AppSettings;
using Domain.Entities.Store;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace POGovernance.Tests.Platform
{
    /// <summary>
    /// QPS session security on the POGovApi sessions: one session per user, sign-out when the admin changes roles /
    /// deactivates / resets the password, idle timeout and absolute lifetime — each with the reason the portal shows.
    /// </summary>
    public class SessionSecurityTests
    {
        private const string Admin = "admin.user@x.com";
        private const string Buyer = "buyer.one@x.com";

        private sealed class Kit
        {
            public FakeStore Store { get; } = new();
            public FakeClock Clock { get; } = new();
            public PoGovSettings Cfg { get; } = new() { AdminEmail = Admin, LoginMode = LoginModes.Local, LoginFailureMinMs = 0 };   // defaults: single session, 30 min idle, 12 h
            public AccountsService Accounts { get; }

            public Kit(Action<PoGovSettings>? configure = null)
            {
                configure?.Invoke(Cfg);
                var audit = new AuditService(Store, Clock, NullLogger<AuditService>.Instance);
                Accounts = new AccountsService(Store, Options.Create(Cfg), Clock, audit, new PlainHasher(), new ScrSource(Store));
            }

            public async Task<string> SignIn(string email = Admin)
            {
                var r = await Accounts.LoginAsync(email, "1234", "10.0.0.1");
                Assert.Null(r.Error);
                return r.Token!;
            }

            public async Task<string?> Reason(string token)
            {
                var (u, reason) = await Accounts.SessionAsync(token);
                return u == null ? reason ?? "unknown" : null;
            }

            public void Minutes(double m) => Clock.Now = Clock.Now.AddMinutes(m);
        }

        [Fact]
        public async Task Signing_in_on_another_device_ends_the_first_session_as_replaced()
        {
            var k = new Kit();
            var first = await k.Accounts.LoginAsync(Admin, "1234", "pc");
            Assert.False(first.ReplacedOther);
            var second = await k.Accounts.LoginAsync(Admin, "1234", "laptop");
            Assert.True(second.ReplacedOther);
            Assert.Equal(SessionEndReasons.Replaced, await k.Reason(first.Token!));
            Assert.Null(await k.Reason(second.Token!));
            Assert.Contains(k.Store.Audit, a => a.Action == "login" && a.Detail!.Contains("signed out 1 other session"));
        }

        [Fact]
        public async Task Several_sessions_are_allowed_when_single_session_is_off()
        {
            var k = new Kit(c => c.SingleSession = false);
            var a = await k.SignIn();
            var b = await k.SignIn();
            Assert.Null(await k.Reason(a));
            Assert.Null(await k.Reason(b));
        }

        [Fact]
        public async Task Admin_changes_end_the_users_sessions_with_their_reason()
        {
            var k = new Kit();
            await k.Accounts.AddUserAsync(Buyer, "Buyer One", new[] { "buyer" }, Admin);
            var admin = await k.SignIn();

            var t = await k.SignIn(Buyer);
            await k.Accounts.UpdateUserAsync(Buyer, new JsonObject { ["name"] = "Buyer 1" }, Admin);   // a name change keeps the session
            Assert.Null(await k.Reason(t));
            await k.Accounts.UpdateUserAsync(Buyer, new JsonObject { ["roles"] = new JsonArray("buyer", "cathead") }, Admin);
            Assert.Equal(SessionEndReasons.Changed, await k.Reason(t));

            t = await k.SignIn(Buyer);
            await k.Accounts.UpdateUserAsync(Buyer, new JsonObject { ["active"] = false }, Admin);
            Assert.Equal(SessionEndReasons.Disabled, await k.Reason(t));
            await k.Accounts.UpdateUserAsync(Buyer, new JsonObject { ["active"] = true }, Admin);

            t = await k.SignIn(Buyer);
            await k.Accounts.ResetPasswordAsync(Buyer, Admin);
            Assert.Equal(SessionEndReasons.Password, await k.Reason(t));

            await k.Accounts.UpdateUserAsync(Buyer, new JsonObject { ["unlock"] = true }, Admin);
            var r = await k.Accounts.LoginAsync(Buyer, "1234", "ip");   // reset back to the default password
            await k.Accounts.DeleteUserAsync(Buyer, Admin);
            Assert.Equal(SessionEndReasons.Disabled, await k.Reason(r.Token!));

            Assert.Null(await k.Reason(admin));   // nobody else is touched
        }

        [Fact]
        public async Task An_SCR_upload_that_adds_roles_signs_the_user_in_again()
        {
            var p = new Platform();
            p.Cfg.SingleSession = true;
            await p.Accounts.AddUserAsync(Buyer, "", new[] { "planner" }, Admin);
            var t = (await p.Accounts.LoginAsync(Buyer, "1234", "ip")).Token!;
            await p.UploadScrAsync(("B1", 100, 299, Buyer, "cat.head@x.com"));
            await p.Accounts.SyncFromScrAsync((await new ScrSource(p.Store).GetScrRowsAsync()).Rows, Admin);
            Assert.Equal((null as AppUser, SessionEndReasons.Changed), await p.Accounts.SessionAsync(t));
        }

        [Fact]
        public async Task Idle_timeout_ends_the_session_and_activity_keeps_it()
        {
            var k = new Kit();
            var t = await k.SignIn();
            for (var i = 0; i < 6; i++)
            {
                k.Minutes(25);   // active every 25 minutes: still signed in after 2.5 hours
                Assert.Null(await k.Reason(t));
            }
            k.Minutes(31);
            Assert.Equal(SessionEndReasons.Idle, await k.Reason(t));
            Assert.Equal(SessionEndReasons.Idle, await k.Reason(t));   // stays ended
        }

        [Fact]
        public async Task Absolute_lifetime_ends_an_active_session()
        {
            var k = new Kit();
            var t = await k.SignIn();
            for (var m = 0; m < 12 * 60 - 20; m += 20)
            {
                k.Minutes(20);
                Assert.Null(await k.Reason(t));
            }
            k.Minutes(30);
            Assert.Equal(SessionEndReasons.Expired, await k.Reason(t));
        }

        [Fact]
        public async Task Sign_out_removes_the_session_and_ended_sessions_are_forgotten_after_a_day()
        {
            var k = new Kit();
            var a = await k.SignIn();
            var b = await k.SignIn();                       // a: replaced, kept as ended
            await k.Accounts.LogoutAsync(b);
            Assert.Equal("unknown", await k.Reason(b));
            Assert.True(((JsonObject)k.Store.Docs[DocNames.Sessions]).ContainsKey(a));
            k.Minutes(25 * 60);
            await k.SignIn();                               // the next sign-in prunes
            Assert.False(((JsonObject)k.Store.Docs[DocNames.Sessions]).ContainsKey(a));
        }

        [Fact]
        public void The_401_message_carries_the_reason_back_to_the_portal()
        {
            foreach (var r in new[] { SessionEndReasons.Replaced, SessionEndReasons.Changed, SessionEndReasons.Disabled,
                                      SessionEndReasons.Password, SessionEndReasons.Idle, SessionEndReasons.Expired })
            {
                Assert.Equal(r, SessionEndReasons.FromMessage(SessionEndReasons.Message(r)));
                Assert.NotNull(SignOutReasons.Text(r));
            }
            Assert.Equal(SessionEndReasons.PleaseLogIn, SessionEndReasons.Message(null));
            Assert.Null(SessionEndReasons.FromMessage(SessionEndReasons.PleaseLogIn));
            Assert.Equal(SignOutReasons.SignedOut, SignOutReasons.Normalize("<script>"));
        }
    }

    /// <summary>The portal side: CAPTCHA, one-time sign-in tickets, ended sessions, notices.</summary>
    public class PortalSignInSecurityTests
    {
        private sealed class Clock : TimeProvider
        {
            public DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
            public override DateTimeOffset GetUtcNow() => Now;
        }

        [Fact]
        public void Captcha_answers_are_single_use_case_insensitive_and_expire()
        {
            var clock = new Clock();
            var captchas = new CaptchaService(new PngCaptchaGenerator("AB3CD"), clock);
            var c = captchas.Create();
            Assert.StartsWith("data:image/png;base64,", c.ImageDataUrl);
            Assert.False(captchas.Validate(c.Id, "AB3CX"));   // wrong …
            Assert.False(captchas.Validate(c.Id, "AB3CD"));   // … and the image is used up
            c = captchas.Create();
            Assert.True(captchas.Validate(c.Id, " ab3 cd "));
            Assert.False(captchas.Validate(c.Id, "AB3CD"));
            c = captchas.Create();
            clock.Now = clock.Now.AddMinutes(6);
            Assert.False(captchas.Validate(c.Id, "AB3CD"));   // expired after 5 minutes
            Assert.False(captchas.Validate(null, "AB3CD"));
            Assert.False(captchas.Validate("forged", "AB3CD"));
        }

        [Fact]
        public void The_captcha_image_is_a_valid_png_and_the_code_uses_no_look_alikes()
        {
            var g = new PngCaptchaGenerator().Generate();
            Assert.Equal(PngCaptchaGenerator.Length, g.Answer.Length);
            Assert.All(g.Answer, ch => Assert.Contains(ch, PngCaptchaGenerator.Alphabet));
            Assert.DoesNotContain(g.Answer, ch => "0O1IL".Contains(ch));
            var png = Convert.FromBase64String(g.ImageBase64);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
            Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(png, 12, 4));
            Assert.Equal(0xCBF43926u, PngCaptchaGenerator.Crc32(System.Text.Encoding.ASCII.GetBytes("123456789")));   // CRC-32 check value
            Assert.NotEqual(g.Answer, new PngCaptchaGenerator().Generate().Answer + "x");
        }

        [Fact]
        public void Sign_in_tickets_are_single_use_and_short_lived()
        {
            var clock = new Clock();
            var store = new LoginTicketStore(clock);
            var t = store.Issue("session-token", rememberMe: true, replacedOtherSession: true);
            var redeemed = store.Redeem(t);
            Assert.Equal(("session-token", true, true), (redeemed!.SessionToken, redeemed.RememberMe, redeemed.ReplacedOtherSession));
            Assert.Null(store.Redeem(t));
            t = store.Issue("x", false, false);
            clock.Now = clock.Now.AddSeconds(61);
            Assert.Null(store.Redeem(t));
            Assert.Null(store.Redeem("short"));
        }

        [Fact]
        public void Ended_sessions_and_login_notices()
        {
            var clock = new Clock();
            var ended = new EndedSessions(clock);
            ended.Add("tok", SessionEndReasons.Replaced);
            Assert.True(ended.TryGet("tok", out var reason));
            Assert.Equal(SessionEndReasons.Replaced, reason);
            Assert.False(ended.TryGet("other", out _));
            clock.Now = clock.Now.AddHours(25);
            Assert.False(ended.TryGet("tok", out _));

            var notices = new LoginNotices(clock);
            notices.Add("tok", LoginNotices.DuplicateLogin);
            Assert.Equal(LoginNotices.DuplicateLogin, notices.Take("tok"));
            Assert.Null(notices.Take("tok"));

            var activity = new UserActivityState(clock);
            clock.Now = clock.Now.AddMinutes(10);
            Assert.Equal(TimeSpan.FromMinutes(10), activity.IdleFor);
            activity.Touch();
            Assert.Equal(TimeSpan.Zero, activity.IdleFor);
        }
    
        private sealed class Env : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
        {
            public string EnvironmentName { get; set; } = "Production";
            public string ApplicationName { get; set; } = "Admin";
            public string WebRootPath { get; set; } = "";
            public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
            public string ContentRootPath { get; set; } = "";
            public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        }

        private sealed class Broken : ICaptchaGenerator
        {
            public int Calls;
            public GeneratedCaptcha Generate() { Calls++; throw new PlatformNotSupportedException("no GDI+"); }
        }

        [Fact]
        public void VmmCaptcha_dll_loads_and_draws_on_Windows_and_falls_back_elsewhere()
        {
            var vmm = new VmmCaptchaGenerator(new Admin.Common.CaptchaSettings());
            if (OperatingSystem.IsWindows())
            {
                var g = vmm.Generate();
                Assert.Equal(5, g.Answer.Length);
                Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, Convert.FromBase64String(g.ImageBase64)[..4]);
            }
            else
            {
                // the .NET Framework 4.5 assembly and its System.Drawing reference resolve; only GDI+ is missing
                var e = Assert.ThrowsAny<Exception>(() => vmm.Generate());
                Assert.IsNotType<FileNotFoundException>(e);
                Assert.IsType<PlatformNotSupportedException>(e.InnerException ?? e);
            }
        }

        [Fact]
        public void A_failing_VmmCaptcha_switches_to_the_builtin_generator_once()
        {
            var broken = new Broken();
            var g = new FallbackCaptchaGenerator(broken, new PngCaptchaGenerator("K7MPQ"), NullLogger.Instance);
            Assert.Equal("K7MPQ", g.Generate().Answer);
            Assert.True(g.UsingFallback);
            Assert.Equal("K7MPQ", g.Generate().Answer);
            Assert.Equal(1, broken.Calls);   // not retried on every sign-in
        }

        [Fact]
        public void The_captcha_engine_follows_the_settings_and_the_platform()
        {
            var settings = new Admin.Common.PortalSettings { DevCaptchaCode = "AB3CD" };
            var log = NullLogger.Instance;
            // the fixed code only in Development
            Assert.Equal("AB3CD", Admin.ServiceRegistration.PortalRegistration.CaptchaGenerator(settings, new Env { EnvironmentName = "Development" }, log).Generate().Answer);
            var prod = Admin.ServiceRegistration.PortalRegistration.CaptchaGenerator(settings, new Env(), log);
            Assert.NotEqual("AB3CD", prod.Generate().Answer);
            if (OperatingSystem.IsWindows()) Assert.IsType<FallbackCaptchaGenerator>(prod);
            else Assert.IsType<PngCaptchaGenerator>(prod);
            settings.Captcha.Engine = "Builtin";
            Assert.IsType<PngCaptchaGenerator>(Admin.ServiceRegistration.PortalRegistration.CaptchaGenerator(settings, new Env(), log));
            Assert.Equal(6, new Admin.Common.CaptchaSettings { Length = 9 }.LengthOrDefault);   // the answer box holds 6
        }
    }
}
