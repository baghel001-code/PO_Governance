using Application.Interfaces.V1.Analytics;
using DecisionEngine.Results.Analytics.Help;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Mvc;

namespace POGovApi.Controllers.V1
{
    /// <summary>"How it works" live values (renderEngineBHelp) for the static help fragments. Any signed-in user.</summary>
    [Route("api/v1/[Action]")]
    public sealed class HelpController : ApiControllerBase
    {
        private readonly IHelpContentService _help;

        public HelpController(IHelpContentService help) => _help = help;

        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<HelpParamsDto>>> HelpParams(CancellationToken ct) => Success(await _help.GetParamsAsync(ct));
    }
}
