using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.RecentlyAddedBadges.Services;

/// <summary>
/// Injects the plugin's small client overlay into jellyfin-web's app shell.
/// This does not alter any files in Jellyfin's web directory.
/// </summary>
public sealed class WebScriptStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(InvokeAsync);
            next(app);
        };
    }

    private static async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        if (!IsIndexRequest(context.Request.Path.Value) || !HttpMethods.IsGet(context.Request.Method))
        {
            await next().ConfigureAwait(false);
            return;
        }

        context.Request.Headers.Remove("Accept-Encoding");
        context.Request.Headers.Remove("Range");
        context.Request.Headers.Remove("If-Range");

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next().ConfigureAwait(false);
        }
        catch
        {
            context.Response.Body = originalBody;
            throw;
        }

        context.Response.Body = originalBody;
        buffer.Position = 0;
        if (context.Response.StatusCode != StatusCodes.Status200OK
            || !(context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
            return;
        }

        string html;
        using (var reader = new StreamReader(buffer, Encoding.UTF8, true, 1024, leaveOpen: true))
            html = await reader.ReadToEndAsync().ConfigureAwait(false);

        if (html.Contains("JellyfinRecentlyAddedBadges", StringComparison.Ordinal))
        {
            await originalBody.WriteAsync(Encoding.UTF8.GetBytes(html)).ConfigureAwait(false);
            return;
        }

        var script = ReadScript();
        var close = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (close >= 0)
            html = html.Insert(close, "<script>\n" + script + "\n</script>\n");

        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html;charset=utf-8";
        context.Response.ContentLength = bytes.Length;
        context.Response.Headers.Remove("ETag");
        context.Response.Headers.Remove("Last-Modified");
        context.Response.Headers.Remove("Accept-Ranges");
        await originalBody.WriteAsync(bytes).ConfigureAwait(false);
    }

    private static string ReadScript()
    {
        using var stream = typeof(WebScriptStartupFilter).Assembly
            .GetManifestResourceStream("Jellyfin.Plugin.RecentlyAddedBadges.wwwroot.recently-added-badges.js");
        if (stream == null) return "/* Recently Added Badges script resource missing. */";
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static bool IsIndexRequest(string? path)
        => !string.IsNullOrEmpty(path)
            && (path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/web", StringComparison.OrdinalIgnoreCase));
}
