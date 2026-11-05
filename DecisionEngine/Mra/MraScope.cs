using DecisionEngine.Model;
using DecisionEngine.Results.Mra;

namespace DecisionEngine.Mra
{
    /// <summary>
    /// The MRA position the single concept engines count (v1.1.7), for the current call: set by the services around a
    /// New PO request / SAP decision / Recommendation (<c>using (MraScope.Use(cap)) …</c>). Nothing set = no MRA counted
    /// (tests, PostgreSQL storage, multi concept). Flows through async calls and Task.Run.
    /// </summary>
    public static class MraScope
    {
        private static readonly AsyncLocal<MraCapacity?> Ambient = new();

        public static MraCapacity? Current => Ambient.Value;

        public static IDisposable Use(MraCapacity? cap)
        {
            var prev = Ambient.Value;
            Ambient.Value = cap;
            return new Restore(prev);
        }

        private sealed class Restore : IDisposable
        {
            private readonly MraCapacity? _prev;
            public Restore(MraCapacity? prev) => _prev = prev;
            public void Dispose() => Ambient.Value = _prev;
        }

        /// <summary>v1.1.7 switch 1: today's tool approvals count only while "Tool requests use capacity" is ON.</summary>
        public static bool ToolRequestsCount(EngineData data) => MraSwitches.From(data.Params).ToolReqCapacity;
    }
}
