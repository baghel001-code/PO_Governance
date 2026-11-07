using System.Data;
using System.Text.Json;
using Application.Interfaces.V1.Sap;
using Application.Services.V1.Sap;
using Dapper;
using DecisionEngine.Mra;
using DecisionEngine.Results.Mra;
using Domain.Entities.Sap;
using Domain.Exceptions;
using Infrastructure.Utilitys;

namespace Infrastructure.Repositories
{
    /// <summary>
    /// <see cref="IMraRepository"/> on SQL Server: PRO_SAP_MRA (database/sqlserver/05_sap_interface.sql, schema v6). The morning
    /// file is passed as one JSON array (@Rows), so a whole file is one call and one transaction.
    /// </summary>
    public sealed class MraSqlServerRepository : IMraRepository
    {
        public const string Sp = "PRO_SAP_MRA";
        private readonly DapperHelper _db;

        public MraSqlServerRepository(DapperHelper db) => _db = db;

        public bool Available => true;
        public string? UnavailableReason => null;

        private static DynamicParameters P(string cmd)
        {
            var p = DapperHelper.AddErrorOutputs(new DynamicParameters());
            p.Add("@Cmd", cmd, DbType.String);
            return p;
        }

        private static void Check(DynamicParameters p)
        {
            var code = DapperHelper.ErrorCode(p);
            if (code != 0) throw new SapStoreException(Sp, code, DapperHelper.ErrorMessage(p));
        }

        /// <summary>The extra fields of a file record (everything but the columns).</summary>
        private sealed class Extra
        {
            public string? CreatedBy { get; set; }
            public string? CreatedById { get; set; }
            public string? CreatedOn { get; set; }
            public string? Inspiration { get; set; }
            public string? GaDesc { get; set; }
            public int Lines { get; set; }
            public MraDecisionInfo? DecisionInfo { get; set; }
        }

        public static string RowsJson(IReadOnlyDictionary<string, MraFileRecord> mras) => JsonSerializer.Serialize(mras.Select(kv => new
        {
            MraNo = kv.Key, kv.Value.Ga, kv.Value.Colour, kv.Value.Qty, kv.Value.Dd, kv.Value.Level,
            PosJson = kv.Value.Pos.Count == 0 ? null : MraStoreJson.Serialize(kv.Value.Pos),
            ExtraJson = MraStoreJson.Serialize(new Extra
            {
                CreatedBy = kv.Value.CreatedBy, CreatedById = kv.Value.CreatedById, CreatedOn = kv.Value.CreatedOn, Inspiration = kv.Value.Inspiration,
                GaDesc = kv.Value.GaDesc, Lines = kv.Value.Lines, DecisionInfo = kv.Value.DecisionInfo
            })
        }));

        public async Task<int> UploadAsync(IReadOnlyDictionary<string, MraFileRecord> mras, string uploadId, string uploadDay, string? fileName, string by,
                                           string countsJson, CancellationToken ct = default)
        {
            var p = P("UPLOAD");
            p.Add("@Rows", RowsJson(mras), DbType.String, size: -1);
            p.Add("@Upload_Id", uploadId, DbType.String, size: 40);
            p.Add("@Upload_Day", DateTime.Parse(uploadDay, System.Globalization.CultureInfo.InvariantCulture), DbType.Date);
            p.Add("@File_Name", fileName is { Length: > 260 } f ? f[..260] : fileName, DbType.String, size: 260);
            p.Add("@Counts_Json", countsJson, DbType.String, size: -1);
            p.Add("@By", by, DbType.String, size: 200);
            p.Add("@Moved_Count", dbType: DbType.Int32, direction: ParameterDirection.Output);
            await _db.ExecuteStoredProcedureAsync(Sp, p, commandTimeout: 600, ct: ct);
            Check(p);
            return p.Get<int?>("@Moved_Count") ?? 0;
        }

        private sealed class ControlRow
        {
            public DateTime? DataDay { get; set; }
            public bool IsDailyDataUpload { get; set; }
            public string? DataVersion { get; set; }
        }

        private sealed class UploadRow
        {
            public string UploadId { get; set; } = "";
            public DateTime UploadDay { get; set; }
            public DateTimeOffset UploadedAt { get; set; }
            public string? UploadedBy { get; set; }
            public string? FileName { get; set; }
            public int MraCount { get; set; }
            public string? CountsJson { get; set; }
            public int? MovedFromPending { get; set; }
        }

        private sealed class FileRow
        {
            public string MraNo { get; set; } = "";
            public string Ga { get; set; } = "";
            public string Colour { get; set; } = "";
            public long Qty { get; set; }
            public DateTime Dd { get; set; }
            public string Level { get; set; } = "";
            public string? PosJson { get; set; }
            public string? ExtraJson { get; set; }
        }

