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

        public async Task UploadAsync(IReadOnlyDictionary<string, MraFileRecord> mras, string uploadId, string uploadDay, string? fileName, string by,
                                      string countsJson, CancellationToken ct = default)
        {
            var p = P("UPLOAD");
            p.Add("@Rows", RowsJson(mras), DbType.String, size: -1);
            p.Add("@Upload_Id", uploadId, DbType.String, size: 40);
            p.Add("@Upload_Day", DateTime.Parse(uploadDay, System.Globalization.CultureInfo.InvariantCulture), DbType.Date);
            p.Add("@File_Name", fileName is { Length: > 260 } f ? f[..260] : fileName, DbType.String, size: 260);
            p.Add("@Counts_Json", countsJson, DbType.String, size: -1);
            p.Add("@By", by, DbType.String, size: 200);
            await _db.ExecuteStoredProcedureAsync(Sp, p, commandTimeout: 600, ct: ct);
            Check(p);
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
        }

        public async Task<MraStoredState> StateAsync(string today, CancellationToken ct = default)
        {
            var p = P("STATE");
            p.Add("@Today", DateTime.Parse(today, System.Globalization.CultureInfo.InvariantCulture), DbType.Date);
            var (ups, files, feed) = await _db.ExecuteStoredProcedureMultiple3ListAsync<UploadRow, FileRow, FeedRow>(Sp, p, ct);
            Check(p);
            var st = new MraStoredState();
            if (ups.FirstOrDefault() is { } u)
                st.Snapshot = new MraSnapshotInfo
                {
                    UploadId = u.UploadId, UploadDay = u.UploadDay.ToString("yyyy-MM-dd"), UploadedAt = u.UploadedAt, By = u.UploadedBy,
                    FileName = u.FileName, MraCount = u.MraCount, Counts = MraStoreJson.Deserialize<MraUploadResponse>(u.CountsJson)
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
            foreach (var r in feed)
            {
                var ev = new MraFeedEvent { Seq = r.Seq, At = r.ReceivedAt, Kind = r.Kind, By = r.ByUser, Raw = r.RawJson, Error = r.Error };
                if (r.Error == null)
                {
                    if (r.Kind == "mra") ev.Mra = MraStoreJson.Deserialize<MraMessage>(r.MsgJson);
                    else ev.Po = MraStoreJson.Deserialize<PoMessage>(r.MsgJson);
                }
                st.Feed.Add(ev);
            }
            return st;
        }

        private sealed class StampRow { public string? LastUploadId { get; set; } public long MaxSeq { get; set; } }

        public async Task<string> StampAsync(CancellationToken ct = default)
        {
            var p = P("STAMP");
            var rows = (await _db.ExecuteStoredProcedureListAsync<StampRow>(Sp, p, commandTimeout: null, ct: ct)).AsList();
            Check(p);
            var s = rows.FirstOrDefault();
            return (s?.LastUploadId ?? "-") + "/" + (s?.MaxSeq ?? 0);
        }

        private sealed class SeqRow { public int Idx { get; set; } public long Seq { get; set; } }

        public async Task<List<long>> AddFeedAsync(IReadOnlyList<MraFeedRow> rows, string by, string? sourceSystem, string? traceId, DateTimeOffset receivedAt,
                                                   CancellationToken ct = default)
        {
            var p = P("FEED_ADD");
            p.Add("@Rows", JsonSerializer.Serialize(rows.Select(r => new { r.Kind, r.RawJson, r.MsgJson, Error = r.Error is { Length: > 500 } e ? e[..500] : r.Error })),
                  DbType.String, size: -1);
            p.Add("@By", by is { Length: > 200 } b ? b[..200] : by, DbType.String, size: 200);
            p.Add("@Source_System", sourceSystem is { Length: > 10 } s ? s[..10] : sourceSystem, DbType.String, size: 10);
            p.Add("@Trace_Id", traceId, DbType.String, size: 20);
            p.Add("@Received_At", receivedAt, DbType.DateTimeOffset);
            var res = (await _db.ExecuteStoredProcedureListAsync<SeqRow>(Sp, p, commandTimeout: 300, ct: ct)).AsList();
            Check(p);
            return res.OrderBy(r => r.Idx).Select(r => r.Seq).ToList();
        }
    }

    /// <summary>PostgreSQL storage: the MRA tables exist only on SQL Server.</summary>
    public sealed class MraUnavailableRepository : IMraRepository
    {
        public bool Available => false;
        public string? UnavailableReason => SapUnavailableRepository.Reason;
        private static Exception Fail() => new SapApiException(503, SapErrorCodes.StorageNotSupported, SapUnavailableRepository.Reason);
        public Task UploadAsync(IReadOnlyDictionary<string, MraFileRecord> mras, string uploadId, string uploadDay, string? fileName, string by, string countsJson, CancellationToken ct = default) => throw Fail();
        public Task<MraStoredState> StateAsync(string today, CancellationToken ct = default) => throw Fail();
        public Task<string> StampAsync(CancellationToken ct = default) => throw Fail();
        public Task<List<long>> AddFeedAsync(IReadOnlyList<MraFeedRow> rows, string by, string? sourceSystem, string? traceId, DateTimeOffset receivedAt, CancellationToken ct = default) => throw Fail();
    }
}
