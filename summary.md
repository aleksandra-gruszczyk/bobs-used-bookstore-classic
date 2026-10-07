# Migration Summary: .NET Framework 4.8 → .NET 10

## Build Status: ✅ ZERO errors, ZERO warnings

All five projects compile cleanly against `net10.0` (or `net10.0;netstandard2.0` for library projects):

| Project | TFM | Status |
|---|---|---|
| Bookstore.Common | net10.0;netstandard2.0 | ✅ 0 errors, 0 warnings |
| Bookstore.Domain | net10.0;netstandard2.0 | ✅ 0 errors, 0 warnings |
| Bookstore.Data | net10.0 | ✅ 0 errors, 0 warnings |
| Bookstore.Web | net10.0 | ✅ 0 errors, 0 warnings |
| Bookstore.Cdk | net10.0 | ✅ 0 errors, 0 warnings |

---

## Changes Made

### Project Files

- **Bookstore.Common.csproj** — Converted to SDK-style, multi-targeted `net10.0;netstandard2.0`.
- **Bookstore.Domain.csproj** — Converted to SDK-style, multi-targeted `net10.0;netstandard2.0`; removed redundant `Microsoft.CSharp` reference.
- **Bookstore.Data.csproj** — Converted to SDK-style, single target `net10.0`; replaced `EntityFramework 6.5.1` with `Microsoft.EntityFrameworkCore 9.0.5` + `Microsoft.EntityFrameworkCore.SqlServer 9.0.5`; upgraded `Magick.NET-Q8-AnyCPU` from 14.6.0 → 14.17.2 (resolved >300 vulnerability warnings).
- **Bookstore.Web.csproj** — Fully rewritten from old MSBuild XML format to `Microsoft.NET.Sdk.Web` SDK-style targeting `net10.0`; removed all old .NET Framework 4.8 references (Autofac.Mvc5, Autofac.Owin, OWIN middleware packages, System.Web.Mvc, EntityFramework, WebGrease, Antlr3, etc.); added ASP.NET Core packages.
- **Bookstore.Cdk.csproj** — Upgraded `Amazon.CDK.Lib` from 2.188.0 → 2.272.0 (resolved NU1901 vulnerability warning) and updated `Constructs` to 10.5.0 to match the new CDK.Lib dependency requirements.

### Data Access (EF6 → EF Core)

- **ApplicationDbContext.cs** — Migrated from `System.Data.Entity.DbContext` to `Microsoft.EntityFrameworkCore.DbContext`; constructor now takes `DbContextOptions<ApplicationDbContext>`; rewrote `OnModelCreating` using EF Core Fluent API (`HasOne/WithMany`, `OnDelete(DeleteBehavior.Restrict)`, `ValueGeneratedOnAdd`, explicit `ToTable` calls for singular names).
- **BookstoreDbInitializer.cs** — Replaced `DropCreateDatabaseIfModelChanges<>` (EF6) with a static `Seed(ModelBuilder)` method using EF Core `HasData` seeding called from `OnModelCreating`.
- **PaginatedList.cs** — Replaced `System.Data.Entity` imports with `Microsoft.EntityFrameworkCore` for `CountAsync` and `ToListAsync`.
- **All Repositories** (AddressRepository, BookRepository, CustomerRepository, OfferRepository, OrderRepository, ReferenceDataRepository, ShoppingCartRepository) — Replaced `System.Data.Entity` with `Microsoft.EntityFrameworkCore`; removed `await Task.Run(() => ...)` wrapper around Add calls (replaced with `AddAsync`); replaced `Include("NavigationProperty")` string-based includes with lambda-based includes; replaced EF6 `.Include(x => x.OrderItems.Select(y => y.Book))` with EF Core `.ThenInclude()` chain; replaced `x.FirstOrDefault().Book` with `x.First().Book` in LINQ grouping projection.

### Configuration

- **BookstoreConfiguration.cs** — Replaced `System.Configuration.ConfigurationManager` with `Microsoft.Extensions.Configuration.IConfiguration`; added static `Initialize(IConfiguration)` method called at startup; retains the existing static API (`GetSetting`, `AddSetting`, `GetConnectionString`, etc.) unchanged so all callers continue to work.
- **Web.config** — Replaced with `appsettings.json` (all service flags, Cognito settings, file service settings, connection string).
- **appsettings.Development.json** — Added for development-specific log levels.
- **Views/Web.config** and **Areas/Admin/Views/web.config** — Removed (MVC 5 Razor configuration, not applicable in ASP.NET Core).

### Application Startup

- **Global.asax** / **Global.asax.cs** — Removed. All lifecycle code migrated to `Program.cs`.
- **Startup.cs** (OWIN) — Removed. Replaced by ASP.NET Core `Program.cs`.
- **App_Start/\*** — All six files (AuthenticationSetup, BundleConfig, ConfigurationSetup, DependencyInjectionSetup, FilterConfig, LoggingSetup, RouteConfig) replaced with stub comments; logic migrated to `Program.cs`.
- **Program.cs** (new) — Full ASP.NET Core top-level program. Registers `ControllersWithViews`, `DbContext` with SQL Server, Autofac `AutofacServiceProviderFactory`, cookie + OpenID Connect authentication (Cognito or local), NLog, static file serving from `wwwroot`, `Content/`, and `Scripts/` directories, and area/default controller routes. Loads AWS SSM parameters at startup when configured. Ensures EF Core database is created on first run.

