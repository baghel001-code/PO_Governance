using Application.Interfaces.V1.Analytics;
using DecisionEngine.Results.Analytics.AttrAnalysis;
using Domain.Entities.Request;
using Domain.Entities.Response;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace POGovApi.Controllers.V1
{
    /// <summary>Planner "🔎 Attribute Analysis" screen (index.html view-attranalysis). Planners and admins only.</summary>
    [Route("api/v1/[Action]")]
    public sealed class AttributeAnalysisController : ApiControllerBase
    {
        private readonly IAttributeAnalysisService _aa;

        public AttributeAnalysisController(IAttributeAnalysisService aa) => _aa = aa;

        /// <summary>Level 1 — Brand → MC (filters: Brand, Mc, MinStock).</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<AaLevel1Dto>>> AttrAnalysisLevel1([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AttrAnalysisQuery? req, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _aa.GetLevel1Async(req ?? new AttrAnalysisQuery(), ct));
        }

        /// <summary>Level 2 — MRP × Flag buckets of one Brand × MC (McKey = "MC CD|||MC Desc" from level 1).</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<AaLevel2Dto>>> AttrAnalysisLevel2([FromBody] AttrBucketReq req, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _aa.GetLevel2Async(req.Brand ?? "", req.McKey ?? "", ct));
        }

        /// <summary>Level 3 — attribute Sales Mix / Stock Mix tables and colour-level STR of one bucket.</summary>
        [HttpPost]
        public async Task<ActionResult<ApiDataResponse<AaLevel3Dto>>> AttrAnalysisLevel3([FromBody] AttrBucketReq req, CancellationToken ct)
        {
            RequireStaff();
            return Success(await _aa.GetLevel3Async(req.Brand ?? "", req.McKey ?? "", req.Mrp ?? "", req.Flag ?? "", ct));
        }
    }
}
