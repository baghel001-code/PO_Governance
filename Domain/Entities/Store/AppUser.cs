using System.Text.Json;
using System.Text.Json.Serialization;

namespace Domain.Entities.Store
{
    /// <summary>
    /// One login — a value of the "users" document (table app_users, column data), same JSON as
    /// accounts.py <c>_new_user</c>. lockedUntil is epoch seconds (0 = not locked).
    /// </summary>
    public sealed class AppUser
    {
        [JsonPropertyName("email")] public string Email { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("roles")] public List<string> Roles { get; set; } = new();
        [JsonPropertyName("active")] public bool Active { get; set; }
        /// <summary>PBKDF2-HMAC-SHA256: base64(salt) + "$" + base64(dk).</summary>
        [JsonPropertyName("pw")] public string Pw { get; set; } = "";
        [JsonPropertyName("mustChange")] public bool MustChange { get; set; }
        [JsonPropertyName("failed")] public int Failed { get; set; }
        [JsonPropertyName("lockedUntil")] public double LockedUntil { get; set; }
        [JsonPropertyName("createdAt")] public string? CreatedAt { get; set; }
        [JsonPropertyName("createdBy")] public string? CreatedBy { get; set; }
        [JsonPropertyName("source")] public string? Source { get; set; }
        [JsonPropertyName("lastLogin")] public string? LastLogin { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

        public AppUser Clone() => new()
        {
            Email = Email, Name = Name, Roles = Roles.ToList(), Active = Active, Pw = Pw, MustChange = MustChange,
            Failed = Failed, LockedUntil = LockedUntil, CreatedAt = CreatedAt, CreatedBy = CreatedBy, Source = Source,
            LastLogin = LastLogin, Extra = Extra == null ? null : new Dictionary<string, JsonElement>(Extra)
        };
    }

    /// <summary>A value of the "sessions" document (table app_sessions): created / seen are epoch seconds.</summary>
    public sealed class SessionRecord
    {
        [JsonPropertyName("email")] public string Email { get; set; } = "";
        [JsonPropertyName("created")] public double Created { get; set; }
        [JsonPropertyName("seen")] public double Seen { get; set; }
        [JsonPropertyName("ip")] public string? Ip { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    }

    /// <summary>
    /// The "scr_mapping" document: the admin's choices for the SCR products file
    /// ({"columns": {header: "buyer"|"cathead"|"skip"}, "brandCol", "mcCol", "mrpCol"}).
    /// </summary>
    public sealed class ScrMapping
    {
        [JsonPropertyName("columns")] public Dictionary<string, string>? Columns { get; set; }
        [JsonPropertyName("brandCol")] public string? BrandCol { get; set; }
        [JsonPropertyName("mcCol")] public string? McCol { get; set; }
        [JsonPropertyName("mrpCol")] public string? MrpCol { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    }

    /// <summary>One line of the audit log (table audit_log / file audit.log, tab separated).</summary>
    public sealed record AuditLine(string At, string? Who, string? Action, string? Detail);

    /// <summary>One stored blob (storage.py list_blobs tuple): mtime = epoch seconds.</summary>
    public sealed record BlobInfo(string Key, string Version, bool Deleted, double Mtime);

    /// <summary>Blob kinds (storage.py): "ds" = data-file versions, "run" = saved Tool 2 runs.</summary>
    public static class BlobKinds
    {
        public const string Dataset = "ds";
        public const string Run = "run";
    }

    /// <summary>Document names (storage.py DOCS).</summary>
    public static class DocNames
    {
        public const string Manifest = "manifest";
        public const string Params = "params";
        public const string Users = "users";
        public const string Sessions = "sessions";
        public const string ScrMapping = "scr_mapping";
        public static readonly string[] All = { Manifest, Params, Users, Sessions, ScrMapping };
    }

    /// <summary>Log streams (storage.py LOGS).</summary>
    public static class LogStreams
    {
        public const string Uploads = "uploads";
        public const string Batches = "batches";
        public const string Runs = "runs";
        public const string Requests = "requests";
        public const string Feedback = "feedback";
        public static readonly string[] All = { Uploads, Batches, Runs, Requests, Feedback };
    }
}
