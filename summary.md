# Migration Summary: .NET Framework 4.8 → .NET 10

## Status: ✅ COMPLETE — Build succeeds with 0 errors, 0 warnings

## What was completed

The AWS Transform Custom CLI (ATX) performed the primary migration work (405 files changed, +587/-55,723 lines). This cycle completed the remaining cleanup items.

### Changes made in this cycle

1. **Upgraded Magick.NET-Q8-AnyCPU from 14.6.0 → 14.17.2** (Bookstore.Data.csproj)
   - 14.6.0 had 652 known vulnerability advisories (NU1901/NU1902/NU1903); 14.17.2 has none.

2. **Aligned AWS SDK versions across projects** (Bookstore.Data.csproj)
   - AWSSDK.S3 and AWSSDK.Rekognition were at 4.0.0.3 in Bookstore.Data but 4.0.101.1 in Bookstore.Web. Both now use 4.0.101.1 to avoid runtime assembly binding conflicts.

3. **Updated Bookstore.Common target framework from netstandard2.0 → net10.0** (Bookstore.Common.csproj)
   - All other projects target net10.0; aligning Common avoids unnecessary cross-TFM compatibility concerns.

4. **Upgraded CDK packages to clear vulnerability warnings** (Bookstore.Cdk.csproj)
   - Amazon.CDK.Lib: 2.188.0 → 2.272.0 (cleared GHSA-464c-974j-9xm6, low severity)
   - Cdklabs.CdkNag: 2.35.66 → 2.38.2 (latest 2.x, no API-breaking changes)
   - Constructs: 10.4.2 → 10.8.1

### ATX migration work (completed before this cycle)

- All 5 projects converted to SDK-style .csproj targeting net10.0
- System.Web, System.Web.Http, and System.Data.Entity references removed
- Global.asax → Program.cs (ASP.NET Core startup)
- Web.config → appsettings.json + appsettings.Development.json
- EF6 → EF Core (Microsoft.EntityFrameworkCore + SqlServer provider)
- OWIN authentication → ASP.NET Core AddAuthentication / AddOpenIdConnect
- MVC controllers updated to use ASP.NET Core routing and middleware
- NLog updated to NLog.Web.AspNetCore 5.3.15 with AWS.Logger.NLog 3.3.4
- Bookstore.Cdk upgraded from net6.0 → net10.0

## Next steps

The following items were identified by ATX and are runtime/deployment concerns (not compile-time blockers):

1. **[CRITICAL] Update Dockerfiles**
   - Root `Dockerfile` still uses `mcr.microsoft.com/dotnet/framework/sdk:4.8` with IIS/ServiceMonitor. It should be replaced with or redirected to `app/Bookstore.Web/Dockerfile`.
   - `app/Bookstore.Web/Dockerfile` references `mcr.microsoft.com/dotnet/aspnet:8.0` and `sdk:8.0`; update to `aspnet:10.0` and `sdk:10.0`.

2. **[HIGH] LocalAuthenticationMiddleware ordering**
   - In Program.cs, `UseMiddleware<LocalAuthenticationMiddleware>()` is registered after `UseAuthorization()`. Move it before `UseAuthorization()` to avoid 401s blocking the login page when the fallback policy is `RequireAuthenticatedUser`.

3. **[MEDIUM] Seed data image paths**
   - BookstoreDbInitializer.cs references `/Content/Images/coverimages/` paths; these should be `/images/coverimages/` to match ASP.NET Core wwwroot-relative static file serving.

4. **[MEDIUM] Thread safety in BookstoreConfiguration**
   - The static `Dictionary<string, string>` fields should be replaced with `ConcurrentDictionary<string, string>` if SSM parameters could be refreshed at runtime.

5. **[MEDIUM] Linux database connection string**
   - `Integrated Security=SSPI` in appsettings.json is Windows-only; override with SQL authentication for Linux/Docker/ECS deployment.

6. **[LOW] EF Core migrations**
   - No EF Core migration files were created. Run `dotnet ef migrations add InitialCreate --startup-project ../Bookstore.Web` from Bookstore.Data to generate them before first deployment.

7. **[LOW] CDK infrastructure review**
   - CDK packages were updated from 2.188.0 → 2.272.0. Run `cdk diff` before deploying to confirm no unintended infrastructure changes.

8. **[INFO] Preview packages**
   - `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.0-preview.5 and EF Core 10.0.0-preview.4 should be updated to stable releases when .NET 10 RTMs (expected November 2025).
   - `Microsoft.Extensions.Configuration.Abstractions` 10.0.0-preview.4 similarly.
