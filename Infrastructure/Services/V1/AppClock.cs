using System.Globalization;
using Application.Interfaces.V1;
using Domain.Entities.AppSettings;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services.V1
{
    /// <summary>
    /// Server clock in the business time zone (default Asia/Kolkata). server.py used the VM's local
    /// time (IST on .35/.37); this keeps the same dates / offsets wherever the API runs.
    /// </summary>
    public sealed class AppClock : IAppClock
    {
        private readonly TimeZoneInfo _tz;
        private readonly TimeProvider _time;

        public AppClock(IOptions<PoGovSettings> cfg, TimeProvider? time = null)
        {
            _tz = FindZone(cfg.Value.BusinessTimeZone);
            _time = time ?? TimeProvider.System;
        }

        public static TimeZoneInfo FindZone(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) id = "Asia/Kolkata";
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (Exception)
            {
                if (id == "Asia/Kolkata")
                {
                    try { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
                    catch (Exception) { return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "IST", "IST"); }
                }
                return TimeZoneInfo.Local;
            }
        }

        public DateTimeOffset Now => TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _tz);

        public string Today => Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>server.py _now(): 2026-10-14T10:20:30+05:30.</summary>
        public string NowIso => Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    }
}
