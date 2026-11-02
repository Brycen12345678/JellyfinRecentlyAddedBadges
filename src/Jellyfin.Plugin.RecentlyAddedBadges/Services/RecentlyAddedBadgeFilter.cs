using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.RecentlyAddedBadges.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using SkiaSharp;

namespace Jellyfin.Plugin.RecentlyAddedBadges.Services;

/// <summary>
/// Adds a cache-version marker to native-client item DTOs and composites the
/// New badge onto Primary poster responses. Original media artwork is never
/// written to disk or modified.
/// </summary>
public sealed class RecentlyAddedBadgeFilter : IAsyncActionFilter
{
    private const string Marker = "-rab";
    private const string NoStore = "private, no-store, max-age=0, must-revalidate";
    private static readonly HashSet<string> WebClients = new(StringComparer.OrdinalIgnoreCase)
    {
        "Jellyfin Web",
        "Jellyfin Media Player",
        "Jellium Desktop",
        "Jellyfin Desktop",
        "Jellyfin for WebOS",
        "Jellyfin for Android",
        "Jellyfin iOS",
        "Jellyfin iPadOS"
    };

    private readonly ILibraryManager _libraryManager;

    public RecentlyAddedBadgeFilter(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (IsImageAction(context))
        {
            if (!context.ActionArguments.TryGetValue("imageType", out var imageType)
                || !string.Equals(imageType?.ToString(), nameof(ImageType.Primary), StringComparison.OrdinalIgnoreCase))
            {
                return next();
            }

            var tag = context.ActionArguments.TryGetValue("tag", out var rawTag) ? rawTag as string : null;
            if (!HasMarker(tag)) return next();
            return RenderImageAsync(context, next);
        }

        // DTO tags are only decorated for native clients. Web clients use the
        // injected DOM overlay so their badge can participate in JE's flex stack.
        if (!IsNativeClient(context.HttpContext)) return next();
        return StampDtoResponseAsync(context, next);
    }

    private async Task StampDtoResponseAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next().ConfigureAwait(false);
        if (executed.Exception != null || executed.Canceled || executed.Result is not ObjectResult { Value: not null } result)
            return;

        var config = Plugin.Instance?.Configuration;
        if (config == null || config.EnabledLibraryIds == null || config.EnabledLibraryIds.Count == 0) return;

        var enabled = ParseLibraryIds(config.EnabledLibraryIds);
        if (enabled.Count == 0) return;

