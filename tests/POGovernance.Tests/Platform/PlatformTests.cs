using System.Text.Json.Nodes;
using Application.Services.V1.Platform;
using Domain.Entities.Store;
using Domain.Exceptions;
using Infrastructure.Services.V1;

namespace POGovernance.Tests.Platform
{
    public class PasswordHashTests
    {
        // Generated with Python 3: hashlib.pbkdf2_hmac('sha256', 'Secret#123ü'.encode(), bytes(range(16)), 200000)
        private const string PythonHash = "AAECAwQFBgcICQoLDA0ODw==$kTUqlUuzuWs/XTYIuXuEj+tWQ3MY3JAgrbo95yN2I+s=";

        [Fact]
        public void Verifies_hash_created_by_python()
        {
            var h = new PasswordHasher();
            Assert.True(h.Verify("Secret#123ü", PythonHash));
            Assert.False(h.Verify("Secret#123u", PythonHash));
        }

        [Fact]
        public void Produces_the_python_format()
        {
            Assert.Equal(PythonHash, PasswordHasher.Hash("Secret#123ü", Enumerable.Range(0, 16).Select(i => (byte)i).ToArray()));
            var h = new PasswordHasher();
            var s = h.Hash("1234");
            Assert.Equal(2, s.Split('$').Length);
            Assert.Equal(16, Convert.FromBase64String(s.Split('$')[0]).Length);
            Assert.True(h.Verify("1234", s));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("garbage")]
        [InlineData("!!$!!")]
        public void Bad_stored_values_never_verify(string? stored) => Assert.False(new PasswordHasher().Verify("1234", stored));
    }

    public class PyJsonTests
    {
        [Fact]
        public void Dumps_matches_python_json_dumps()
        {
            var o = JsonNode.Parse("{\"a\":\"ü\",\"b\":[1,2.5,null,true],\"c\":{\"d\":\"x\\\"y\\n\"}}")!;
            Assert.Equal("{\"a\": \"\\u00fc\", \"b\": [1, 2.5, null, true], \"c\": {\"d\": \"x\\\"y\\n\"}}", PyJson.Dumps(o, ensureAscii: true));
            Assert.Equal("{\"a\": \"ü\", \"b\": [1, 2.5, null, true]}", PyJson.Dumps(JsonNode.Parse("{\"a\":\"ü\",\"b\":[1,2.5,null,true]}"), false));
        }
    }

    public class ReplayTests
    {
        private static JsonObject Req(int seq, string reqId, string? approvalStatus = null)
        {
            var r = new JsonObject { ["reqId"] = reqId, ["seq"] = seq, ["qty"] = 100 };
            if (approvalStatus != null) r["approval"] = new JsonObject { ["status"] = approvalStatus, ["approvers"] = new JsonArray("c@x.com") };
            return new JsonObject { ["type"] = "request", ["seq"] = seq, ["request"] = r };
        }

        [Fact]
        public void Approval_events_apply_qty_and_final_qty()
        {
            var recs = new List<JsonObject>
            {
                Req(1, "REQ-20261014-0001", "pending"),
                Req(2, "REQ-20261014-0002", "pending"),
                new() { ["type"] = "approval", ["seq"] = 3, ["reqId"] = "REQ-20261014-0001", ["status"] = "approved", ["qty"] = 80, ["by"] = "c@x.com", ["byName"] = "C", ["at"] = "t" },
                new() { ["type"] = "approval", ["seq"] = 4, ["reqId"] = "REQ-20261014-0002", ["status"] = "rejected", ["qty"] = null, ["comment"] = "no" },
            };
            var (reqs, seq) = PlatformCore.Replay(recs);
            Assert.Equal(4, seq);
            Assert.Equal(80, reqs[0]["finalApprovedQty"]!.GetValue<int>());
            Assert.Equal("approved", reqs[0]["approval"]!["status"]!.GetValue<string>());
            Assert.Equal("", reqs[0]["approval"]!["comment"]!.GetValue<string>());
            Assert.Equal(0, reqs[1]["finalApprovedQty"]!.GetValue<int>());
            Assert.Equal(0, reqs[1]["approval"]!["qty"]!.GetValue<int>());
            Assert.Equal("no", reqs[1]["approval"]!["comment"]!.GetValue<string>());
        }

