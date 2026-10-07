using Bookstore.Domain.Customers;
using Microsoft.AspNetCore.Http;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Bookstore.Web.Helpers
{
    public class LocalAuthenticationMiddleware
    {
        private const string UserId = "FB6135C7-1464-4A72-B74E-4B63D343DD09";
        private const string LocalAuthCookieName = "LocalAuthentication";

        private readonly RequestDelegate _next;

        public LocalAuthenticationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, ICustomerService customerService)
        {
            if (context.Request.Path.Value.StartsWith("/Authentication/Login", StringComparison.OrdinalIgnoreCase))
            {
                SetClaimsPrincipal(context);

                await SaveCustomerDetailsAsync(context, customerService);

                context.Response.Cookies.Append(LocalAuthCookieName, "1", new CookieOptions
                {
                    Expires = DateTimeOffset.Now.AddDays(1)
                });

                context.Response.Redirect("/");
                return;
            }
            else if (context.Request.Cookies.ContainsKey(LocalAuthCookieName))
            {
                SetClaimsPrincipal(context);

                await SaveCustomerDetailsAsync(context, customerService);
            }

            await _next(context);
        }

        private static void SetClaimsPrincipal(HttpContext context)
        {
            var identity = new ClaimsIdentity("Application");

            identity.AddClaim(new Claim(ClaimTypes.Name, "bookstoreuser"));
            identity.AddClaim(new Claim("nameidentifier", UserId));
            identity.AddClaim(new Claim("given_name", "Bookstore"));
            identity.AddClaim(new Claim("family_name", "User"));
            identity.AddClaim(new Claim(ClaimTypes.Role, "Administrators"));

            context.User = new ClaimsPrincipal(identity);
        }

        private static async Task SaveCustomerDetailsAsync(HttpContext context, ICustomerService customerService)
        {
            var identity = (ClaimsIdentity)context.User.Identity;

            var dto = new CreateOrUpdateCustomerDto(
                identity.FindFirst("nameidentifier")?.Value,
                identity.Name,
                identity.FindFirst("given_name")?.Value,
                identity.FindFirst("family_name")?.Value);

            await customerService.CreateOrUpdateCustomerAsync(dto);
        }
    }
}
