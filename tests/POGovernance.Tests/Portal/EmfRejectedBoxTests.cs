using Admin.Components.Shared.DataSetup;
using Application.DTOs.Ingest;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace POGovernance.Tests.Portal
{
    /// <summary>The Data Setup box that lists the rejected rows of the Entry / Mid / Fashion mapping upload.</summary>
    public class EmfRejectedBoxTests
    {
        private static async Task<string> RenderAsync(IngestReport? report)
        {
            // every portal component gets the API client injected (_Imports.razor); this box never calls it
            var services = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                .AddSingleton(System.Reflection.DispatchProxy.Create<Application.Interfaces.V1.IAPICall, NoCalls>()).BuildServiceProvider();
            await using var html = new HtmlRenderer(services, NullLoggerFactory.Instance);
            return await html.Dispatcher.InvokeAsync(async () =>
                (await html.RenderComponentAsync<EmfRejectedBox>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Report"] = report }))).ToHtmlString());
        }

        public class NoCalls : System.Reflection.DispatchProxy
        {
            protected override object? Invoke(System.Reflection.MethodInfo? m, object?[]? a) => throw new InvalidOperationException("no API calls expected");
        }

        [Fact]
        public async Task Lists_every_rejected_row_with_its_reason()
        {
            var rep = new IngestReport { DuplicatesIgnored = 1 };
            rep.Rejected.Add(new IngestRejectedRow { File = "EMF.xlsb", Sheet = "Sheet1", Row = 7, Brand = "NON-PL", Mc = "113010003", Flag = "Normal", Mrp = "399", Tag = "4.PREMIUM", Reason = "Tag \"4.PREMIUM\" is not Entry, Mid or Fashion" });
            rep.Rejected.Add(new IngestRejectedRow { File = "EMF.xlsb", Sheet = "Sheet1", Row = 8, Brand = "NON-PL", Mc = "113010003", Flag = "Normal", Mrp = "499", Tag = "", Reason = "No tag — must be Entry, Mid or Fashion" });
            var h = await RenderAsync(rep);
            Assert.Contains("2 row(s) rejected — not loaded", h);
            Assert.Contains("<td>7</td><td>NON-PL</td><td>113010003</td><td>Normal</td><td>399</td>", h);
            Assert.Contains("<strong>4.PREMIUM</strong>", h);
            Assert.Contains("<strong>(blank)</strong>", h);
            Assert.Contains("1 repeated row(s)", h);
            Assert.DoesNotContain("File / sheet", h);   // one file, one sheet: no file column
        }

        [Fact]
        public async Task Shows_nothing_for_a_clean_upload()
        {
            Assert.Equal("", (await RenderAsync(new IngestReport())).Trim());
            Assert.Equal("", (await RenderAsync(null)).Trim());
        }
    }
}