        var revision = GetRevision(config);
        switch (result.Value)
        {
            case BaseItemDto item:
                Stamp(item, enabled, config, revision);
                break;
            case QueryResult<BaseItemDto> query:
                if (query.Items != null)
                    foreach (var dto in query.Items) Stamp(dto, enabled, config, revision);
                break;
            case IEnumerable<BaseItemDto> items:
                foreach (var dto in items) Stamp(dto, enabled, config, revision);
                break;
        }
    }

    private void Stamp(BaseItemDto dto, HashSet<Guid> enabled, PluginConfiguration config, string revision)
    {
        if (dto.Type is not (BaseItemKind.Movie or BaseItemKind.Series)
            || dto.Id == Guid.Empty
            || dto.DateCreated is not { } added
            || !IsRecent(added, config.BadgeDurationDays)
            || !IsInEnabledLibrary(dto.ParentId, enabled))
            return;

        if (dto.ImageTags == null || !dto.ImageTags.TryGetValue(ImageType.Primary, out var tag) || string.IsNullOrWhiteSpace(tag))
            return;

        var token = CreateItemToken(dto.Id, added, revision, tag);
        var decorated = StripMarker(tag) + Marker + token;
        if (string.Equals(tag, decorated, StringComparison.Ordinal)) return;

        dto.ImageTags[ImageType.Primary] = decorated;
        ReKeyBlurhash(dto, tag, decorated);
    }

    private bool IsInEnabledLibrary(Guid? parentId, HashSet<Guid> enabled)
    {
        if (parentId is not { } id || id == Guid.Empty) return false;
        var visited = new HashSet<Guid>();
        for (var depth = 0; depth < 12 && id != Guid.Empty && visited.Add(id); depth++)
        {
            if (enabled.Contains(id)) return true;
            var parent = _libraryManager.GetItemById<BaseItem>(id);
            if (parent == null) break;
            id = parent.ParentId;
        }
        return false;
    }

    private async Task RenderImageAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (!context.ActionArguments.TryGetValue("itemId", out var rawId)
            || rawId is not Guid itemId
            || itemId == Guid.Empty)
        {
            await OriginalNoStoreAsync(http, next).ConfigureAwait(false);
            return;
        }

        var config = Plugin.Instance?.Configuration;
        var item = _libraryManager.GetItemById<BaseItem>(itemId);
        if (config == null || item == null || !IsEligible(item, config))
        {
            await OriginalNoStoreAsync(http, next).ConfigureAwait(false);
            return;
        }

        var suppliedTag = context.ActionArguments.TryGetValue("tag", out var imageTag) ? imageTag as string : null;
        var currentRevision = GetRevision(config);
        var expectedToken = CreateItemToken(item.Id, item.DateCreated, currentRevision, StripMarker(suppliedTag ?? string.Empty));
        if (!string.Equals(GetMarkerToken(suppliedTag), expectedToken, StringComparison.Ordinal))
        {
            await OriginalNoStoreAsync(http, next).ConfigureAwait(false);
            return;
        }

        var executed = await next().ConfigureAwait(false);
        if (executed.Exception != null || executed.Canceled || http.Response.HasStarted) return;
        http.Response.Headers[HeaderNames.CacheControl] = NoStore;
        http.Response.Headers.Remove(HeaderNames.ETag);
        http.Response.Headers.Remove(HeaderNames.LastModified);

        if (http.Response.StatusCode == StatusCodes.Status304NotModified) return;

        try
        {
            var source = await ReadImageAsync(executed.Result).ConfigureAwait(false);
            if (source == null) return;

            using var bitmap = SKBitmap.Decode(source);
            if (bitmap == null || bitmap.Width < 32 || bitmap.Height < 32 || bitmap.Width > 4096 || bitmap.Height > 4096)
                return;

            DrawBadge(bitmap, config);
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            if (encoded == null) return;

            executed.Result = new FileContentResult(encoded.ToArray(), "image/png");
            http.Response.Headers[HeaderNames.CacheControl] = NoStore;
            http.Response.Headers.Remove(HeaderNames.ETag);
            http.Response.Headers.Remove(HeaderNames.LastModified);
            http.Response.Headers.Remove(HeaderNames.Expires);
        }
        catch (Exception)
        {
            // Poster decoration is additive: a drawing problem must never make
            // the original image unavailable.
            http.Response.Headers[HeaderNames.CacheControl] = NoStore;
        }
    }

    private bool IsEligible(BaseItem item, PluginConfiguration config)
    {
        if (config.EnabledLibraryIds == null || item.GetBaseItemKind() is not (BaseItemKind.Movie or BaseItemKind.Series)
            || !IsRecent(item.DateCreated, config.BadgeDurationDays))
            return false;
        return IsInEnabledLibrary(item.ParentId, ParseLibraryIds(config.EnabledLibraryIds));
    }

    private static async Task<byte[]?> ReadImageAsync(IActionResult? result)
    {
        switch (result)
        {
            case FileContentResult content:
                return content.FileContents;
            case FileStreamResult streamResult:
            {
                await using var output = new MemoryStream();
                await streamResult.FileStream.CopyToAsync(output).ConfigureAwait(false);
                return output.ToArray();
            }
            case PhysicalFileResult physical:
                return await File.ReadAllBytesAsync(physical.FileName).ConfigureAwait(false);
            default:
                return null;
        }
    }

    private static void DrawBadge(SKBitmap bitmap, PluginConfiguration config)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var textSize = Math.Clamp(width * 0.055f, 14f, 40f);
        var horizontalPadding = textSize * 0.55f;
        var verticalPadding = textSize * 0.28f;
        var margin = Math.Max(8f, width * 0.035f);

        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White,
            TextSize = textSize,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };
        var textWidth = textPaint.MeasureText("New");
        var badgeWidth = textWidth + horizontalPadding * 2;
        var badgeHeight = textSize + verticalPadding * 2;
        var position = string.IsNullOrWhiteSpace(config.BadgePosition) ? "top-left" : config.BadgePosition;
        var x = position.EndsWith("right", StringComparison.OrdinalIgnoreCase)
            ? width - margin - badgeWidth : margin;
        var y = position.StartsWith("bottom", StringComparison.OrdinalIgnoreCase)
            ? height - margin - badgeHeight : margin;

        SKColor background;
        try { background = SKColor.Parse(string.IsNullOrWhiteSpace(config.BadgeBackgroundColor) ? "#E53935" : config.BadgeBackgroundColor); }
        catch { background = new SKColor(229, 57, 53); }

        using var canvas = new SKCanvas(bitmap);
        using var backgroundPaint = new SKPaint { IsAntialias = true, Color = background, Style = SKPaintStyle.Fill };
        var rect = new SKRect(x, y, x + badgeWidth, y + badgeHeight);
        canvas.DrawRoundRect(rect, badgeHeight * 0.22f, badgeHeight * 0.22f, backgroundPaint);

        var metrics = textPaint.FontMetrics;
        var baseline = y + badgeHeight / 2f - (metrics.Ascent + metrics.Descent) / 2f;
        canvas.DrawText("New", x + horizontalPadding, baseline, textPaint);
        canvas.Flush();
    }

    private static bool IsRecent(DateTime added, int days)
    {
        if (days < 1) return false;
        var utc = added.Kind == DateTimeKind.Utc ? added : added.ToUniversalTime();
        var age = DateTime.UtcNow - utc;
        return age >= TimeSpan.Zero && age < TimeSpan.FromDays(Math.Clamp(days, 1, 365));
    }

    private static HashSet<Guid> ParseLibraryIds(IEnumerable<string> values)
        => values.Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();

    private static string GetRevision(PluginConfiguration config)
    {
        var raw = string.Join("|", config.BadgeBackgroundColor, config.BadgePosition,
            config.BadgeDurationDays.ToString(CultureInfo.InvariantCulture),
            string.Join(",", (config.EnabledLibraryIds ?? []).OrderBy(x => x, StringComparer.Ordinal)),
            DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant()[..16];
    }

    private static string CreateItemToken(Guid id, DateTime added, string revision, string baseTag)
    {
        var raw = id.ToString("N") + "|" + added.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + revision + "|" + baseTag;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant()[..16];
    }

    private static bool HasMarker(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return false;
        var index = tag.LastIndexOf(Marker, StringComparison.Ordinal);
        return index >= 0 && tag.Length - index == Marker.Length + 16
            && tag.AsSpan(index + Marker.Length).ToString().All(Uri.IsHexDigit);
    }

    private static string? GetMarkerToken(string? tag)
    {
        if (!HasMarker(tag)) return null;
        return tag![(tag.LastIndexOf(Marker, StringComparison.Ordinal) + Marker.Length)..];
    }

    private static string StripMarker(string tag)
    {
        var index = tag.LastIndexOf(Marker, StringComparison.Ordinal);
        return index >= 0 && tag.Length - index == Marker.Length + 16 ? tag[..index] : tag;
    }

    private static void ReKeyBlurhash(BaseItemDto dto, string oldTag, string newTag)
    {
        if (dto.ImageBlurHashes == null
            || !dto.ImageBlurHashes.TryGetValue(ImageType.Primary, out var byTag)
            || byTag == null) return;
        if (byTag.Remove(oldTag, out var hash)) byTag[newTag] = hash;
    }

    private static bool IsNativeClient(HttpContext context)
    {
        var client = context.User.Claims.FirstOrDefault(c => string.Equals(c.Type, "Jellyfin-Client", StringComparison.OrdinalIgnoreCase))?.Value;
        return !string.IsNullOrWhiteSpace(client) && !WebClients.Contains(client.Trim());
    }

    private static bool IsImageAction(ActionExecutingContext context)
    {
        var controller = context.ActionDescriptor.RouteValues.TryGetValue("controller", out var c) ? c : null;
        var action = context.ActionDescriptor.RouteValues.TryGetValue("action", out var a) ? a : null;
        return string.Equals(controller, "Image", StringComparison.OrdinalIgnoreCase)
            && action is not null
            && (action.Equals("GetItemImage", StringComparison.OrdinalIgnoreCase)
                || action.Equals("GetItemImageByIndex", StringComparison.OrdinalIgnoreCase)
                || action.Equals("GetItemImage2", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task OriginalNoStoreAsync(HttpContext http, ActionExecutionDelegate next)
    {
        http.Request.Headers.Remove(HeaderNames.IfNoneMatch);
        http.Request.Headers.Remove(HeaderNames.IfModifiedSince);
        var executed = await next().ConfigureAwait(false);
        if (executed.Exception == null && !executed.Canceled)
            http.Response.Headers[HeaderNames.CacheControl] = NoStore;
    }
}
