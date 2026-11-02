using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.RecentlyAddedBadges.Configuration;

/// <summary>
/// Persistent settings for Recently Added Badges.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    public PluginConfiguration()
    {
        EnabledLibraryIds = [];
        BadgeBackgroundColor = "#E53935";
        BadgePosition = "top-left";
        BadgeDurationDays = 7;
    }

    /// <summary>
    /// Jellyfin library folder IDs for which the badge is enabled.
    /// The dashboard page will populate these from the server's libraries.
    /// </summary>
    public List<string> EnabledLibraryIds { get; set; }

    /// <summary>
    /// CSS-style hex colour used as the badge background.
    /// </summary>
    public string BadgeBackgroundColor { get; set; }

    /// <summary>
    /// Poster corner: top-left, top-right, bottom-left, or bottom-right.
    /// </summary>
    public string BadgePosition { get; set; }

    /// <summary>
    /// Number of days after DateCreated that an item remains marked New.
    /// </summary>
    public int BadgeDurationDays { get; set; }
}
