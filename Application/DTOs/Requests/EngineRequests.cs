using DecisionEngine.Results.Tool2;
using Domain.Entities.Base;

namespace Application.DTOs.Requests
{
    // Request bodies that wrap DecisionEngine filter DTOs (the simple ones are in Domain.Entities.Request).

    /// <summary>Tool2Detail: a run and the detail-view filter (tab B / A, brand, div, mc, mrp, mon, dec).</summary>
    public sealed class Tool2DetailReq : BaseRequest
    {
        public string? RunId { get; set; }
        public Tool2DetailFilterDto? Filter { get; set; }
    }

    /// <summary>Tool2Rollup: a run and the rollup-table filter.</summary>
    public sealed class Tool2RollupReq : BaseRequest
    {
        public string? RunId { get; set; }
        public Tool2RollupFilterDto? Filter { get; set; }
    }
}