        [Fact]
        public void Clear_empties_the_live_ledger_but_history_keeps_requests()
        {
            var recs = new List<JsonObject>
            {
                Req(1, "REQ-20261014-0001", "pending"),
                Req(2, "REQ-20261014-0002"),
                new() { ["type"] = "clear", ["seq"] = 3, ["by"] = "Planner P", ["at"] = "2026-10-14T12:00:00+05:30" },
                Req(4, "REQ-20261014-0004"),
            };
            var (live, seq) = PlatformCore.Replay(recs.Select(r => r.DeepClone().AsObject()));
            Assert.Equal(4, seq);
            Assert.Single(live);
            Assert.Equal("REQ-20261014-0004", live[0]["reqId"]!.GetValue<string>());

            var (all, _) = PlatformCore.Replay(recs, includeCleared: true);
            Assert.Equal(3, all.Count);
            Assert.Equal("Planner P", all[0]["cleared"]!["by"]!.GetValue<string>());
            Assert.Equal("cleared", all[0]["approval"]!["status"]!.GetValue<string>());
            Assert.Null(all[2]["cleared"]);
        }

        [Fact]
        public void Approval_after_clear_does_not_touch_cleared_requests()
        {
            var recs = new List<JsonObject>
            {
                Req(1, "REQ-20261014-0001", "pending"),
                new() { ["type"] = "clear", ["seq"] = 2 },
                new() { ["type"] = "approval", ["seq"] = 3, ["reqId"] = "REQ-20261014-0001", ["status"] = "approved", ["qty"] = 5 },
            };
            var (all, _) = PlatformCore.Replay(recs, includeCleared: true);
            Assert.Equal("cleared", all[0]["approval"]!["status"]!.GetValue<string>());
        }
    }

    public class NextNumberTests
    {
        [Fact]
        public void Running_number_per_day_ignores_backfilled_ids_and_other_days()
        {
            var log = new List<JsonObject>
            {
                new() { ["uploadId"] = "UPL-20261014-0001" },
                new() { ["uploadId"] = "UPL-20261014-0007", ["note"] = "x" },
                new() { ["uploadId"] = "UPL-20261014-9003" },   // back-filled (9001+)
                new() { ["uploadId"] = "UPL-20261013-0042" },
                new() { ["other"] = "UPL-20261014-0009" },        // any top-level string value counts (as in server.py)
                new() { ["nested"] = new JsonObject { ["x"] = "UPL-20261014-0050" } },
            };
            Assert.Equal("UPL-20261014-0010", PlatformCore.NextNumber(log, "20261014", "UPL"));
            Assert.Equal("UPL-20261015-0001", PlatformCore.NextNumber(log, "20261015", "UPL"));
            Assert.Equal("FB-20261014-0001", PlatformCore.NextNumber(new List<JsonObject>(), "20261014", "FB"));
        }

        [Fact]
        public async Task Upload_batches_and_feedback_get_sequential_ids()
        {
            var p = new Platform();
            Assert.Equal("UPL-20261014-0001", await p.Data.NewUploadBatchAsync("zip", new[] { "a.zip" }, Platform.Admin));
            Assert.Equal("UPL-20261014-0002", await p.Data.NewUploadBatchAsync("files", Array.Empty<string>(), Platform.Admin));
            var fb = new FeedbackService(p.Store, p.Clock, p.Audit);
            Assert.Equal("FB-20261014-0001", (await fb.CreateAsync(new JsonObject { ["message"] = "hi", ["rating"] = 9 }, Platform.Admin)).Id);
            Assert.Equal("FB-20261014-0002", (await fb.CreateAsync(new JsonObject { ["message"] = "again", ["rating"] = 4.7 }, Platform.Admin)).Id);
            var items = (await fb.ListAsync(false, Platform.Admin)).Items;
            Assert.Null(items.Single(x => x["id"]!.GetValue<string>() == "FB-20261014-0001")["rating"]);
            Assert.Equal(4, items.Single(x => x["id"]!.GetValue<string>() == "FB-20261014-0002")["rating"]!.GetValue<int>());
        }

        [Fact]
        public void New_id_has_the_python_format()
        {
            var id = PlatformCore.NewId(new FakeClock());
            Assert.Matches(@"^20261014-103000-[0-9a-f]{4}$", id);
        }
    }

