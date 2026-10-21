using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Application.Interfaces.V1;
using Application.Interfaces.V1.Sap;
using DecisionEngine.Model;
using Domain.Entities.AppSettings;
using Domain.Entities.Sap;
using Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace Application.Services.V1.Sap
{
    /// <summary>GET /ping and GET /reason-codes.</summary>
    public sealed class SapUtilityService : ISapUtilityService
    {
        private readonly ISapRepository _repo;
        private readonly IAppClock _clock;
        private readonly SapInterfaceSettings _cfg;

        public SapUtilityService(ISapRepository repo, IAppClock clock, IOptions<SapInterfaceSettings> cfg)
        {
            _repo = repo; _clock = clock; _cfg = cfg.Value;
        }

        public Task<SapPingResponse> PingAsync(SapCallContext caller, CancellationToken ct = default) => Task.FromResult(new SapPingResponse
        {
            Status = "ok",
            ServerTime = SapJson.Timestamp(_clock.Now),
            ApiVersion = _cfg.ApiVersion,
            Environment = SapEnums.Environments.Contains((_cfg.Environment ?? "").ToUpperInvariant()) ? _cfg.Environment!.ToUpperInvariant() : "TEST",
            SourceSystemRecognised = caller.SourceSystem
        });

        public async Task<SapReasonCodesResponse> ReasonCodesAsync(CancellationToken ct = default)
        {
            var (codes, version) = await _repo.ActiveExceptionCodesAsync((int)_clock.Now.Offset.TotalMinutes, ct);
            return new SapReasonCodesResponse
            {
                Version = string.IsNullOrEmpty(version) ? _clock.Today : version,
                ReasonCodes = codes.Select(c => new SapReasonCode { Code = c.Code, Text = c.Text, Active = c.Active }).ToList()
            };
        }
    }

    /// <summary>
    /// The portal's SAP monitoring screen (/admin/sap, admin only): recent decisions with their items and JSON, events with
    /// lag and status, technical users (add, reset password, enable / disable, unlock), reason codes, and a "Test decision"
    /// that runs a request through the real decision path (logged with Is_Test = 1, request_id prefixed TEST-).
    /// </summary>
    public sealed class SapAdminService : ISapAdminService
    {
        private static readonly Regex UserNameRx = new(@"^[A-Za-z0-9_.\-]{3,64}$", RegexOptions.Compiled);
        private static readonly Regex SystemRx = new(@"^[A-Za-z0-9]{2,10}$", RegexOptions.Compiled);
        private static readonly Regex ReasonCodeRx = new(@"^[A-Z0-9]{1,4}$", RegexOptions.Compiled);
        private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

        private readonly ISapRepository _repo;
        private readonly ISapDecisionService _decisions;
        private readonly ISapEventService _events;
        private readonly IPasswordHasher _hasher;
        private readonly IAuditService _audit;
        private readonly IAppClock _clock;
        private readonly IDatasetCache _cache;
        private readonly IStore _store;
        private readonly SapInterfaceSettings _cfg;

        public SapAdminService(ISapRepository repo, ISapDecisionService decisions, ISapEventService events, IPasswordHasher hasher, IAuditService audit,
                               IAppClock clock, IDatasetCache cache, IStore store, IOptions<SapInterfaceSettings> cfg)
        {
            _repo = repo; _decisions = decisions; _events = events; _hasher = hasher; _audit = audit; _clock = clock; _cache = cache; _store = store;
            _cfg = cfg.Value;
        }

        private int Tz => (int)_clock.Now.Offset.TotalMinutes;

        private void RequireStore()
        {
            if (!_repo.Available) throw new ServiceUnavailableException(_repo.UnavailableReason ?? "SAP interface requires SQL Server storage");
        }

        public async Task<SapOverviewResponse> OverviewAsync(CancellationToken ct = default)
        {
            var o = new SapOverviewResponse
            {
                Enabled = _cfg.Enabled, Available = _repo.Available, UnavailableReason = _repo.UnavailableReason,
                Environment = _cfg.Environment, BlockVerdict = _cfg.BlockVerdict, UnknownItemPolicy = _cfg.UnknownItemPolicy,
                DecisionTimeoutMs = _cfg.DecisionTimeoutMs, EventsLagLimitSeconds = _cfg.EventsLagLimitSeconds,
                AllowedIps = _cfg.AllowedIps.ToList(), TrustedProxies = _cfg.TrustedProxies.ToList(), OnlyOnPort = _cfg.OnlyOnPort
            };
            var data = await _cache.GetCurrentAsync(ct);
            o.RulesVersion = string.IsNullOrWhiteSpace(_cfg.RulesVersion) ? "R-P" + data.ParamsVersion.ToString(CultureInfo.InvariantCulture) : _cfg.RulesVersion;
            o.BwSnapshot = SapDecisionService.BwSnapshot(await Platform.PlatformCore.GetManifestAsync(_store, ct));
            if (!_repo.Available) return o;
            o.Events = await _repo.EventStatusAsync(_clock.Today, Tz, ct);
            o.Decisions = await _repo.DecisionStatsAsync(_clock.Today, Tz, ct);
            return o;
        }

        public Task<SapDecisionListResponse> ListDecisionsAsync(SapDecisionListReq req, CancellationToken ct = default)
        {
            RequireStore();
            req.Max = Math.Clamp(req.Max <= 0 ? 200 : req.Max, 1, 2000);
            return _repo.ListDecisionsAsync(req, Tz, ct);
        }

        public async Task<SapDecisionDetailResponse> GetDecisionAsync(long id, CancellationToken ct = default)
        {
            RequireStore();
            var d = await _repo.GetDecisionAsync(id, ct);
            if (d.Decision == null) throw new NotFoundException("Decision not found");
            return d;
        }

        public Task<SapEventListResponse> ListEventsAsync(SapEventListReq req, CancellationToken ct = default)
        {
            RequireStore();
            req.Max = Math.Clamp(req.Max <= 0 ? 200 : req.Max, 1, 2000);
            return ListEventsCoreAsync(req, ct);
        }

        private async Task<SapEventListResponse> ListEventsCoreAsync(SapEventListReq req, CancellationToken ct)
        {
            var res = await _repo.ListEventsAsync(req, Tz, ct);
            res.Status = await _repo.EventStatusAsync(_clock.Today, Tz, ct);
            return res;
        }

        public async Task<SapEventRow> GetEventAsync(long id, CancellationToken ct = default)
        {
            RequireStore();
            return await _repo.GetEventAsync(id, ct) ?? throw new NotFoundException("Event not found");
        }

        public async Task<SapTechUsersResponse> ListTechUsersAsync(CancellationToken ct = default)
        {
            RequireStore();
            return new SapTechUsersResponse { Users = await _repo.ListTechUsersAsync(ct), MinPasswordLength = _cfg.MinPasswordLength };
        }

        public async Task<SapTechUserResponse> AddTechUserAsync(SapTechUserAddReq req, string by, CancellationToken ct = default)
        {
            RequireStore();
            var name = (req.UserName ?? "").Trim();
            if (!UserNameRx.IsMatch(name)) throw new AppValidationException("User name: 3-64 letters, digits, '_', '.' or '-' (e.g. sap_ecp)");
            var system = (req.SourceSystem ?? "").Trim().ToUpperInvariant();
            if (!SystemRx.IsMatch(system)) throw new AppValidationException("SAP system: the SAP system ID (SY-SYSID), e.g. ECP or ECQ");
            var (pw, generated) = Password(req.Password);
            SapTechUser u;
            try
            {
                u = await _repo.AddTechUserAsync(name, system, string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(), _hasher.Hash(pw), by, ct);
            }
            catch (SapStoreException ex) when (ex.ErrorCode == 50409)
            {
                throw new ConflictException("A technical user named " + name + " already exists");
            }
            await _audit.AppendAsync(by, "sap-user-add", name + " (" + system + ")", ct);
            return new SapTechUserResponse { User = u, GeneratedPassword = generated ? pw : null };
        }

        public async Task<SapTechUserResponse> UpdateTechUserAsync(SapTechUserUpdateReq req, string by, CancellationToken ct = default)
        {
            RequireStore();
            var name = (req.UserName ?? "").Trim();
            if (name.Length == 0) throw new AppValidationException("User name is required");
            try
            {
                switch ((req.Action ?? "").Trim().ToLowerInvariant())
                {
                    case "enable":
                    case "disable":
                    {
                        var on = string.Equals(req.Action, "enable", StringComparison.OrdinalIgnoreCase);
                        var u = await _repo.SetTechUserActiveAsync(name, on, by, ct);
                        await _audit.AppendAsync(by, "sap-user-update", name + (on ? " enabled" : " disabled"), ct);
                        return new SapTechUserResponse { User = u };
                    }
                    case "unlock":
                    {
                        var u = await _repo.UnlockTechUserAsync(name, ct);
                        await _audit.AppendAsync(by, "sap-user-update", name + " unlocked", ct);
                        return new SapTechUserResponse { User = u };
                    }
                    case "reset-password":
                    {
                        var (pw, generated) = Password(req.Password);
                        var u = await _repo.SetTechUserPasswordAsync(name, _hasher.Hash(pw), by, ct);
                        await _audit.AppendAsync(by, "sap-password-reset", name, ct);
                        return new SapTechUserResponse { User = u, GeneratedPassword = generated ? pw : null };
                    }
                    default:
                        throw new AppValidationException("Action must be enable, disable, unlock or reset-password");
                }
            }
            catch (SapStoreException ex) when (ex.ErrorCode == 50404)
            {
                throw new NotFoundException("Unknown technical user " + name);
            }
        }

        /// <summary>The given password (checked against SapInterface:MinPasswordLength) or a generated 32-character one.</summary>
        private (string Password, bool Generated) Password(string? given)
        {
            if (string.IsNullOrEmpty(given))
                return (new string(RandomNumberGenerator.GetItems<char>(PasswordAlphabet, Math.Max(32, _cfg.MinPasswordLength))), true);
            if (given.Length < _cfg.MinPasswordLength)
                throw new AppValidationException("The password must have at least " + _cfg.MinPasswordLength + " characters (SAP API specification §2)");
            if (given.Contains(':')) throw new AppValidationException("The password must not contain ':' (HTTP Basic authentication)");
            return (given, false);
        }

        public async Task<SapReasonCodesListResponse> ListReasonCodesAsync(CancellationToken ct = default)
        {
            RequireStore();
            return new SapReasonCodesListResponse { Items = await _repo.ListReasonCodesAsync(null, ct) };
        }

        public async Task<SapReasonCodeRow> SaveReasonCodeAsync(SapReasonCodeSaveReq req, string by, CancellationToken ct = default)
        {
            RequireStore();
            req.Code = (req.Code ?? "").Trim().ToUpperInvariant();
            req.Text = (req.Text ?? "").Trim();
            if (!ReasonCodeRx.IsMatch(req.Code)) throw new AppValidationException("Code: 1-4 capital letters or digits (spec ReasonCode.code maxLength 4)");
            if (req.Text.Length == 0 || req.Text.Length > 60) throw new AppValidationException("Text: 1-60 characters (spec ReasonCode.text maxLength 60)");
            var row = await _repo.SaveReasonCodeAsync(req, by, ct);
            await _audit.AppendAsync(by, "sap-reason-code", req.Code + " = " + req.Text + (req.Active ? "" : " (inactive)"), ct);
            return row;
        }

        public async Task<SapTestDecisionResponse> TestDecisionAsync(SapTestDecisionReq req, string by, string clientIp, CancellationToken ct = default)
        {
            RequireStore();
            var body = string.IsNullOrWhiteSpace(req.RequestJson) ? await SampleRequestAsync(ct) : req.RequestJson!;
            // keep test request_ids apart from SAP's: always TEST-…
            try
            {
                if (JsonNode.Parse(body) is JsonObject o && o["request_id"] is JsonValue v && v.TryGetValue<string>(out var rid)
                    && !rid.StartsWith("TEST-", StringComparison.Ordinal))
                {
                    o["request_id"] = "TEST-" + rid;
                    body = o.ToJsonString();
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // invalid JSON: the decision path answers 400 INVALID_JSON
            }
            var res = await _decisions.DecideAsync(body, new SapCallContext("portal:" + by, "PORTAL", clientIp, SapJson.NewTraceId(), IsTest: true), ct);
            return new SapTestDecisionResponse { HttpStatus = res.Status, ResponseJson = res.Json, LogId = res.LogId, DurationMs = res.DurationMs, SampleRequestJson = body };
        }

        public async Task<string> SampleRequestAsync(CancellationToken ct = default)
        {
            var data = await _cache.GetCurrentAsync(ct);
            var row = data.F1?.FirstOrDefault(r => r.Col("Generic Material", "GENERIC MATERIAL").Trim().Length > 0);
            var gm = row?.Col("Generic Material", "GENERIC MATERIAL").Trim() ?? "100234500";
            var colour = row?.Col("Color", "Colour", "COLOR", "COLOUR").Trim() ?? "NAVY";
            var now = _clock.Now;
            var (y, m) = (now.Year, now.Month);
            var delivery = new DateTime(y, m, 1).AddMonths(1).AddDays(9);
            var sample = new SapDecisionRequest
            {
                RequestId = "TEST-" + now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
                Mode = (_cfg.DefaultMode ?? "SHADOW").ToUpperInvariant() == "LIVE" ? "LIVE" : "SHADOW",
                SourceSystem = "ECQ", Client = "300", OtbCheck = "PASSED", DepthCheck = "PASSED",
                RequestedAt = SapJson.Timestamp(now),
                PurchaseOrder = new SapDecisionPurchaseOrder
                {
                    PoNumber = "4500099001", DocType = "NB", Vendor = "0000201345", PurchOrg = "1000", PurchGroup = "B07",
                    BuyerUser = "BUYER07", Division = row?.Col("Division", "DIV") is { Length: > 0 } dv ? dv : "MENS", Currency = "INR",
                    Items = new List<SapDecisionRequestItem>
                    {
                        new()
                        {
                            PoItem = 10, Article = "0000000" + gm + "01", GenericArticle = "00000000" + gm, Colour = colour,
                            MerchCategory = row?.Col("MC CD") ?? "", Season = "AW26", OrderQty = 1200, Uom = "PC", NetValue = 418800,
                            DeliveryDate = delivery.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        }
                    }
                }
            };
            var json = JsonNode.Parse(SapJson.Serialize(sample))!;
            return json.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = SapJson.Options.Encoder });
        }
    }

    /// <summary>A PRO_SAP_* procedure returned a non-zero @Error_Code (50404 not found, 50409 exists …).</summary>
    public sealed class SapStoreException : Exception
    {
        public SapStoreException(string procedure, int code, string message) : base($"{procedure} failed ({code}): {message}")
        {
            Procedure = procedure;
            ErrorCode = code;
        }

        public string Procedure { get; }
        public int ErrorCode { get; }
    }
}
