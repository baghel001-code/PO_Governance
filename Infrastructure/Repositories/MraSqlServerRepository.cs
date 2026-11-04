using System.Data;
using System.Text.Json;
using Application.Interfaces.V1.Sap;
using Application.Services.V1.Sap;
using Dapper;
using Domain.Entities.Sap;
using Domain.Exceptions;
using Infrastructure.Utilitys;

namespace Infrastructure.Repositories
{
    /// <summary>
    /// <see cref="IMraRepository"/> on SQL Server: PRO_SAP_MRA_STATUS (database/sqlserver/05_sap_interface.sql, schema v5) with
    /// the lines passed as one JSON array (@Rows), so a whole file or batch is one call and one transaction.
    /// </summary>
    public sealed class MraSqlServerRepository : IMraRepository
    {
        public const string Sp = "PRO_SAP_MRA_STATUS";
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
            if (code == 50409) throw new AppValidationException(DapperHelper.ErrorMessage(p));
            if (code != 0) throw new SapStoreException(Sp, code, DapperHelper.ErrorMessage(p));
        }

        /// <summary>The lines as the procedure's OPENJSON reads them.</summary>
        public static string RowsJson(IEnumerable<MraLine> lines) => JsonSerializer.Serialize(lines.Select(l => new
        {
            l.MraNo, l.GenericArticle, l.Colour, l.GenericArtDesc, l.VendorCode, l.VendorName, l.BrandType, l.McCode, l.McName, l.Mrp,
            l.PlanningFlag, l.Fit, l.Design, l.Style, l.Fabric, l.ProductMaterial, l.Qty, l.PoValue,
            DeliveryDate = l.DeliveryDate?.ToString("yyyy-MM-dd"), l.Status, l.ApprovalLevel, l.RequestId, l.Remark,
            ChangedAt = l.ChangedAt?.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz")
        }));

        public async Task<(int Rows, int Replaced)> UploadAsync(IReadOnlyList<MraLine> lines, string uploadId, string? fileName, string by, CancellationToken ct = default)
        {
            var p = P("UPLOAD");
            p.Add("@Rows", RowsJson(lines), DbType.String, size: -1);
            p.Add("@Upload_Id", uploadId, DbType.String, size: 40);
            p.Add("@File_Name", fileName is { Length: > 260 } f ? f[..260] : fileName, DbType.String, size: 260);
            p.Add("@By", by, DbType.String, size: 200);
            p.Add("@Row_Count", dbType: DbType.Int32, direction: ParameterDirection.Output);
            p.Add("@Replaced_Count", dbType: DbType.Int32, direction: ParameterDirection.Output);
            await _db.ExecuteStoredProcedureAsync(Sp, p, commandTimeout: 600, ct: ct);
            Check(p);
            return (p.Get<int?>("@Row_Count") ?? 0, p.Get<int?>("@Replaced_Count") ?? 0);
        }

        public async Task<List<MraApplyRow>> ApplyAsync(IReadOnlyList<MraLine> lines, string by, string? sourceSystem, string batchId, string? traceId, CancellationToken ct = default)
        {
            var p = P("APPLY");
            p.Add("@Rows", RowsJson(lines), DbType.String, size: -1);
            p.Add("@By", by, DbType.String, size: 200);
            p.Add("@Source_System", sourceSystem, DbType.String, size: 10);
            p.Add("@Batch_Id", batchId, DbType.String, size: 40);
            p.Add("@Trace_Id", traceId, DbType.String, size: 20);
            var rows = (await _db.ExecuteStoredProcedureListAsync<MraApplyRow>(Sp, p, commandTimeout: null, ct: ct)).AsList();
            Check(p);
            return rows;
        }

        public async Task<MraListResponse> ListAsync(MraListReq req, CancellationToken ct = default)
        {
            var p = P("LIST");
            p.Add("@Status", req.Status, DbType.String, size: 30);
            p.Add("@Search", req.Search, DbType.String, size: 100);
            p.Add("@Page", req.Page, DbType.Int32);
            p.Add("@Page_Size", req.PageSize, DbType.Int32);
            var (items, total) = await _db.ExecuteStoredProcedureMultipleListAsync<MraLine, int>(Sp, p, ct);
            Check(p);
            return new MraListResponse { Items = items.AsList(), Total = total.FirstOrDefault(), Page = req.Page, PageSize = req.PageSize };
        }

        public async Task<MraSummary> SummaryAsync(DateTimeOffset since, CancellationToken ct = default)
        {
            var p = P("SUMMARY");
            p.Add("@Since", since, DbType.DateTimeOffset);
            var (byStatus, totals) = await _db.ExecuteStoredProcedureMultipleListAsync<MraStatusCount, MraSummary>(Sp, p, ct);
            Check(p);
            var s = totals.FirstOrDefault() ?? new MraSummary();
            s.ByStatus = byStatus.AsList();
            return s;
        }

        public async Task<List<MraHistoryRow>> HistoryAsync(string mraNo, CancellationToken ct = default)
        {
            var p = P("HISTORY");
            p.Add("@Mra_No", mraNo, DbType.String, size: 20);
            var rows = (await _db.ExecuteStoredProcedureListAsync<MraHistoryRow>(Sp, p, commandTimeout: null, ct: ct)).AsList();
            Check(p);
            return rows;
        }
    }

    /// <summary>PostgreSQL storage: the MRA status tables exist only on SQL Server.</summary>
    public sealed class MraUnavailableRepository : IMraRepository
    {
        public bool Available => false;
        public string? UnavailableReason => SapUnavailableRepository.Reason;
        private static Exception Fail() => new SapApiException(503, SapErrorCodes.StorageNotSupported, SapUnavailableRepository.Reason);
        public Task<(int Rows, int Replaced)> UploadAsync(IReadOnlyList<MraLine> lines, string uploadId, string? fileName, string by, CancellationToken ct = default) => throw Fail();
        public Task<List<MraApplyRow>> ApplyAsync(IReadOnlyList<MraLine> lines, string by, string? sourceSystem, string batchId, string? traceId, CancellationToken ct = default) => throw Fail();
        public Task<MraListResponse> ListAsync(MraListReq req, CancellationToken ct = default) => throw Fail();
        public Task<MraSummary> SummaryAsync(DateTimeOffset since, CancellationToken ct = default) => throw Fail();
        public Task<List<MraHistoryRow>> HistoryAsync(string mraNo, CancellationToken ct = default) => throw Fail();
    }
}