    public class ScrTests
    {
        [Fact]
        public void Analyse_guesses_roles_and_key_columns()
        {
            var p = new Platform();
            var rows = new[]
            {
                Platform.Row(("Brand Type", "VM"), ("MC Code", 1001.0), ("MRP Value", 299.0), ("Buyer Email", "b1@x.com"), ("Buyer Name", "Bee One"),
                             ("Category Head Mail", "c@x.com"), ("Cat Head Name", "Cee"), ("Division Head", "d@x.com"), ("Level 2", "l2@x.com")),
                Platform.Row(("Brand Type", "VM"), ("MC Code", 1002.0), ("MRP Value", 399.0), ("Buyer Email", "b2@x.com; b3@x.com"), ("Buyer Name", "Bee Two; Bee Three"),
                             ("Category Head Mail", "c@x.com"), ("Cat Head Name", "Cee"), ("Division Head", "d@x.com"), ("Level 2", "")),
            };
            var a = p.Accounts.AnalyseScr(rows);
            Assert.Equal("Brand Type", a.BrandCol);
            Assert.Equal("MC Code", a.McCol);
            Assert.Equal("MRP Value", a.MrpCol);
            var cols = a.Columns.ToDictionary(c => c.Column);
            Assert.Equal("buyer", cols["Buyer Email"].Guess);
            Assert.Equal("Buyer Name", cols["Buyer Email"].NameColumn);
            Assert.Equal(3, cols["Buyer Email"].DistinctEmails);
            Assert.Equal("cathead", cols["Category Head Mail"].Guess);
            Assert.Equal("Cat Head Name", cols["Category Head Mail"].NameColumn);
            Assert.Equal("skip", cols["Division Head"].Guess);
            Assert.Equal("buyer", cols["Level 2"].Guess);
            Assert.False(cols.ContainsKey("Buyer Name"));

            var people = p.Accounts.ScrPeople(rows, new ScrMapping());
            Assert.Equal(new[] { "b1@x.com", "c@x.com", "l2@x.com", "b2@x.com", "b3@x.com" }, people.Keys);
            Assert.Equal("Bee Three", people["b3@x.com"].Name);
            Assert.Equal(new[] { "vm|1001|299" }, people["b1@x.com"].Keys);
            Assert.Equal(new[] { "vm|1001|299", "vm|1002|399" }, people["c@x.com"].CatheadKeys.OrderBy(x => x));
            Assert.DoesNotContain("d@x.com", people.Keys);

            // the admin's mapping wins over the guesses
            var mapped = p.Accounts.ScrPeople(rows, new ScrMapping { Columns = new() { ["Division Head"] = "cathead", ["Level 2"] = "skip" } });
            Assert.Contains("cathead", mapped["d@x.com"].Roles);
            Assert.DoesNotContain("l2@x.com", mapped.Keys);
        }

        [Fact]
        public async Task Upload_syncs_users_and_scope_index_is_cached_per_version()
        {
            var p = new Platform();
            await p.UploadScrAsync(("VM", 1001.0, 299.0, "b1@x.com", "c@x.com"), ("VM", "1002", "399", "b2@x.com", "c@x.com"));
            var users = await p.Accounts.GetUsersAsync();
            Assert.Equal(4, users.Count);   // admin + 3
            var b1 = users.Single(u => u.Email == "b1@x.com");
            Assert.Equal(new[] { "buyer" }, b1.Roles);
            Assert.Equal("scr", b1.Source);
            Assert.True(b1.MustChange);
            Assert.Equal("B", b1.Name);   // name_from_email("b1@x.com") → "B"
            Assert.Equal("Cat Head", users.Single(u => u.Email == "c@x.com").Name);

            var idx = await p.Accounts.ScopeIndexAsync();
            Assert.Equal(new[] { "c@x.com" }, idx.Approvers["vm|1002|399"]);
            Assert.Same(idx, await p.Accounts.ScopeIndexAsync());
            Assert.Equal(1, p.Cache.Invalidations);

            // a new version → new index; existing users only gain roles
            p.Clock.Now = p.Clock.Now.AddSeconds(2);
            await p.UploadScrAsync(("VM", 1001.0, 299.0, "c@x.com", "b1@x.com"));
            var idx2 = await p.Accounts.ScopeIndexAsync();
            Assert.NotSame(idx, idx2);
            Assert.Equal(new[] { "b1@x.com" }, idx2.Approvers["vm|1001|299"]);
            Assert.Equal(new[] { "cathead", "buyer" }, (await p.Accounts.GetUserAsync("b1@x.com"))!.Roles);
            Assert.Contains(p.Store.Audit, l => l.Action == "scr-sync" && l.Detail == "added=0 updated=2");
        }

