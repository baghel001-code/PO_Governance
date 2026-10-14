using Application.DTOs.Analytics.Grc;

namespace Application.Interfaces.V1.Analytics
{
    /// <summary>Writes the GRC row-detail export as .xlsx (implemented in Infrastructure with ClosedXML).</summary>
    public interface IGrcExcelExporter
    {
        byte[] WriteRowDetail(GrcRowDetailExportDto export);
    }
}
