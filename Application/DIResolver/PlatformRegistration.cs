using Application.Interfaces.V1;
using Application.Services.V1.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace Application.DIResolver
{
    public static class PlatformRegistration
    {
        /// <summary>
        /// Platform business services (accounts, requests, approvals, data, history, runs, params,
        /// feedback, audit, status, AI, start-up maintenance). All are singletons: accounts and the SCR
        /// source keep in-memory state like the v1.1.4 server; the others are stateless.
        /// Needs IStore, IAppClock, IPasswordHasher, IDatasetCache and IAiClient from AddInfrastructure.
        /// </summary>
        public static IServiceCollection AddPlatformServices(this IServiceCollection services)
        {
            services.AddSingleton<IAuditService, AuditService>();
            services.AddSingleton<IScrSource, ScrSource>();
            services.AddSingleton<IAccountsService, AccountsService>();

            services.AddSingleton<RequestService>();
            services.AddSingleton<IRequestService>(sp => sp.GetRequiredService<RequestService>());
            services.AddSingleton<IRequestLedger>(sp => sp.GetRequiredService<RequestService>());

            services.AddSingleton<DataService>();
            services.AddSingleton<IDataService>(sp => sp.GetRequiredService<DataService>());
            services.AddSingleton<IDatasetStore>(sp => sp.GetRequiredService<DataService>());

            services.AddSingleton<IApprovalService, ApprovalService>();
            services.AddSingleton<IHistoryService, HistoryService>();
            services.AddSingleton<IRunService, RunService>();
            services.AddSingleton<IParamsService, ParamsService>();
            services.AddSingleton<IFeedbackService, FeedbackService>();
            services.AddSingleton<IStatusService, StatusService>();
            services.AddSingleton<IAiService, AiService>();
            services.AddSingleton<IMaintenanceService, MaintenanceService>();
            return services;
        }
    }
}
