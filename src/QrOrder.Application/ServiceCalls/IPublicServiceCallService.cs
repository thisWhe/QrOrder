namespace QrOrder.Application.ServiceCalls
{
    public interface IPublicServiceCallService
    {
        Task<ServiceCallDto> CreateAsync(CreateServiceCallRequest request, CancellationToken cancellationToken = default);
    }
}
