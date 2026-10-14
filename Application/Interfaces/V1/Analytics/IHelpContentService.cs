using Application.DTOs.Analytics.Help;
using Domain.Entities.Engine;

namespace Application.Interfaces.V1.Analytics
{
    /// <summary>
    /// "How it works" tab (index.html view-cat-howitworks + renderEngineBHelp, line 1949): the parameter-dependent
    /// values injected into the static help text (Admin/wwwroot/help/hw-*.html, placeholders {{NAME}}).
    /// </summary>
    public interface IHelpContentService
    {
        Task<HelpParamsDto> GetParamsAsync(CancellationToken ct = default);
        HelpParamsDto Compute(EngineParams p);
    }
}
