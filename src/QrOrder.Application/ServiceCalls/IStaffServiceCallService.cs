namespace QrOrder.Application.ServiceCalls
{
    public interface IStaffServiceCallService
    {
        Task<List<ServiceCallDto>> ListAsync(bool activeOnly, CancellationToken cancellationToken = default);
        Task<ServiceCallDto?> CompleteAsync(Guid id, CancellationToken cancellationToken = default);
    }
}
