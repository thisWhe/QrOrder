using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using QrOrder.Infrastructure.Data;

namespace QrOrder.Web.Health;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;

    public DatabaseHealthCheck(AppDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("MSSQL bağlantısı hazır.")
                : HealthCheckResult.Unhealthy("MSSQL bağlantısı kurulamadı.");
        }
        catch (Exception error)
        {
            return HealthCheckResult.Unhealthy("MSSQL sağlık kontrolü başarısız.", error);
        }
    }
}