### OWIN → ASP.NET Core Middleware

- **LocalAuthenticationMiddleware.cs** — Rewritten as a standard ASP.NET Core `IMiddleware`-style middleware (constructor takes `RequestDelegate next`, `InvokeAsync` receives `HttpContext` + `ICustomerService` via DI); no longer depends on `Microsoft.Owin`.
- **IOwinRequestExtensions.cs** — Replaced with an empty stub (logic now in `Program.cs` OpenIdConnect event handlers).

### Helpers & Validators

- **HttpContextExtensions.cs** — `HttpContextBase` → `HttpContext` (ASP.NET Core); `HttpCookie` → `CookieOptions` + `Response.Cookies.Append`.
- **ControllerExtensions.cs** — `System.Web.Mvc.Controller` → `Microsoft.AspNetCore.Mvc.Controller`.
- **MvcHelpers.cs** — `System.Web.Mvc.HtmlHelper` → `Microsoft.AspNetCore.Mvc.Rendering.IHtmlHelper`.
- **ImageTypesAttribute.cs** — `System.Web.HttpPostedFileBase` → `Microsoft.AspNetCore.Http.IFormFile`; `file.FileName` still available.
- **MaxFileSizeAttribute.cs** — `HttpPostedFileBase` → `IFormFile`; `file.ContentLength` → `file.Length`.

### Controllers

All controllers migrated:
- `using System.Web.Mvc` → `using Microsoft.AspNetCore.Mvc`
- `ActionResult` → `IActionResult`
- `[RouteArea]` → `[Area]`
- `HttpContext` property type changed to ASP.NET Core `HttpContext`
- `HttpContext.GetShoppingCartCorrelationId()` extension now on `HttpContext` (not `HttpContextBase`)
- **InventoryController** — `model.CoverImage?.InputStream` → `model.CoverImage?.OpenReadStream()`

### View Models

- `System.Web.Mvc.SelectListItem` → `Microsoft.AspNetCore.Mvc.Rendering.SelectListItem` in all view models that build select lists (AddressCreateUpdateViewModel, InventoryCreateUpdateViewModel, InventoryIndexViewModel, OfferIndexViewModel, ResaleCreateViewModel, ReferenceDataCreateViewModel).
- `System.Web.HttpPostedFileBase` → `Microsoft.AspNetCore.Http.IFormFile` in InventoryCreateUpdateViewModel.

### Views

- **Views/_ViewImports.cshtml** — Added `@using Microsoft.AspNetCore.Routing` alongside existing tag helper import.
- **Areas/Admin/Views/_ViewImports.cshtml** — Created with required namespaces.
- **Areas/Admin/Views/Orders/Index.cshtml** — Replaced `Html.EnumDropDownListFor` (not available for nullable enums in this version) with `Html.DropDownListFor` + `Html.GetSelectListForEnum<OrderStatus>` helper.
- **Areas/Admin/Views/Offers/Index.cshtml** — Same fix for `Html.EnumDropDownListFor` → `Html.DropDownListFor` + `Html.GetSelectListForEnum<OfferStatus>`.
- **All views with `@Html.Partial(...)` / `@Html.RenderPartial(...)`** — Replaced with `<partial name="..." />` tag helper to eliminate MVC1000 deadlock warnings.
- **Areas/Admin/AdminAreaRegistration.cs** — Replaced with an empty stub; area routing is handled by `Program.cs`.

---

## Architecture Notes

- **EF Core database initialization**: `dbContext.Database.EnsureCreated()` is called at startup. This creates the schema and seeds data (via `HasData`) on first run, equivalent to the old EF6 `DropCreateDatabaseIfModelChanges` behavior without the drop/recreate semantics. For a production environment, consider switching to explicit EF Core migrations.
- **Static files**: The app retains its `Content/` and `Scripts/` folder layout (instead of moving everything to `wwwroot/`) and serves those via additional `UseStaticFiles` middleware configurations. This preserves all existing URL paths in views.
- **Autofac**: Migrated from `Autofac.Mvc5` + `Autofac.Owin` to `Autofac.Extensions.DependencyInjection` 10.0.0 using `UseServiceProviderFactory(new AutofacServiceProviderFactory())`. Repository registrations use `InstancePerLifetimeScope` (equivalent to `InstancePerRequest` in OWIN).

---

## Next Steps

- **EF Core Migrations**: Replace `EnsureCreated()` with proper EF Core migrations (`dotnet ef migrations add InitialCreate`, `dotnet ef database update`) for safe production schema evolution.
- **Amazon CDK.Lib 2.272.0**: If the `Cdklabs.CdkNag` version 2.35.66 is incompatible with the updated CDK.Lib, update it as well.
- **Unit/integration tests**: No test project existed in this solution. Consider adding one targeting `net10.0`.
- **File paths**: The book seed data still uses `/Content/Images/coverimages/...` paths. These are now served from the `Content/` static-files path. Verify the paths resolve at runtime.
- **Cognito HTTPS**: As noted in the original README, the Amazon Cognito Hosted UI requires HTTPS for redirect URIs (except `localhost`). Configure HTTPS (e.g., via `app.UseHttpsRedirection()`) or an HTTPS termination proxy before enabling Cognito auth in production.
