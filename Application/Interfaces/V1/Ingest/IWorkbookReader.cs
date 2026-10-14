using Application.DTOs.Ingest;

namespace Application.Interfaces.V1.Ingest
{
    /// <summary>
    /// Reads an uploaded spreadsheet (.xlsx / .xlsm / .xlsb / .xls, or delimited text such as .csv)
    /// the way <c>XLSX.read(buf, {type:'array', cellDates:false})</c> did in v1.1.4: the format is
    /// detected from the content, every sheet is returned with its used range and typed cells
    /// (number / text / boolean / error + the number-format code SheetJS would apply).
    /// Throws on unreadable content.
    /// </summary>
    public interface IWorkbookReader
    {
        WorkbookData Read(byte[] content, string fileName);
    }
}
