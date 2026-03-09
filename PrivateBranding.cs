using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PrivateBranding;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// ─── Plugin Entry Point ───────────────────────────────────────────────────────

namespace Jellyfin.Plugin.PrivateBranding;

public class PluginConfiguration : BasePluginConfiguration
{
    public bool HideBrandingForUnauthenticated { get; set; } = true;
}

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

// ─── Middleware ───────────────────────────────────────────────────────────────

public class PrivateBrandingMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly byte[] EmptyBranding = Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new
        {
            LoginDisclaimer = (string?)null,
            CustomCss = (string?)null,
            SplashscreenEnabled = false
        })
    );

    public PrivateBrandingMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Method == HttpMethods.Get
            && context.Request.Path.StartsWithSegments("/Branding/Configuration", StringComparison.OrdinalIgnoreCase)
            && Plugin.Instance?.Configuration.HideBrandingForUnauthenticated == true
            && !IsAuthenticated(context))
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.Body.WriteAsync(EmptyBranding);
            return;
        }

        await _next(context);
    }

    private static bool IsAuthenticated(HttpContext context)
    {
        foreach (var header in new[] { "Authorization", "X-Emby-Authorization" })
        {
            if (context.Request.Headers.TryGetValue(header, out var value))
            {
                var auth = value.ToString();
                if (auth.Contains("Token=", StringComparison.OrdinalIgnoreCase)
                    && !auth.Contains("Token=\"\"", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return context.Request.Query.TryGetValue("ApiKey", out var key)
            && !string.IsNullOrWhiteSpace(key);
    }
}

// ─── Registration ─────────────────────────────────────────────────────────────

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHostedService<PrivateBrandingStartup>();
    }
}

public class PrivateBrandingStartup : IHostedService
{
    private readonly IApplicationBuilder _appBuilder;

    public PrivateBrandingStartup(IApplicationBuilder appBuilder)
        => _appBuilder = appBuilder;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _appBuilder.UseMiddleware<PrivateBrandingMiddleware>();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