        [Theory]
        [InlineData("sukhdev.singh@x.com", "Sukhdev Singh")]
        [InlineData("RAVI_kumar2@x.com", "Ravi Kumar")]
        [InlineData("123@x.com", "123@x.com")]
        public void Name_from_email(string email, string name) => Assert.Equal(name, AccountsService.NameFromEmail(email));
    }

    public class AccountsTests
    {
        [Fact]
        public async Task Seeds_admin_locks_after_failed_logins_and_validates_passwords()
        {
            var p = new Platform();
            var (tok, err) = await p.Accounts.LoginAsync("Admin.User@x.com ", "1234", "1.2.3.4");
            Assert.Null(err);
            Assert.NotNull(await p.Accounts.SessionUserAsync(tok));
            Assert.Equal("Password must be at least 8 characters", await p.Accounts.ChangePasswordAsync("admin.user@x.com", "1234", "abc"));
            Assert.Equal("Current password is incorrect", await p.Accounts.ChangePasswordAsync("admin.user@x.com", "nope", "abcdefgh1"));
            Assert.Equal("Use at least one letter and one number", await p.Accounts.ChangePasswordAsync("admin.user@x.com", "1234", "abcdefghij"));
            Assert.Equal("Password can't be your email or user name", p.Accounts.ValidateNewPassword("admin1234@x.com", "ADMIN1234"));
            for (var i = 0; i < 4; i++)
                Assert.Equal("Email or password is incorrect", (await p.Accounts.LoginAsync("admin.user@x.com", "bad", "ip")).Error);
            Assert.Equal("Too many wrong attempts — locked for 15 minutes.", (await p.Accounts.LoginAsync("admin.user@x.com", "bad", "ip")).Error);
            Assert.StartsWith("Too many wrong attempts. Try again in 16 minute(s)"  /* int(900 // 60) + 1, as accounts.py */, (await p.Accounts.LoginAsync("admin.user@x.com", "1234", "ip")).Error);
            p.Clock.Now = p.Clock.Now.AddMinutes(16);
            Assert.Null((await p.Accounts.LoginAsync("admin.user@x.com", "1234", "ip")).Error);
        }

        [Fact]
        public async Task Sessions_expire_after_idle_hours()
        {
            var p = new Platform();
            var (tok, _) = await p.Accounts.LoginAsync("admin.user@x.com", "1234", "ip");
            p.Clock.Now = p.Clock.Now.AddHours(11);
            Assert.NotNull(await p.Accounts.SessionUserAsync(tok));
            p.Clock.Now = p.Clock.Now.AddHours(12.5);
            Assert.Null(await p.Accounts.SessionUserAsync(tok));
        }

        [Fact]
        public async Task Update_user_guards()
        {
            var p = new Platform();
            await p.Accounts.AddUserAsync("x@x.com", "", new[] { "buyer" }, "admin.user@x.com");
            await Assert.ThrowsAsync<AppValidationException>(() => p.Accounts.AddUserAsync("x@x.com", "", new[] { "buyer" }, "a"));
            var e = await Assert.ThrowsAsync<AppValidationException>(() => p.Accounts.AddUserAsync("bad", "", new[] { "buyer" }, "a"));
            Assert.Equal("Not a valid email: bad", e.Message);
            e = await Assert.ThrowsAsync<AppValidationException>(() => p.Accounts.UpdateUserAsync("admin.user@x.com", new JsonObject { ["active"] = false }, "admin.user@x.com"));
            Assert.Equal("You can't deactivate yourself", e.Message);
            e = await Assert.ThrowsAsync<AppValidationException>(() => p.Accounts.UpdateUserAsync("admin.user@x.com", new JsonObject { ["active"] = false }, "x@x.com"));
            Assert.Equal("At least one active admin must remain", e.Message);
            var u = await p.Accounts.UpdateUserAsync("x@x.com", new JsonObject { ["roles"] = new JsonArray("buyer", "admin", "nope"), ["name"] = "  " }, "admin.user@x.com");
            Assert.Equal(new[] { "admin", "buyer" }, u.Roles);
            Assert.Equal("X", u.Name);
            Assert.Contains(p.Store.Audit, l => l.Action == "user-update" && l.Detail == "x@x.com {\"roles\": [\"buyer\", \"admin\", \"nope\"], \"name\": \"  \"}");
        }
    }

