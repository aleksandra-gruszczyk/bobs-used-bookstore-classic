using BobsBookstoreClassic.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;

namespace Bookstore.Web.Controllers
{
    public class AuthenticationController : Controller
    {
        public IActionResult Login(string redirectUri = null)
        {
            if (string.IsNullOrWhiteSpace(redirectUri)) return RedirectToAction("Index", "Home");

            return Redirect(redirectUri);
        }

        public IActionResult LogOut()
        {
            return BookstoreConfiguration.GetSetting("Services/Authentication") == "aws"
                ? CognitoSignOut()
                : LocalSignOut();
        }

        private IActionResult LocalSignOut()
        {
            Response.Cookies.Delete("LocalAuthentication");

            return RedirectToAction("Index", "Home");
        }

        private IActionResult CognitoSignOut()
        {
            Response.Cookies.Delete(".AspNet.Cookies");

            var domain = BookstoreConfiguration.GetSetting("Authentication/Cognito/CognitoDomain");
            var clientId = BookstoreConfiguration.GetSetting("Authentication/Cognito/LocalClientId");
            var logoutUri = $"{Request.Scheme}://{Request.Host}/";

            return Redirect($"{domain}/logout?client_id={clientId}&logout_uri={logoutUri}");
        }
    }
}
