using System.ComponentModel.DataAnnotations;

namespace DataIntegration.Infrastructure.Sync;

public class SyncOptions
{
    public const string SectionName = "Sync";

    /// <summary>
    /// Off by default, deliberately.
    /// </summary>
    /// <remarks>
    /// The App Service free tier allows 60 CPU-minutes per day; a loop polling an
    /// 8.5 MB feed would exhaust it and the site would start returning 403. It is
    /// also rude to poll a volunteer-funded upstream from an idle demo. Turning
    /// this on should be a decision, not a default.
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>
    /// Sources to sync each cycle, in order. Empty means the live feed only.
    /// </summary>
    public IList<string> Sources { get; } = [];

    /// <summary>
    /// The live feed publishes a 5-minute window, so polling faster than that
    /// re-fetches the same readings; the upsert absorbs them, but the bandwidth
    /// is wasted.
    /// </summary>
    [Range(1, 1_440)]
    public int IntervalMinutes { get; set; } = 10;

    /// <summary>
    /// Delay before the first cycle, so startup is not competing with the first
    /// real requests and a crash-looping instance cannot hammer the upstream.
    /// </summary>
    [Range(0, 600)]
    public int StartupDelaySeconds { get; set; } = 30;
}