    public class RequestAndApprovalTests
    {
        private static readonly Domain.Records.AppUserContext Buyer = Platform.User("b1@x.com", "Bee", "buyer");
        private static readonly Domain.Records.AppUserContext CatHead = Platform.User("c@x.com", "Cee", "cathead");
        private static readonly Domain.Records.AppUserContext Other = Platform.User("z@x.com", "Zed", "cathead");

        private static JsonObject NewReq(string decision = "Watch List", string mc = "1001", int qty = 100) => new()
        {
            ["gm"] = "G1", ["colour"] = "RED", ["month"] = "Oct", ["qty"] = qty, ["decision"] = decision, ["colourKey"] = "G1|RED",
            ["info"] = new JsonObject { ["brand"] = "VM", ["mcCd"] = mc, ["mrp"] = 299 }
        };

        private static async Task<Platform> SetupAsync()
        {
            var p = new Platform();
            await p.UploadScrAsync(("VM", "1001", 299.0, "b1@x.com", "c@x.com"));
            return p;
        }

        [Fact]
        public async Task Watch_list_request_gets_pending_approval_with_category_heads()
        {
            var p = await SetupAsync();
            var r = await p.Requests.SaveAsync(NewReq(), 0, false, Buyer);
            Assert.Equal("REQ-20261014-0001", r["reqId"]!.GetValue<string>());
            Assert.Equal("2026-10-14", r["reqDate"]!.GetValue<string>());
            Assert.Equal("pending", r["approval"]!["status"]!.GetValue<string>());
            Assert.Equal("c@x.com", r["approval"]!["approvers"]![0]!.GetValue<string>());
            Assert.Equal("Cat Head", r["approval"]!["approverNames"]![0]!.GetValue<string>());
            Assert.Equal(1, await p.Approvals.PendingCountAsync(CatHead));
            Assert.Equal(0, await p.Approvals.PendingCountAsync(Buyer));
            Assert.Equal(1, await p.Approvals.PendingCountAsync(Platform.Admin));
        }

        [Fact]
        public async Task Scope_and_stale_conflict()
        {
            var p = await SetupAsync();
            var ex = await Assert.ThrowsAsync<ForbiddenException>(() => p.Requests.SaveAsync(NewReq(mc: "9999"), 0, false, Buyer));
            Assert.Equal("This Brand / MC / MRP is not mapped to you in the SCR products file. Ask the administrator.", ex.Message);
            await p.Requests.SaveAsync(NewReq("Allow"), 0, false, Buyer);
            var c = await Assert.ThrowsAsync<ConflictException>(() => p.Requests.SaveAsync(NewReq("Allow"), 0, false, Platform.Admin));
            var payload = Assert.IsType<Application.DTOs.Platform.RequestConflict>(c.Payload);
            Assert.Equal("Bee", payload.ByOthers);
            Assert.Equal(1, payload.Seq);
            var forced = await p.Requests.SaveAsync(NewReq("Allow"), 0, true, Platform.Admin);
            Assert.Equal(2, forced["seq"]!.GetValue<int>());
            Assert.Equal(2, forced["id"]!.GetValue<int>());
        }

