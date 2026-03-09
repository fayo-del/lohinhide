using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.PrivateBranding;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.PrivateBranding;

// ─── Configuration ────────────────────────────────────────────────────────────

public class PluginConfiguration : BasePluginConfiguration
{
    public bool Enabled { get; set; } = true;
}

// ─── Plugin ───────────────────────────────────────────────────────────────────

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public override string Name => "Private Branding";
    public override Guid Id => Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    public override string Description => "Hides custom branding from unauthenticated users.";
    public static Plugin? Instance { get; private set; }
    public IEnumerable<PluginPageInfo> GetPages() => [];
}

// ─── Filter ───────────────────────────────────────────────────────────────────

/// <summary>
/// MVC resource filter that intercepts GET /Branding/Configuration before Jellyfin processes it.
/// For unauthenticated requests, short-circuits with empty branding JSON.
/// </summary>
public class PrivateBrandingFilter : IAsyncResourceFilter
{
    private static readonly byte[] EmptyBranding = Encoding.UTF8.GetBytes(
        """{"LoginDisclaimer":null,"CustomCss":null,"SplashscreenEnabled":false}"""
    );

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;

        bool isBrandingEndpoint =
            request.Method == HttpMethods.Get &&
            request.Path.StartsWithSegments("/Branding/Configuration", StringComparison.OrdinalIgnoreCase);

        if (isBrandingEndpoint && Plugin.Instance?.Configuration.Enabled == true && !IsAuthenticated(request))
        {
            var response = context.HttpContext.Response;
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = "application/json; charset=utf-8";
            await response.Body.WriteAsync(EmptyBranding);
            // Short-circuit — do NOT call next()
            return;
        }

        await next();
    }

    private static bool IsAuthenticated(HttpRequest request)
    {
        foreach (var header in new[] { "Authorization", "X-Emby-Authorization" })
        {
            if (request.Headers.TryGetValue(header, out var val))
            {
                var auth = val.ToString();
                if (auth.Contains("Token=", StringComparison.OrdinalIgnoreCase)
                    && !auth.Contains("Token=\"\"", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return request.Query.TryGetValue("ApiKey", out var key)
            && !string.IsNullOrWhiteSpace(key);
    }
}

// ─── Registration ─────────────────────────────────────────────────────────────

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Register the filter globally so it runs before every controller action
        serviceCollection.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(options =>
        {
            options.Filters.Add<PrivateBrandingFilter>();
        });
    }
}
