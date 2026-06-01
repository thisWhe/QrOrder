namespace QrOrder.Application.Public
{
    public record CreateTableSessionResult(string SessionToken, DateTimeOffset ExpiresAt, int TableNumber);
}
