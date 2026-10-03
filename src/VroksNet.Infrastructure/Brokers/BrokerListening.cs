namespace VroksNet.Infrastructure.Brokers;

/// <summary>What every <see cref="IListeningBrokerAdapter"/> shares: the setup budget and its timeout messages.</summary>
internal static class BrokerListening
{
    /// <summary>For reaching the broker and setting up the subscription — separate from (and not counted against) the listen timeout.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    public static string TimeoutMessage(ListenStage stage, string target, TimeSpan timeout) => stage switch
    {
        ListenStage.Connecting => $"Timed out connecting to the broker after {ConnectTimeout.TotalSeconds:0}s.",
        ListenStage.SettingUp => $"Connected, but setting up the subscription on {target} timed out after {ConnectTimeout.TotalSeconds:0}s.",
        _ => $"No message on {target} within {timeout.TotalSeconds:0}s."
    };
}
