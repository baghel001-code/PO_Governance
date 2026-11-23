using System.Text.Json.Nodes;
using Application.Services.V1.Tool1;
using DecisionEngine.Model;
using DecisionEngine.Tool1;
using Domain.Exceptions;

namespace POGovernance.Tests.Platform
{
    /// <summary>
    /// Multi concept rule Step 3: an option in the 20–30% band may be raised as an exception (MRA Level 8) with the buyer's
    /// reason; it waits for the B&amp;M Head (PoGovSettings:BmHeadEmail) — only she or an admin can approve or reject it.
    /// </summary>
    public class ExceptionApprovalTests
    {
        private static readonly Domain.Records.AppUserContext Buyer = Platform.User("b1@x.com", "Bee", "buyer");
        private static readonly Domain.Records.AppUserContext CatHead = Platform.User("c@x.com", "Cee", "cathead");
        private static readonly Domain.Records.AppUserContext Planner = Platform.User("p@x.com", "Pia", "planner");
        private static readonly Domain.Records.AppUserContext Anne = Platform.User("anne@x.com", "Anne", "cathead");

        private static JsonObject NewReq(string decision = Decisions.Exception) => new()
        {
            ["gm"] = "G1", ["colour"] = "RED", ["month"] = "Oct", ["qty"] = 100, ["decision"] = decision, ["colourKey"] = "G1|RED",
            ["colourReason"] = "Festive build-up in the north stores",
            ["info"] = new JsonObject { ["brand"] = "VM", ["mcCd"] = "1001", ["mrp"] = 299 }
        };

        private static async Task<Platform> SetupAsync(string bmHead = "Anne@x.com")
        {
            var p = new Platform();
            p.Cfg.BmHeadEmail = bmHead;
            await p.UploadScrAsync(("VM", "1001", 299.0, "b1@x.com", "c@x.com"));
            await p.Accounts.AddUserAsync("anne@x.com", "Anne", new[] { "cathead" }, "admin.user@x.com");
            return p;
        }

        [Fact]
        public async Task An_exception_waits_for_the_BM_head()
        {
            var p = await SetupAsync();
            var r = await p.Requests.SaveAsync(NewReq(), 0, false, Buyer);
            var a = r["approval"]!.AsObject();
            Assert.Equal("pending", a["status"]!.GetValue<string>());
            Assert.Equal("anne@x.com", Assert.Single(a["approvers"]!.AsArray())!.GetValue<string>());   // the setting, lower-cased
            Assert.Equal("Anne", a["approverNames"]![0]!.GetValue<string>());                            // her portal name
            Assert.Equal("exception", a["kind"]!.GetValue<string>());

            // only the B&M Head and the admins have it in their queue — not the category head, not a planner
            Assert.Equal(1, await p.Approvals.PendingCountAsync(Anne));
            Assert.Equal(1, await p.Approvals.PendingCountAsync(Platform.Admin));
            Assert.Equal(0, await p.Approvals.PendingCountAsync(CatHead));
            Assert.Equal(0, await p.Approvals.PendingCountAsync(Planner));
            Assert.Single((await p.Approvals.GetAsync(7, Anne)).Pending);
            Assert.Empty((await p.Approvals.GetAsync(7, Planner)).Pending);
        }

        [Fact]
        public async Task Only_the_BM_head_or_an_admin_decides_an_exception()
        {
            var p = await SetupAsync();
            var rid = (await p.Requests.SaveAsync(NewReq(), 0, false, Buyer))["reqId"]!.GetValue<string>();
            foreach (var who in new[] { CatHead, Planner })
            {
                var ex = await Assert.ThrowsAsync<ForbiddenException>(() => p.Approvals.DecideAsync(rid, new() { ["decision"] = "approved", ["qty"] = 50 }, who));
                Assert.Equal("This exception is waiting for the B&M Head (anne@x.com)", ex.Message);
            }
            var ok = await p.Approvals.DecideAsync(rid, new() { ["decision"] = "approved", ["qty"] = 80, ["comment"] = "ok for the festive season" }, Anne);
            Assert.True(ok.Seq > 0);
        }

        [Fact]
        public async Task Without_a_BM_head_set_an_admin_decides()
        {
            var p = await SetupAsync(bmHead: "");
            var r = await p.Requests.SaveAsync(NewReq(), 0, false, Buyer);
            Assert.Empty(r["approval"]!["approvers"]!.AsArray());
            var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
                p.Approvals.DecideAsync(r["reqId"]!.GetValue<string>(), new() { ["decision"] = "rejected", ["comment"] = "no" }, Planner));
            Assert.Equal("This exception is waiting for the B&M Head (an admin)", ex.Message);
            await p.Approvals.DecideAsync(r["reqId"]!.GetValue<string>(), new() { ["decision"] = "rejected", ["comment"] = "not this season" }, Platform.Admin);
        }

        [Fact]
        public async Task A_watch_list_still_goes_to_the_category_head_and_planners()
        {
            var p = await SetupAsync();
            var r = await p.Requests.SaveAsync(NewReq(Decisions.WatchList), 0, false, Buyer);
            Assert.Equal("c@x.com", r["approval"]!["approvers"]![0]!.GetValue<string>());
            Assert.Null(r["approval"]!["kind"]);
            Assert.Equal(1, await p.Approvals.PendingCountAsync(Planner));
            Assert.Equal(0, await p.Approvals.PendingCountAsync(Anne));
        }

        [Fact]
        public void The_buyer_must_give_a_reason_for_an_exception()
        {
            Assert.True(Tool1Service.NeedsColourReason(new Tool1Outcome { Engine = "A", Decision = Decisions.Exception }));
            Assert.True(Tool1Service.NeedsColourReason(new Tool1Outcome { Engine = "B", Decision = Decisions.WatchList }));
            Assert.False(Tool1Service.NeedsColourReason(new Tool1Outcome { Engine = "A", Decision = Decisions.Block }));
        }
    }
}
