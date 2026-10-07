using Amazon.Rekognition;
using Amazon.S3;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using BobsBookstoreClassic.Data;
using Bookstore.Data;
using Bookstore.Data.FileServices;
using Bookstore.Data.ImageResizeService;
using Bookstore.Data.ImageValidationServices;
using Bookstore.Data.Repositories;
using Bookstore.Domain;
using Bookstore.Domain.Addresses;
using Bookstore.Domain.Books;
using Bookstore.Domain.Carts;
using Bookstore.Domain.Customers;
using Bookstore.Domain.Offers;
using Bookstore.Domain.Orders;
using Bookstore.Domain.ReferenceData;
using Bookstore.Web.Helpers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NLog;
using NLog.AWS.Logger;
using NLog.Config;
using NLog.Targets;
using NLog.Web;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

// Initialize BookstoreConfiguration from appsettings.json
BookstoreConfiguration.Initialize(builder.Configuration);

// Configure NLog
ConfigureLogging(builder.Configuration);

// Configure services
builder.Services.AddControllersWithViews();

// Configure DbContext
var connectionString = BookstoreConfiguration.GetConnectionString("BookstoreDatabaseConnection")
    ?? builder.Configuration.GetConnectionString("BookstoreDatabaseConnection");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// Use Autofac as DI container
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder =>
{
    ConfigureDependencyInjection(containerBuilder, builder.Configuration, builder.Environment);
});

// Configure authentication
ConfigureAuthentication(builder.Services, builder.Configuration);

// Load SSM parameters if configured for AWS
ConfigureAwsParameters(builder.Configuration);

var app = builder.Build();

// Ensure database is created/migrated
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    dbContext.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Admin/Error/Index");
    app.UseHsts();
}

app.UseStaticFiles();
// Also serve /Content and /Scripts directories for legacy CSS/JS paths
app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        System.IO.Path.Combine(app.Environment.ContentRootPath, "Content")),
    RequestPath = "/Content"
});
app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        System.IO.Path.Combine(app.Environment.ContentRootPath, "Scripts")),
    RequestPath = "/Scripts"
});
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Register local authentication middleware when not using AWS
if (BookstoreConfiguration.GetSetting("Services/Authentication") != "aws")
{
    app.UseMiddleware<LocalAuthenticationMiddleware>();
}

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

// ─── Local functions ────────────────────────────────────────────────────────

static void ConfigureLogging(IConfiguration configuration)
{
    var config = new LoggingConfiguration();
    Target loggingTarget;

    if (BookstoreConfiguration.GetSetting("Services/LoggingService") == "aws")
    {
        loggingTarget = new AWSTarget { LogGroup = Bookstore.Common.Constants.AppName };
    }
    else
    {
        loggingTarget = new DebuggerTarget("debugger");
    }

    config.AddTarget("main", loggingTarget);
    config.LoggingRules.Add(new LoggingRule("*", NLog.LogLevel.Info, loggingTarget));
    LogManager.Configuration = config;
}

static void ConfigureAuthentication(IServiceCollection services, IConfiguration configuration)
{
    if (BookstoreConfiguration.GetSetting("Services/Authentication") == "aws")
    {
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            options.ClientId = BookstoreConfiguration.GetSetting("Authentication/Cognito/LocalClientId");
            options.MetadataAddress = BookstoreConfiguration.GetSetting("Authentication/Cognito/MetadataAddress");
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.UsePkce = true;
            options.SaveTokens = true;
            options.UseTokenLifetime = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                NameClaimType = "cognito:username",
                RoleClaimType = "cognito:groups"
            };
            options.Events = new OpenIdConnectEvents
            {
                OnRedirectToIdentityProvider = context =>
                {
                    var request = context.HttpContext.Request;
                    var returnUrl = $"{request.Scheme}://{request.Host}/signin-oidc";
                    context.ProtocolMessage.RedirectUri = returnUrl;
                    return Task.CompletedTask;
                },
                OnTokenValidated = async context =>
                {
                    var service = context.HttpContext.RequestServices.GetRequiredService<ICustomerService>();
                    var identity = (ClaimsIdentity)context.Principal.Identity;

                    var dto = new CreateOrUpdateCustomerDto(
                        identity.GetSub(),
                        identity.Name,
                        identity.FindFirst(y => y.Type.Contains("givenname"))?.Value,
                        identity.FindFirst(y => y.Type.Contains("surname"))?.Value);

                    await service.CreateOrUpdateCustomerAsync(dto);
                }
            };
        });
    }
    else
    {
        // No authentication services needed for local mode - middleware handles it
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    services.AddAuthorization();
}

