namespace Domain.Entities.Common
{
    /// <summary>One row of API_MASTER (QPS ApiData; database/sqlserver/04_api_master.sql).</summary>
    public class ApiData
    {
        public string? Api_Name { get; set; }
        public string? Task_Name { get; set; }
        public string? Api_Url { get; set; }
        public string? Api_Username { get; set; }
        public string? Api_Pwd { get; set; }
        public string? Api_Proxy { get; set; }
        public int Api_Port { get; set; }
        public string? Api_Http_Method { get; set; }
        public string? Api_Content_Type { get; set; }
        public string? Api_Authorization_Type { get; set; }
        /// <summary>The static API token, My_Encode-d (decoded by the portal when it loads the list).</summary>
        public string? Api_Authorization { get; set; }
        /// <summary>Seconds.</summary>
        public int Api_Timeout { get; set; }
        public string? Token_Api_Name { get; set; }
        public DateTime? Api_Token_Expiry_Time { get; set; }
        public string? Api_Token_Status { get; set; }
        public bool Active_Status { get; set; } = true;

        public ApiData Clone() => (ApiData)MemberwiseClone();
    }

    /// <summary>The API master list the portal loaded at start-up (QPS AllApi).</summary>
    public static class AllApi
    {
        public static List<ApiData>? ApiDetails { get; set; }
    }

    /// <summary>appsettings "MasterApiSettings": the GetAllApi row itself (QPS ApiMasterData).</summary>
    public static class ApiMasterData
    {
        public static ApiData? apiData { get; set; }
    }

    /// <summary>appsettings "Cors" of POGovApi (QPS CorsSettings): origins allowed to call the API from a browser.</summary>
    public class CorsSettings
    {
        public string[] AllowBlazorApp { get; set; } = Array.Empty<string>();
    }

    /// <summary>A file sent to a multipart API (Data Setup uploads).</summary>
    public sealed record ApiUploadFile(string FileName, Stream Content, string ContentType = "application/octet-stream");
}
