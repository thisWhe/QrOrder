using Microsoft.EntityFrameworkCore;
using QrOrder.Application.Common;
using QrOrder.Infrastructure.Data;

namespace QrOrder.Infrastructure.Services
{
    public class BusinessHoursService : IBusinessHoursService
    {
        private readonly AppDbContext _db;

        public BusinessHoursService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<BusinessHoursStatus> EvaluateAsync(
            Guid tenantId,
            string timeZoneId,
            CancellationToken cancellationToken = default)
        {
            var hours = await _db.TenantBusinessHours
                .Where(x => x.TenantId == tenantId)
                .ToListAsync(cancellationToken);

            // Mevcut isletmeler ayar kaydedene kadar siparis almaya devam eder.
            if (hours.Count == 0)
                return new BusinessHoursStatus(true, null);

            var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ResolveTimeZone(timeZoneId));
            var localTime = TimeOnly.FromDateTime(localNow.DateTime);
            var today = hours.SingleOrDefault(x => x.DayOfWeek == localNow.DayOfWeek);
            var previousDay = localNow.DayOfWeek == DayOfWeek.Sunday
                ? DayOfWeek.Saturday
                : (DayOfWeek)((int)localNow.DayOfWeek - 1);
            var previous = hours.SingleOrDefault(x => x.DayOfWeek == previousDay);

            var openFromToday = today is { IsOpen: true } && IsWithinToday(today.OpenTime, today.CloseTime, localTime);
            var openFromPreviousNight = previous is { IsOpen: true }
                && previous.OpenTime > previous.CloseTime
                && localTime < previous.CloseTime;

            return openFromToday || openFromPreviousNight
                ? new BusinessHoursStatus(true, null)
                : new BusinessHoursStatus(false, "Isletme su anda kapali. Menuyu inceleyebilirsiniz.");
        }

        private static bool IsWithinToday(TimeOnly open, TimeOnly close, TimeOnly now)
        {
            if (open == close) return true;
            if (open < close) return now >= open && now < close;
            return now >= open;
        }

        private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
        {
            var requested = string.IsNullOrWhiteSpace(timeZoneId) ? "Europe/Istanbul" : timeZoneId;
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(requested);
            }
            catch (TimeZoneNotFoundException) when (requested == "Europe/Istanbul")
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
            }
        }
    }
}
