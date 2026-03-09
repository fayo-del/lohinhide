using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Jellyfin.Plugin.PrivateBranding;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Branding;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

// ─── Action Filter ────────────────────────────────────────────────────────────

/// <summary>
/// Action filter that runs before Jellyfin executes the BrandingController action.
/// Short-circuits with empty branding for unauthenticated requests.
/// </summary>
public class PrivateBrandingFilter : IAsyncActionFilter
{
    private static readonly BrandingOptions EmptyBranding = new()
    {
        LoginDisclaimer = null,
        CustomCss = null,
        SplashscreenEnabled = false
    };

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;

        bool isBrandingGet =
            request.Method == HttpMethods.Get &&
            request.Path.StartsWithSegments("/Branding/Configuration", StringComparison.OrdinalIgnoreCase);

        if (isBrandingGet
            && Plugin.Instance?.Configuration.Enabled == true
            && !IsAuthenticated(request))
        {
            // Short-circuit: return empty branding, skip the real controller action
            context.Result = new OkObjectResult(EmptyBranding);
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
        serviceCollection.Configure<MvcOptions>(options =>
        {
            options.Filters.Add<PrivateBrandingFilter>(int.MinValue); // highest priority
        });
    }
}