        [Fact]
        public async Task Approval_validation_rules()
        {
            var p = await SetupAsync();
            var rid = (await p.Requests.SaveAsync(NewReq(), 0, false, Buyer))["reqId"]!.GetValue<string>();

            async Task<AppException> Fail(string id, JsonObject body, Domain.Records.AppUserContext who) =>
                await Assert.ThrowsAnyAsync<AppException>(() => p.Approvals.DecideAsync(id, body, who));

            Assert.Equal("decision must be approved or rejected", (await Fail(rid, new() { ["decision"] = "maybe" }, CatHead)).Message);
            Assert.Equal("Bad request id", (await Fail("REQ-1", new() { ["decision"] = "approved" }, CatHead)).Message);
            var nf = await Fail("REQ-20261014-0009", new() { ["decision"] = "approved" }, CatHead);
            Assert.Equal((404, "Request not found or doesn't need approval"), (nf.Status, nf.Message));
            var own = await Fail(rid, new() { ["decision"] = "approved", ["qty"] = 1 }, Buyer);
            Assert.Equal((403, "You cannot approve or reject your own request"), (own.Status, own.Message));
            var other = await Fail(rid, new() { ["decision"] = "approved", ["qty"] = 1 }, Other);
            Assert.Equal((403, "This request is waiting for c@x.com"), (other.Status, other.Message));
            Assert.Equal("Enter the quantity to approve", (await Fail(rid, new() { ["decision"] = "approved", ["qty"] = " " }, CatHead)).Message);
            Assert.Equal("Approved qty must be between 0 and the requested 100", (await Fail(rid, new() { ["decision"] = "approved", ["qty"] = 101 }, CatHead)).Message);
            Assert.Equal("Approved qty must be between 0 and the requested 100", (await Fail(rid, new() { ["decision"] = "approved", ["qty"] = -1 }, CatHead)).Message);
            Assert.Equal("Please add a reason when rejecting", (await Fail(rid, new() { ["decision"] = "rejected", ["comment"] = "  " }, CatHead)).Message);

            var ok = await p.Approvals.DecideAsync(rid, new() { ["decision"] = "approved", ["qty"] = "60", ["comment"] = "fine" }, CatHead);
            Assert.Equal(2, ok.Seq);
            var again = await Fail(rid, new() { ["decision"] = "rejected", ["comment"] = "x" }, Platform.Admin);
            Assert.Equal((409, "Already approved by Cee"), (again.Status, again.Message));

            var (reqs, seq) = await p.Requests.GetDayAsync("2026-10-14");
            Assert.Equal(2, seq);
            Assert.Equal(60, reqs[0]["finalApprovedQty"]!.GetValue<long>());
            var lists = await p.Approvals.GetAsync(90, CatHead);
            Assert.Empty(lists.Pending);
            Assert.Single(lists.Decided);
            Assert.Contains(p.Store.Audit, l => l.Action == "approval" && l.Detail == $"{rid} approved qty=60 fine");
        }

        [Fact]
        public async Task Visibility_of_requests_for_buyers()
        {
            var p = await SetupAsync();
            await p.Requests.SaveAsync(NewReq("Allow", mc: "5555"), 0, false, Platform.Admin);   // admin: any MC
            await p.Requests.SaveAsync(NewReq("Allow"), 1, false, Platform.Admin);
            var mine = await p.Requests.GetDayForUserAsync(null, Buyer);
            Assert.Single(mine.Requests);   // only the one on the buyer's Brand × MC × MRP
            Assert.Equal(2, (await p.Requests.GetDayForUserAsync(null, Platform.Admin)).Requests.Count);
            await Assert.ThrowsAsync<AppValidationException>(() => p.Requests.GetDayForUserAsync("14-10-2026", Buyer));
        }

        [Fact]
        public async Task Clear_day_marks_requests_in_history()
        {
            var p = await SetupAsync();
            await p.Requests.SaveAsync(NewReq(), 0, false, Buyer);
            await Assert.ThrowsAsync<ForbiddenException>(() => p.Requests.ClearDayAsync(null, Buyer));
            Assert.Equal(2, (await p.Requests.ClearDayAsync(null, Platform.Admin)).Seq);
            Assert.Empty((await p.Requests.GetDayAsync("2026-10-14")).Requests);
            var hist = new HistoryService(p.Store, p.Accounts, Microsoft.Extensions.Options.Options.Create(p.Cfg));
            var items = (await hist.RequestsAsync(null, null, Buyer)).Items;
            Assert.Single(items);
            Assert.Equal("cleared", items[0]["approval"]!["status"]!.GetValue<string>());
        }
    }