static void ConfigureDependencyInjection(ContainerBuilder builder, IConfiguration configuration, IWebHostEnvironment env)
{
    builder.RegisterType<BookService>().As<IBookService>();
    builder.RegisterType<OrderService>().As<IOrderService>();
    builder.RegisterType<ReferenceDataService>().As<IReferenceDataService>();
    builder.RegisterType<OfferService>().As<IOfferService>();
    builder.RegisterType<CustomerService>().As<ICustomerService>();
    builder.RegisterType<AddressService>().As<IAddressService>();
    builder.RegisterType<ShoppingCartService>().As<IShoppingCartService>();
    builder.RegisterType<ImageResizeService>().As<IImageResizeService>();

    builder.RegisterType<CustomerRepository>().As<ICustomerRepository>().InstancePerLifetimeScope();
    builder.RegisterType<AddressRepository>().As<IAddressRepository>().InstancePerLifetimeScope();
    builder.RegisterType<BookRepository>().As<IBookRepository>().InstancePerLifetimeScope();
    builder.RegisterType<OfferRepository>().As<IOfferRepository>().InstancePerLifetimeScope();
    builder.RegisterType<ShoppingCartRepository>().As<IShoppingCartRepository>().InstancePerLifetimeScope();
    builder.RegisterType<OrderRepository>().As<IOrderRepository>().InstancePerLifetimeScope();
    builder.RegisterType<ReferenceDataRepository>().As<IReferenceDataRepository>().InstancePerLifetimeScope();

    builder.RegisterGeneric(typeof(PaginatedList<>)).As(typeof(IPaginatedList<>)).InstancePerLifetimeScope();

    if (BookstoreConfiguration.GetSetting("Services/FileService") == "aws")
    {
        builder.RegisterType<AmazonS3Client>().As<IAmazonS3>();
        builder.RegisterType<S3FileService>().As<IFileService>();
    }
    else
    {
        var webRootPath = env.WebRootPath ?? env.ContentRootPath;
        builder.RegisterInstance(new LocalFileService(webRootPath)).As<IFileService>();
    }

    if (BookstoreConfiguration.GetSetting("Services/ImageValidationService") == "aws")
    {
        builder.RegisterType<AmazonRekognitionClient>().As<IAmazonRekognition>();
        builder.RegisterType<RekognitionImageValidationService>().As<IImageValidationService>();
    }
    else
    {
        builder.RegisterType<LocalImageValidationService>().As<IImageValidationService>();
    }

    if (BookstoreConfiguration.GetSetting("Services/Authentication") != "aws")
    {
        builder.RegisterType<LocalAuthenticationMiddleware>();
    }
}

static void ConfigureAwsParameters(IConfiguration configuration)
{
    var needsDatabase = BookstoreConfiguration.GetSetting("Services/Database") == "aws";
    var needsAuth = BookstoreConfiguration.GetSetting("Services/Authentication") == "aws";
    var needsFiles = BookstoreConfiguration.GetSetting("Services/FileService") == "aws";

    if (!needsDatabase && !needsAuth && !needsFiles) return;

    using var client = new Amazon.SimpleSystemsManagement.AmazonSimpleSystemsManagementClient();
    var rootPath = "/" + Bookstore.Common.Constants.AppName;

    if (needsDatabase)
    {
        try
        {
            var request = new Amazon.SimpleSystemsManagement.Model.GetParameterRequest
            {
                Name = $"{rootPath}/Database/ConnectionStrings/BookstoreDatabaseConnection"
            };
            var response = client.GetParameterAsync(request).GetAwaiter().GetResult();
            BookstoreConfiguration.AddSetting(
                response.Parameter.Name.Replace($"{rootPath}/Database/", string.Empty),
                response.Parameter.Value);
        }
        catch { /* log and continue */ }
    }

    if (needsAuth)
    {
        try
        {
            var request = new Amazon.SimpleSystemsManagement.Model.GetParametersByPathRequest
            {
                Path = $"{rootPath}/Authentication/",
                Recursive = true
            };
            var response = client.GetParametersByPathAsync(request).GetAwaiter().GetResult();
            foreach (var parameter in response.Parameters)
            {
                BookstoreConfiguration.AddSetting(
                    parameter.Name.Replace($"{rootPath}/", string.Empty),
                    parameter.Value);
            }
        }
        catch { /* log and continue */ }
    }

    if (needsFiles)
    {
        try
        {
            var request = new Amazon.SimpleSystemsManagement.Model.GetParametersByPathRequest
            {
                Path = $"{rootPath}/Files/",
                Recursive = true
            };
            var response = client.GetParametersByPathAsync(request).GetAwaiter().GetResult();
            foreach (var parameter in response.Parameters)
            {
                BookstoreConfiguration.AddSetting(
                    parameter.Name.Replace($"{rootPath}/", string.Empty),
                    parameter.Value);
            }
        }
        catch { /* log and continue */ }
    }
}
