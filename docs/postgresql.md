# PostgreSQL for FoodVision

Each module owns its data model and EF Core migrations. A module must expose its
own `DbContext`, use a dedicated PostgreSQL schema, and keep its migration files
next to the module's PostgreSQL infrastructure project. Modules do not query
another module's tables or use its `DbContext`.

The common connection-string key is `ConnectionStrings:FoodVision`. Its value is
provided through environment variables or user secrets; no connection credentials
are committed to the repository.

## Local development

1. Copy `docker/.env.postgres.example` to `docker/.env.postgres` and replace the
   placeholders with local-only values.
2. Start PostgreSQL together with the application:

   ```powershell
   docker compose --env-file docker/.env.postgres -f docker-compose.yml -f docker-compose.postgres.yml up --build
   ```

   The backend receives a container-network connection string automatically.

3. To run the backend from the host, store the connection string outside tracked
   configuration, for example in an environment variable:

   ```powershell
   $env:ConnectionStrings__FoodVision = "Host=localhost;Port=5432;Database=<database>;Username=<user>;Password=<password>"
   ```

## Migrations

When a connection string is configured, the application applies all registered
module migrations at startup, before accepting requests. A migration failure
prevents startup. Without a connection string, PostgreSQL is not registered and
the application starts without database access.

Create migrations in the owning PostgreSQL-infrastructure project. The application
project is used as the startup project for EF Core tools.

The `dotnet-ef` major version must match the EF Core package major version. This
repository currently uses EF Core 10, so verify it with `dotnet ef --version`
before using the commands below.

```powershell
dotnet ef migrations add <MigrationName> --project backend/src/Modules/<Module>/<Module>.Infrastructure.Postgres/<Module>.Infrastructure.Postgres.csproj --startup-project backend/src/FoodVision.App/FoodVision.App.csproj --context <Module>DbContext
dotnet ef database update --project backend/src/Modules/<Module>/<Module>.Infrastructure.Postgres/<Module>.Infrastructure.Postgres.csproj --startup-project backend/src/FoodVision.App/FoodVision.App.csproj --context <Module>DbContext
```

To verify migrations against a clean database, use an isolated Compose project and
a free local port. The cleanup targets only the isolated project's resources.

```powershell
$env:POSTGRES_PORT = "55432"
$env:ConnectionStrings__FoodVision = "Host=localhost;Port=55432;Database=<database>;Username=<user>;Password=<password>"
docker compose --project-name foodvision-db-clean-check --env-file docker/.env.postgres -f docker-compose.yml -f docker-compose.postgres.yml up -d --wait postgres
dotnet run --project backend/src/FoodVision.App/FoodVision.App.csproj
```

Wait for the application to start, stop it with Ctrl+C, then clean up:

```powershell
docker compose --project-name foodvision-db-clean-check --env-file docker/.env.postgres -f docker-compose.yml -f docker-compose.postgres.yml down --volumes
Remove-Item Env:POSTGRES_PORT, Env:ConnectionStrings__FoodVision
```

This creates every module's migration history and schema from scratch.

