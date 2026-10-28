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
        /// <summary>
        /// HRMS employee code: the person signs in with it and their HRMS password (checked by POGovApi with the HRMS
        /// login API). Empty = a local account (email + the password stored in <see cref="Pw"/>).
        /// </summary>
        [JsonPropertyName("empCode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EmpCode { get; set; }
        /// <summary>The HRMS details the admin fetched when registering the employee (or refreshed later).</summary>
        [JsonPropertyName("hr"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HrProfile? Hr { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

        /// <summary>Signs in with Employee ID + HRMS password.</summary>
        [JsonIgnore] public bool IsEmployee => !string.IsNullOrWhiteSpace(EmpCode);

        public AppUser Clone() => new()
        {
            Email = Email, Name = Name, Roles = Roles.ToList(), Active = Active, Pw = Pw, MustChange = MustChange,
            Failed = Failed, LockedUntil = LockedUntil, CreatedAt = CreatedAt, CreatedBy = CreatedBy, Source = Source,
            LastLogin = LastLogin, EmpCode = EmpCode, Hr = Hr?.Clone(),
            Extra = Extra == null ? null : new Dictionary<string, JsonElement>(Extra)
        };
    }

    /// <summary>An employee's HRMS details as stored with the login (HRMS employee API field names in the comments).</summary>
    public sealed class HrProfile
    {
        [JsonPropertyName("email")] public string? Email { get; set; }              // empMailID
        [JsonPropertyName("location")] public string? Location { get; set; }        // empLocation
        [JsonPropertyName("locationCode")] public string? LocationCode { get; set; } // empLocCode
        [JsonPropertyName("department")] public string? Department { get; set; }    // empDeptName
        [JsonPropertyName("designation")] public string? Designation { get; set; }  // empDesig
        [JsonPropertyName("mobile")] public string? Mobile { get; set; }            // empContactNo
        [JsonPropertyName("manager")] public string? Manager { get; set; }          // empRptMng
        [JsonPropertyName("grade")] public int Grade { get; set; }                  // empGrade
        [JsonPropertyName("gradeDesc")] public string? GradeDesc { get; set; }      // empGradeDesc
        [JsonPropertyName("hrActive")] public bool HrActive { get; set; }           // empStatus
        [JsonPropertyName("resigned")] public bool Resigned { get; set; }           // isResign
        [JsonPropertyName("fetchedAt")] public string? FetchedAt { get; set; }
        [JsonPropertyName("fetchedBy")] public string? FetchedBy { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

        public HrProfile Clone()
        {
            var c = (HrProfile)MemberwiseClone();
            c.Extra = Extra == null ? null : new Dictionary<string, JsonElement>(Extra);
            return c;
        }
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
