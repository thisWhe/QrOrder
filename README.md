# QR Order Management System

QR Order is a multi-tenant restaurant ordering system built with ASP.NET Core and MSSQL. Customers scan a table-specific QR code, open the public menu, place orders, call service staff, and follow order status in real time. Staff members use separate kitchen and service screens, while admins manage menu data, tables, QR links, users, settings, reports, and service call history.

## Features

- Multi-tenant restaurant structure
- Table-specific QR menu access
- Public mobile-first menu page
- Cart and customer order note support
- Real-time order notifications with SignalR
- Kitchen dashboard for new/preparing orders
- Service dashboard for ready orders and waiter calls
- Customer service call flow
- Admin panel for products, categories, tables, users, settings, orders, and reports
- JWT authentication and role-based authorization
- EF Core migrations with MSSQL
- N-Tier architecture

## Tech Stack

- ASP.NET Core 9
- Entity Framework Core
- MSSQL / SQL Server Express
- SignalR
- ASP.NET Core Identity
- JWT Bearer Authentication
- Razor Pages
- Vanilla JavaScript
- CSS

## Project Structure

```text
src/
  QrOrder.Domain/          Domain entities and enums
  QrOrder.Application/     Application contracts and DTOs
  QrOrder.Infrastructure/  EF Core, Identity, services, migrations
  QrOrder.Web/             API controllers, pages, SignalR hubs, UI assets
```

## Main Pages

```text
Admin panel:
http://localhost:5140/staff/admin

Kitchen screen:
http://localhost:5140/staff/kitchen

Service screen:
http://localhost:5140/staff/service

Example customer QR menu:
http://localhost:5140/v/demo-cafe/t/84222936215c466d98c512d3d50947ed
```

## Demo Users

```text
Tenant: demo-cafe

Admin:
admin@demo.com / Admin123!

Kitchen:
kitchen@demo.com / Kitchen123!

Service:
service@demo.com / Service123!
```

## Local Setup

1. Open the solution:

```text
QrOrder.sln
```

2. Configure local development settings in:

```text
src/QrOrder.Web/appsettings.Development.json
```

Example:

```json
{
  "PublicBaseUrl": "http://localhost:5140",
  "ConnectionStrings": {
    "Default": "Server=localhost\\SQLEXPRESS;Database=QrOrderDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;Encrypt=False"
  },
  "Jwt": {
    "Issuer": "QrOrder",
    "Audience": "QrOrderStaff",
    "Key": "your-local-development-secret-key"
  },
  "Seed": {
    "DemoData": true
  }
}
```

3. Run the application:

```powershell
dotnet run --project src\QrOrder.Web\QrOrder.Web.csproj --launch-profile http
```

EF Core migrations are applied automatically on startup in the current development setup.

## Notes

- `appsettings.Development.json`, `bin`, `obj`, `.vs`, `logs`, and local test projects are intentionally ignored.
- Use a strong JWT key and a production SQL Server connection string outside source control for production deployments.