        private sealed class FeedRow
        {
            public long Seq { get; set; }
            public DateTimeOffset ReceivedAt { get; set; }
            public string Kind { get; set; } = "";
            public string? ByUser { get; set; }
            public string? SourceSystem { get; set; }
            public string RawJson { get; set; } = "";
            public string? MsgJson { get; set; }
            public string? Error { get; set; }
            public long? PendingSeq { get; set; }
        }

        private static MraFeedEvent Event(FeedRow r)
        {
            var ev = new MraFeedEvent { Seq = r.Seq, At = r.ReceivedAt, Kind = r.Kind, By = r.ByUser, Raw = r.RawJson, Error = r.Error };
            if (r.Error == null)
            {
                if (r.Kind == "mra") ev.Mra = MraStoreJson.Deserialize<MraMessage>(r.MsgJson);
                else ev.Po = MraStoreJson.Deserialize<PoMessage>(r.MsgJson);
            }
            return ev;
        }

        public async Task<MraStoredState> StateAsync(string today, CancellationToken ct = default)
        {
            var p = P("STATE");
            p.Add("@Today", DateTime.Parse(today, System.Globalization.CultureInfo.InvariantCulture), DbType.Date);
            var (ctl, ups, files, feed, pending) = await _db.ExecuteStoredProcedureMultiple5ListAsync<ControlRow, UploadRow, FileRow, FeedRow, FeedRow>(Sp, p, ct);
            Check(p);
            var st = new MraStoredState();
            if (ctl.FirstOrDefault() is { } c)
                st.Control = new MraControl { DataDay = c.DataDay, IsDailyDataUpload = c.IsDailyDataUpload, DataVersion = c.DataVersion };
            st.Pending = pending.Select(Event).ToList();
            if (ups.FirstOrDefault() is { } u)
                st.Snapshot = new MraSnapshotInfo
                {
                    UploadId = u.UploadId, UploadDay = u.UploadDay.ToString("yyyy-MM-dd"), UploadedAt = u.UploadedAt, By = u.UploadedBy,
                    FileName = u.FileName, MraCount = u.MraCount, MovedFromPending = u.MovedFromPending,
                    Counts = MraStoreJson.Deserialize<MraUploadResponse>(u.CountsJson)
                };
            foreach (var f in files)
            {
                var x = MraStoreJson.Deserialize<Extra>(f.ExtraJson) ?? new Extra();
                st.File[f.MraNo] = new MraFileRecord
                {
                    Ga = f.Ga, Colour = f.Colour, Qty = f.Qty, Dd = f.Dd.ToString("yyyy-MM-dd"), Level = f.Level,
                    Pos = MraStoreJson.Deserialize<List<MraFilePo>>(f.PosJson) ?? new List<MraFilePo>(),
                    CreatedBy = x.CreatedBy, CreatedById = x.CreatedById, CreatedOn = x.CreatedOn, Inspiration = x.Inspiration, GaDesc = x.GaDesc,
                    Lines = x.Lines, DecisionInfo = x.DecisionInfo
                };
            }
            st.Feed = feed.Select(Event).ToList();
            return st;
        }

        private sealed class StampRow
        {
            public string? DataVersion { get; set; }
            public bool IsDailyDataUpload { get; set; }
            public long MaxSeq { get; set; }
            public long MaxPendingSeq { get; set; }
        }

        public async Task<string> StampAsync(string today, CancellationToken ct = default)
        {
            var p = P("STAMP");
            p.Add("@Today", DateTime.Parse(today, System.Globalization.CultureInfo.InvariantCulture), DbType.Date);
            var rows = (await _db.ExecuteStoredProcedureListAsync<StampRow>(Sp, p, commandTimeout: null, ct: ct)).AsList();
            Check(p);
            var x = rows.FirstOrDefault();
            return $"{x?.DataVersion ?? "-"}/{(x?.IsDailyDataUpload == true ? 1 : 0)}/{x?.MaxSeq ?? 0}/{x?.MaxPendingSeq ?? 0}";
        }

        private sealed class SeqRow { public int Idx { get; set; } public long Seq { get; set; } public bool Pending { get; set; } }

