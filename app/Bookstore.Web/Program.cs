using System.Security.Claims;
using Amazon.Rekognition;
using Amazon.S3;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using BobsBookstoreClassic.Data;
using Bookstore.Common;
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
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NLog;
using NLog.AWS.Logger;
using NLog.Config;
using NLog.Targets;
using NLog.Web;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration: Initialize BookstoreConfiguration from IConfiguration ──
BookstoreConfiguration.Initialize(builder.Configuration);

// ── Configuration: Load AWS SSM parameters if services are configured for AWS ──
ConfigureAwsParametersAsync(builder.Configuration).GetAwaiter().GetResult();

// ── Logging: NLog integration ──
ConfigureNLog();
builder.Logging.ClearProviders();
builder.Host.UseNLog();

// ── DI: Services ──
builder.Services.AddScoped<IBookService, BookService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IReferenceDataService, ReferenceDataService>();
builder.Services.AddScoped<IOfferService, OfferService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IAddressService, AddressService>();
builder.Services.AddScoped<IShoppingCartService, ShoppingCartService>();
builder.Services.AddScoped<IImageResizeService, ImageResizeService>();

// ── DI: DbContext ──
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("BookstoreDatabaseConnection")));

// ── DI: Repositories ──
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IAddressRepository, AddressRepository>();
builder.Services.AddScoped<IBookRepository, BookRepository>();
builder.Services.AddScoped<IOfferRepository, OfferRepository>();
builder.Services.AddScoped<IShoppingCartRepository, ShoppingCartRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IReferenceDataRepository, ReferenceDataRepository>();

// ── DI: Open generic ──
builder.Services.AddScoped(typeof(IPaginatedList<>), typeof(PaginatedList<>));

// ── DI: File service (conditional) ──
if (builder.Configuration["Services:FileService"] == "aws")
{
    builder.Services.AddSingleton<IAmazonS3, AmazonS3Client>();
    builder.Services.AddScoped<IFileService, S3FileService>();
}
else
{
    builder.Services.AddScoped<IFileService>(sp =>
    {
        var env = sp.GetRequiredService<IWebHostEnvironment>();
        return new LocalFileService(env.WebRootPath ?? env.ContentRootPath);
    });
}

// ── DI: Image validation service (conditional) ──
if (builder.Configuration["Services:ImageValidationService"] == "aws")
{
    builder.Services.AddSingleton<IAmazonRekognition, AmazonRekognitionClient>();
    builder.Services.AddScoped<IImageValidationService, RekognitionImageValidationService>();
}
else
{
    builder.Services.AddScoped<IImageValidationService, LocalImageValidationService>();
}

// ── Authentication ──
if (builder.Configuration["Services:Authentication"] == "aws")
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie()
    .AddOpenIdConnect(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Cognito:LocalClientId"];
        options.MetadataAddress = builder.Configuration["Authentication:Cognito:MetadataAddress"];
        options.ResponseType = "code";
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.SaveTokens = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "cognito:username",
            RoleClaimType = "cognito:groups"
        };
        options.Events = new OpenIdConnectEvents
        {
            OnRedirectToIdentityProvider = context =>
            {
                var returnUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}/signin-oidc";
                context.ProtocolMessage.RedirectUri = returnUrl;
                return Task.CompletedTask;
            },
            OnAuthorizationCodeReceived = context =>
            {
                var returnUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}/signin-oidc";
                context.TokenEndpointRequest.RedirectUri = returnUrl;
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var customerService = context.HttpContext.RequestServices.GetRequiredService<ICustomerService>();
                var identity = (ClaimsIdentity)context.Principal!.Identity!;

                var dto = new CreateOrUpdateCustomerDto(
                    identity.GetSub(),
                    identity.Name ?? "",
                    identity.FindFirst(y => y.Type.Contains("givenname"))?.Value ?? "",
                    identity.FindFirst(y => y.Type.Contains("surname"))?.Value ?? "");

                await customerService.CreateOrUpdateCustomerAsync(dto);
            }
        };
    });
}
else
{
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie();
}

// ── Authorization: global require-authenticated fallback policy ──
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// ── MVC ──
builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// ── Local auth middleware (development mode without Cognito) ──
if (builder.Configuration["Services:Authentication"] != "aws")
{
    app.UseMiddleware<LocalAuthenticationMiddleware>();
}

app.MapControllerRoute(
    name: "admin",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

// ── Helper: Configure NLog programmatically ──
static void ConfigureNLog()
{
    var config = new LoggingConfiguration();

    NLog.Targets.Target loggingTarget;

    if (BookstoreConfiguration.GetSetting("Services:LoggingService") == "aws")
    {
        loggingTarget = new AWSTarget { LogGroup = Constants.AppName };
    }
    else
    {
        loggingTarget = new DebuggerTarget();
    }

    config.AddTarget("aws", loggingTarget);
    config.LoggingRules.Add(new LoggingRule("*", NLog.LogLevel.Info, loggingTarget));

    LogManager.Configuration = config;
}

// ── Helper: Load AWS SSM parameters into BookstoreConfiguration ──
static async Task ConfigureAwsParametersAsync(IConfiguration configuration)
{
    var rootPath = "/" + Constants.AppName;
    const string databasePath = "/Database";
    const string authenticationPath = "/Authentication";
    const string fileServicePath = "/Files";

    if (configuration["Services:Database"] == "aws")
    {
        using var client = new AmazonSimpleSystemsManagementClient();
        var request = new GetParameterRequest { Name = $"{rootPath}{databasePath}/ConnectionStrings/BookstoreDatabaseConnection" };
        var response = await client.GetParameterAsync(request);
        BookstoreConfiguration.AddSetting(
            response.Parameter.Name.Replace($"{rootPath}{databasePath}/", string.Empty),
            response.Parameter.Value);
    }

    if (configuration["Services:Authentication"] == "aws")
    {
        using var client = new AmazonSimpleSystemsManagementClient();
        var request = new GetParametersByPathRequest { Path = $"{rootPath}{authenticationPath}/", Recursive = true };
        var response = await client.GetParametersByPathAsync(request);
        foreach (var parameter in response.Parameters)
        {
            BookstoreConfiguration.AddSetting(
                parameter.Name.Replace($"{rootPath}/", string.Empty),
                parameter.Value);
        }
    }

    if (configuration["Services:FileService"] == "aws")
    {
        using var client = new AmazonSimpleSystemsManagementClient();
        var request = new GetParametersByPathRequest { Path = $"{rootPath}{fileServicePath}/", Recursive = true };
        var response = await client.GetParametersByPathAsync(request);
        foreach (var parameter in response.Parameters)
        {
            BookstoreConfiguration.AddSetting(
                parameter.Name.Replace($"{rootPath}/", string.Empty),
                parameter.Value);
        }
    }
}
