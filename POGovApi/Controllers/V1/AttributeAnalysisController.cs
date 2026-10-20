using Application.Interfaces.V1.Analytics;
using DecisionEngine.Results.Analytics.AttrAnalysis;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Mvc;

namespace POGovApi.Controllers.V1
{
    /// <summary>Planner "🔎 Attribute Analysis" screen (index.html view-attranalysis). Planners and admins only.</summary>
    [Route("api/v1/attribute-analysis")]
    public sealed class AttributeAnalysisController : ApiControllerBase
    {
        private readonly IAttributeAnalysisService _aa;

        public AttributeAnalysisController(IAttributeAnalysisService aa) => _aa = aa;

        /// <summary>Level 1 — Brand → MC (filters: brand, mc, minStock).</summary>
        [HttpGet]
        public async Task<ActionResult<ApiResponse<AaLevel1Dto>>> Level1([FromQuery] AttrAnalysisQuery query, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _aa.GetLevel1Async(query, ct));
        }

        /// <summary>Level 2 — MRP × Flag buckets of one Brand × MC (mcKey = "MC CD|||MC Desc" from level 1).</summary>
        [HttpGet("mc")]
        public async Task<ActionResult<ApiResponse<AaLevel2Dto>>> Level2([FromQuery] string brand, [FromQuery] string mcKey, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _aa.GetLevel2Async(brand, mcKey, ct));
        }

        /// <summary>Level 3 — attribute Sales Mix / Stock Mix tables and colour-level STR of one bucket.</summary>
        [HttpGet("bucket")]
        public async Task<ActionResult<ApiResponse<AaLevel3Dto>>> Level3([FromQuery] string brand, [FromQuery] string mcKey, [FromQuery] string? mrp, [FromQuery] string? flag, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _aa.GetLevel3Async(brand, mcKey, mrp ?? "", flag ?? "", ct));
        }
    }
}
