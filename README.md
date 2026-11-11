# AcxiomCRM

AcxiomCRM is an ASP.NET Core MVC customer relationship management app. It includes customer, lead, and opportunity management, a sales dashboard, and account authentication with role-based access.

## Requirements

- .NET 10 SDK

## Run locally

From the project directory:

```powershell
dotnet restore
dotnet run
```

The app applies its Entity Framework Core migrations at startup and creates the SQLite database (`acxiomcrm.db`) locally. The HTTP and HTTPS launch profiles are defined in `Properties/launchSettings.json`.

## Accounts and roles

Register through the app to create a Sales Executive account. Admin and Manager roles are created at startup, but no accounts with those roles are created unless bootstrap credentials are configured.

To create a bootstrap account, set the corresponding environment variables before starting the app:

| Account | Email variable | Password variable |
| --- | --- | --- |
| Admin | `BootstrapUsers__Admin__Email` | `BootstrapUsers__Admin__Password` |
| Manager | `BootstrapUsers__Manager__Email` | `BootstrapUsers__Manager__Password` |

Set both variables for an account or neither. Passwords must meet the application's Identity password policy. Keep these values in a local environment or secret manager; do not commit them to source control. The local `appsettings.*.json` files are ignored by Git.

## Project structure

- `Controllers/`, `Views/`, and `ViewModels/` contain the MVC application.
- `Models/` and `Data/` contain the domain entities, EF Core context, and startup seeding.
- `Migrations/` contains the EF Core database migrations.
- `wwwroot/` contains static assets.

## Configuration and generated files

The default connection string uses a local SQLite file. Local database files, build output, IDE state, logs, environment files, and environment-specific appsettings files are excluded by `.gitignore`. The base `appsettings.json` remains tracked for non-secret defaults; do not put credentials or other secrets in it.
