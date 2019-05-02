﻿using Microsoft.Identity.Client;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.OpenIdConnect;
using System;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using WebApp.Utils;

namespace WebApp.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        [Authorize]
        public ActionResult About()
        {
            ViewBag.Name = ClaimsPrincipal.Current.FindFirst("name").Value;
            ViewBag.AuthorizationRequest = string.Empty;

            // The object ID claim will only be emitted for work or school accounts at this time.
            Claim oid = ClaimsPrincipal.Current.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier");
            ViewBag.ObjectId = oid == null ? string.Empty : oid.Value;

            // The 'preferred_username' claim can be used for showing the user's primary way of identifying themselves
            ViewBag.Username = ClaimsPrincipal.Current.FindFirst("preferred_username").Value;

            // The subject or nameidentifier claim can be used to uniquely identify the user
            ViewBag.Subject = ClaimsPrincipal.Current.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier").Value;
            return View();
        }

        [Authorize]
        public async Task<ActionResult> SendMail()
        {
            // Before we render the send email screen, we use the incremental consent to obtain and cache the aceess token with the correct scopes
            IConfidentialClientApplication app = MsalAppBuilder.BuildConfidentialClientApplication();
            AuthenticationResult result = null;
            var accounts = await app.GetAccountsAsync();
            string[] scopes = { "Mail.Send" };

            try
            {
                // try to get token silently
                result = await app.AcquireTokenSilent(scopes, accounts.FirstOrDefault()).ExecuteAsync().ConfigureAwait(false);
            }
            catch (MsalUiRequiredException ex)
            {
                // A MsalUiRequiredException happened on AcquireTokenSilentAsync.
                // This indicates you need to call AcquireTokenAsync to acquire a token
                Debug.WriteLine($"MsalUiRequiredException: {ex.Message}");

                try
                {
                    // Build the auth code request Uri
                    string authReqUrl = await OAuth2RequestManager.GenerateAuthorizationRequestUrl(scopes, app, this.HttpContext, Url);
                    ViewBag.AuthorizationRequest = authReqUrl;
                    ViewBag.Relogin = "true";
                }
                catch (MsalException msalex)
                {
                    Response.Write($"Error Acquiring Token:{System.Environment.NewLine}{msalex}");
                }
            }
            catch (Exception ex)
            {
                Response.Write($"Error Acquiring Token Silently:{System.Environment.NewLine}{ex}");
            }

            return View();
        }

        [Authorize]
        [HttpPost]
        public async Task<ActionResult> SendMail(string recipient, string subject, string body)
        {
            string messagetemplate = @"{{
  ""Message"": {{
    ""Subject"": ""{0}"",
    ""Body"": {{
                ""ContentType"": ""Text"",
      ""Content"": ""{1}""
    }},
    ""ToRecipients"": [
      {{
        ""EmailAddress"": {{
          ""Address"": ""{2}""
        }}
}}
    ],
    ""Attachments"": []
  }},
  ""SaveToSentItems"": ""false""
}}
";
            string message = String.Format(messagetemplate, subject, body, recipient);

            HttpClient client = new HttpClient();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/microsoft.graph.sendMail")
            {
                Content = new StringContent(message, Encoding.UTF8, "application/json")
            };


            IConfidentialClientApplication app = MsalAppBuilder.BuildConfidentialClientApplication();
            AuthenticationResult result = null;
            var accounts = await app.GetAccountsAsync();
            string[] scopes = { "Mail.Send" };

            try
            {
                // try to get token silently
                result = await app.AcquireTokenSilent(scopes, accounts.FirstOrDefault()).ExecuteAsync().ConfigureAwait(false);
            }
            catch (Exception eee)
            {
                ViewBag.Error = "An error has occurred acquiring the token from cache. Details: " + eee.Message;
                return View();
            }

            if (result != null)
            {
                // Use the token to send email

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
                HttpResponseMessage response = await client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    ViewBag.AuthorizationRequest = null;
                    return View("MailSent");
                }
            }


            return View();
        }

        public async Task<ActionResult> ReadMail()
        {
            IConfidentialClientApplication app = MsalAppBuilder.BuildConfidentialClientApplication();
            AuthenticationResult result = null;
            var accounts = await app.GetAccountsAsync();
            string[] scopes = { "Mail.Read" };

            try
            {
                // try to get token silently
                result = await app.AcquireTokenSilent(scopes, accounts.FirstOrDefault()).ExecuteAsync().ConfigureAwait(false);
            }
            catch (MsalUiRequiredException)
            {
                ViewBag.Relogin = "true";
                return View();
            }
            catch (Exception eee)
            {
                ViewBag.Error = "An error has occurred. Details: " + eee.Message;
                return View();
            }

            if (result != null)
            {
                // Use the token to read email
                HttpClient hc = new HttpClient();
                hc.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", result.AccessToken);
                HttpResponseMessage hrm = await hc.GetAsync("https://graph.microsoft.com/v1.0/me/messages");

                string rez = await hrm.Content.ReadAsStringAsync();
                ViewBag.Message = rez;
            }

            return View();
        }

        public void RefreshSession()
        {
            HttpContext.GetOwinContext().Authentication.Challenge(
                new AuthenticationProperties { RedirectUri = "/Home/ReadMail" },
                OpenIdConnectAuthenticationDefaults.AuthenticationType);
        }
    }
}