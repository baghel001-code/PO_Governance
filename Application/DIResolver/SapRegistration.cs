using Application.Interfaces.V1.Sap;
using Application.Services.V1.Sap;
using Microsoft.Extensions.DependencyInjection;

namespace Application.DIResolver
{
    public static class SapRegistration
    {
        /// <summary>
        /// Registers the SAP R/3 interface use cases (/api/sap/v1 and the /admin/sap screen): decisions, events, Basic-auth of the
        /// technical users, ping / reason codes, the portal monitoring service, and the SAP decision engine. Needs an
        /// <see cref="ISapRepository"/> and the SapInterface options (Infrastructure <c>AddSapInterface</c>).
        /// </summary>
        public static IServiceCollection AddSapServices(this IServiceCollection services)
        {
            services.AddDecisionEngine();
            services.AddSingleton<ISapEventService, SapEventService>();
            services.AddSingleton<ISapDecisionService, SapDecisionService>();
            services.AddSingleton<ISapAuthService, SapAuthService>();
            services.AddSingleton<ISapUtilityService, SapUtilityService>();
            services.AddSingleton<ISapAdminService, SapAdminService>();
            return services;
        }
    }
}
