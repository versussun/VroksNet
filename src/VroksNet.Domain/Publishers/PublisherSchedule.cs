namespace VroksNet.Domain.Publishers;

/// <summary>When a <see cref="Publisher"/> publishes on its own.</summary>
public static class PublisherSchedule
{
    /// <summary>The background worker checks once a second, so nothing finer is honored.</summary>
    public const int MinIntervalSeconds = 1;

    public const int MaxIntervalSeconds = 24 * 60 * 60;

    /// <summary>
    /// An enabled publisher is due once its interval has passed since its last publish (or right
    /// away if it never published). A failed publish counts too, so a broken one retries once per
    /// interval rather than every second.
    /// </summary>
    public static bool IsDue(Publisher publisher, DateTimeOffset now)
        => publisher.IsEnabled
            && (publisher.LastPublishedAt is not { } last || now - last >= TimeSpan.FromSeconds(publisher.IntervalSeconds));
}
