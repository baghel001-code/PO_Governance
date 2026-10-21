namespace Infrastructure.Services.V1
{
    /// <summary>
    /// Whether the start-up work (database reachable, schema present, users loaded, migrate_ids, prune_history)
    /// has finished. Until it has, <see cref="Middleware.DatabaseReadinessMiddleware"/> answers every api/v1 call
    /// (except Health) with 503 and the reason, so the portal can show why instead of "Cannot reach the server".
    /// </summary>
    public sealed class DatabaseReadiness
    {
        private volatile bool _ready;
        private volatile string? _error;

        public bool Ready => _ready;
        /// <summary>Why the last start-up attempt failed (null once ready or before the first attempt ends).</summary>
        public string? Error => _error;

        public void MarkReady() { _error = null; _ready = true; }
        public void MarkFailed(string error) => _error = error;
    }
}
