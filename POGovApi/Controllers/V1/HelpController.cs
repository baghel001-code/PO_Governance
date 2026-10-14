using Application.DTOs.Analytics.Help;
using Application.Interfaces.V1.Analytics;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Mvc;

namespace POGovApi.Controllers.V1
{
    /// <summary>"How it works" live values (renderEngineBHelp) for the static help fragments. Any signed-in user.</summary>
    [Route("api/v1/help")]
    public sealed class HelpController : ApiControllerBase
    {
        private readonly IHelpContentService _help;

        public HelpController(IHelpContentService help) => _help = help;

        [HttpGet("params")]
        public async Task<ActionResult<ApiResponse<HelpParamsDto>>> Params(CancellationToken ct) => Success(await _help.GetParamsAsync(ct));
    }
}
