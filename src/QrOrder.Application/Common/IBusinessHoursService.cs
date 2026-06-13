namespace QrOrder.Application.Common
{
    public record BusinessHoursStatus(bool IsOpen, string? ClosedMessage);

    public interface IBusinessHoursService
    {
        Task<BusinessHoursStatus> EvaluateAsync(
            Guid tenantId,
            string timeZoneId,
            CancellationToken cancellationToken = default);
    }
}
