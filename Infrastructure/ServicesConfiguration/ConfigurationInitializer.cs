using Domain.Entities.Common;
using Microsoft.Extensions.Configuration;
using MyNewEncDec;

namespace Infrastructure.ServicesConfiguration
{
    /// <summary>QPS ConfigurationInitializer: MasterApiSettings → <see cref="ApiMasterData"/>, ApiNamesSettings → <see cref="AllApiNames"/>.</summary>
    public static class ConfigurationInitializer
    {
        public static void InitializeApiSettings(IConfiguration configuration)
        {
            var enc = new New_Enc_Dec();
            ApiMasterData.apiData = configuration.GetSection("MasterApiSettings").Get<ApiData>() ?? new ApiData();
            if (string.IsNullOrWhiteSpace(ApiMasterData.apiData.Task_Name)) ApiMasterData.apiData.Task_Name = "All";
            if (!string.IsNullOrEmpty(ApiMasterData.apiData.Api_Authorization))
                ApiMasterData.apiData.Api_Authorization = enc.My_Decode(ApiMasterData.apiData.Api_Authorization);
            var apiNamesSettings = configuration.GetSection("ApiNamesSettings").Get<ApiNames>() ?? new ApiNames();
            AllApiNames.Initialize(apiNamesSettings);
        }
    }
}