        public async Task<List<MraFeedStored>> AddFeedAsync(IReadOnlyList<MraFeedRow> rows, string by, string? sourceSystem, string? traceId, DateTimeOffset receivedAt,
                                                   CancellationToken ct = default)
        {
            var p = P("FEED_ADD");
            p.Add("@Rows", JsonSerializer.Serialize(rows.Select(r => new { r.Kind, r.RawJson, r.MsgJson, Error = r.Error is { Length: > 500 } e ? e[..500] : r.Error })),
                  DbType.String, size: -1);
            p.Add("@By", by is { Length: > 200 } b ? b[..200] : by, DbType.String, size: 200);
            p.Add("@Source_System", sourceSystem is { Length: > 10 } s ? s[..10] : sourceSystem, DbType.String, size: 10);
            p.Add("@Trace_Id", traceId, DbType.String, size: 20);
            p.Add("@Received_At", receivedAt, DbType.DateTimeOffset);
            p.Add("@Today", receivedAt.Date, DbType.Date);
            var res = (await _db.ExecuteStoredProcedureListAsync<SeqRow>(Sp, p, commandTimeout: 300, ct: ct)).AsList();
            Check(p);
            return res.OrderBy(r => r.Idx).Select(r => new MraFeedStored(r.Seq, r.Pending)).ToList();
        }

        private sealed class DecisionRow
        {
            public long ItemId { get; set; }
            public string? Gm { get; set; }
            public string? Colour { get; set; }
            public string? DeliveryMonth { get; set; }
            public double OrderQty { get; set; }
            public double GroupQty { get; set; }
            public string? Verdict { get; set; }
            public string? EngineDecision { get; set; }
            public string? ReasonText { get; set; }
            public double? MaxPermissible { get; set; }
            public bool HasWorking { get; set; }
            public string DecisionId { get; set; } = "";
            public string? RequestId { get; set; }
            public string? PoNumber { get; set; }
            public DateTimeOffset DecidedAt { get; set; }
            public string? Mode { get; set; }
            public string? ViewUrl { get; set; }
            public string? DocumentNo { get; set; }
        }

        public async Task<List<MraToolDecision>> DecisionsAsync(DateTime since, CancellationToken ct = default)
        {
            var p = P("DECISIONS");
            p.Add("@Since", since.Date, DbType.Date);
            var rows = (await _db.ExecuteStoredProcedureListAsync<DecisionRow>(Sp, p, commandTimeout: 120, ct: ct)).AsList();
            Check(p);
            return rows.Select(r => new MraToolDecision
            {
                ItemId = r.ItemId, Gm = r.Gm ?? "", Colour = r.Colour ?? "", DeliveryMonth = r.DeliveryMonth ?? "", OrderQty = r.OrderQty,
                Verdict = r.Verdict, Decision = r.EngineDecision ?? "", Reason = r.ReasonText, MaxPermissible = r.MaxPermissible, HasWorking = r.HasWorking,
                DecisionId = r.DecisionId, RequestId = r.RequestId, PoNumber = r.PoNumber, DecidedAt = r.DecidedAt, Mode = r.Mode, ViewUrl = r.ViewUrl,
                DocumentNo = r.DocumentNo
            }).ToList();
        }

        private sealed class WorkingRow { public string? WorkingJson { get; set; } }

        public async Task<string?> WorkingAsync(long itemId, CancellationToken ct = default)
        {
            var p = P("WORKING");
            p.Add("@Item_Id", itemId, DbType.Int64);
            var rows = (await _db.ExecuteStoredProcedureListAsync<WorkingRow>(Sp, p, commandTimeout: null, ct: ct)).AsList();
            Check(p);
            return rows.FirstOrDefault()?.WorkingJson;
        }
    }

    /// <summary>PostgreSQL storage: the MRA tables exist only on SQL Server.</summary>
    /// <remarks>(v8 decision lookups below are part of <see cref="MraSqlServerRepository"/>.)</remarks>
    public sealed class MraUnavailableRepository : IMraRepository
    {
        public bool Available => false;
        public string? UnavailableReason => SapUnavailableRepository.Reason;
        private static Exception Fail() => new SapApiException(503, SapErrorCodes.StorageNotSupported, SapUnavailableRepository.Reason);
        public Task<int> UploadAsync(IReadOnlyDictionary<string, MraFileRecord> mras, string uploadId, string uploadDay, string? fileName, string by, string countsJson, CancellationToken ct = default) => throw Fail();
        public Task<MraStoredState> StateAsync(string today, CancellationToken ct = default) => throw Fail();
        public Task<string> StampAsync(string today, CancellationToken ct = default) => throw Fail();
        public Task<List<MraFeedStored>> AddFeedAsync(IReadOnlyList<MraFeedRow> rows, string by, string? sourceSystem, string? traceId, DateTimeOffset receivedAt, CancellationToken ct = default) => throw Fail();
        public Task<List<MraToolDecision>> DecisionsAsync(DateTime since, CancellationToken ct = default) => throw Fail();
        public Task<string?> WorkingAsync(long itemId, CancellationToken ct = default) => throw Fail();
    }
}
