using Domain.Entities.Sap;

namespace Application.Interfaces.V1.Sap
{
    /// <summary>The PO document report (/admin/sap) as .xlsx (Infrastructure, ClosedXML).</summary>
    public interface ISapReportExcelExporter
    {
        byte[] WriteDocumentReport(IReadOnlyList<SapDocumentReportRow> rows, int tzOffsetMinutes);
    }
}