    public class DataServiceTests
    {
        [Fact]
        public async Task Save_restore_and_clear_versions()
        {
            var p = new Platform();
            var payload = new DecisionEngine.Model.DatasetPayload();
            payload.Rows.Add(Platform.Row(("GM", "G1"), ("Qty", 5.0)));
            var r1 = await p.Data.SaveVersionAsync("f1", payload, "a.xlsx", null, Platform.Admin);
            p.Clock.Now = p.Clock.Now.AddSeconds(5);
            var r2 = await p.Data.SaveVersionAsync("f1", payload, "b.xlsx", "UPL-20261014-0042", Platform.Admin);
            Assert.Equal(2, r2.DataVersion);
            Assert.Equal("UPL-20261014-0001", r1.Entry.UploadId);
            Assert.Equal("UPL-20261014-0042", r2.Entry.UploadId);
            Assert.Equal(1, r2.Entry.Rows);

            var restored = await p.Data.RestoreVersionAsync("f1", r1.Entry.Version, Platform.Admin);
            Assert.Equal(r1.Entry.Version, restored.Entry!.Version);
            Assert.Equal("Admin User", restored.Entry.RestoredBy);
            var m = await p.Data.GetManifestAsync();
            Assert.Equal(3, m.DataVersion);
            Assert.Equal(r1.Entry.Version, m.Datasets["f1"].Version);
            var batches = p.Store.Logs[LogStreams.Batches];
            Assert.Equal("Made current again from UPL-20261014-0001", batches[^1]["note"]!.GetValue<string>());

            var e = await Assert.ThrowsAsync<NotFoundException>(() => p.Data.RestoreVersionAsync("f1", "nope", Platform.Admin));
            Assert.Equal("That version is no longer stored", e.Message);
            await Assert.ThrowsAsync<ForbiddenException>(() => p.Data.ClearAsync("f1", null, Platform.User("b@x.com", "B", "buyer")));
            Assert.Equal(4, (await p.Data.ClearAsync("f1", null, Platform.Admin)).DataVersion);
            Assert.Empty((await p.Data.GetManifestAsync()).Datasets);
            await Assert.ThrowsAsync<NotFoundException>(() => p.Data.GetPayloadGzipAsync("f1", null));
            Assert.NotNull(await p.Data.GetPayloadGzipAsync("f1", r2.Entry.Version));
            Assert.Equal(4, p.Cache.Invalidations);
        }

        [Fact]
        public void Put_body_sanity_check()
        {
            Assert.NotEmpty(DataService.PreparePutBody(System.Text.Encoding.UTF8.GetBytes(" {\n \"rows\" : [ ]}"), false));
            var ex = Assert.Throws<AppValidationException>(() => DataService.PreparePutBody(System.Text.Encoding.UTF8.GetBytes("[1]"), false));
            Assert.Equal("Body must be {\"rows\": [...]}", ex.Message);
            Assert.Equal("Body is not valid gzip", Assert.Throws<AppValidationException>(() => DataService.PreparePutBody(new byte[] { 1, 2, 3 }, true)).Message);
        }

        [Fact]
        public async Task Migrate_ids_backfills_upload_and_run_numbers()
        {
            var p = new Platform();
            p.Store.Logs[LogStreams.Uploads] = new()
            {
                new() { ["key"] = "f1", ["version"] = "v1", ["action"] = "upload", ["updatedBy"] = "P", ["updatedAt"] = "2026-01-05T10:00:00+05:30" },
                new() { ["key"] = "f2", ["version"] = "v2", ["action"] = "upload", ["updatedBy"] = "P", ["updatedAt"] = "2026-01-05T10:01:00+05:30" },
                new() { ["key"] = "f3", ["version"] = "v3", ["action"] = "upload", ["updatedBy"] = "P", ["updatedAt"] = "2026-01-05T10:09:00+05:30" },
            };
            p.Store.Logs[LogStreams.Runs] = new()
            {
                new() { ["id"] = "20260105-100000-abcd" },
                new() { ["id"] = "20260105-110000-abce", ["runNo"] = "RUN-20260105-0007" },
                new() { ["id"] = "20260105-120000-abcf" },
                new() { ["id"] = "20260105-120000-abcf", ["deleted"] = true },
            };
            await p.Store.PutDocAsync(DocNames.Manifest, JsonNode.Parse("{\"dataVersion\":3,\"paramsVersion\":0,\"datasets\":{\"f1\":{\"version\":\"v1\",\"rows\":1,\"bytes\":1}}}")!);
            var m = new MaintenanceService(p.Store, p.Clock, Microsoft.Extensions.Options.Options.Create(p.Cfg));
            await m.MigrateIdsAsync();
            var ups = p.Store.Logs[LogStreams.Uploads];
            Assert.Equal(new[] { "UPL-20260105-9001", "UPL-20260105-9001", "UPL-20260105-9002" }, ups.Select(u => u["uploadId"]!.GetValue<string>()));
            Assert.Equal(2, p.Store.Logs[LogStreams.Batches].Count);
            Assert.Equal("UPL-20260105-9001", (await p.Data.GetManifestAsync()).Datasets["f1"].UploadId);
            var runs = p.Store.Logs[LogStreams.Runs];
            Assert.Equal("RUN-20260105-0001", runs[0]["runNo"]!.GetValue<string>());
            Assert.Equal("RUN-20260105-0008", runs[2]["runNo"]!.GetValue<string>());
            Assert.Null(runs[3]["runNo"]);
        }
    }
}
