namespace VroksNet.Infrastructure.Brokers;

/// <summary>How far a Listen got — so a timeout says which part timed out (<see cref="BrokerListening.TimeoutMessage"/>).</summary>
internal enum ListenStage
{
    Connecting,
    SettingUp,
    Listening
}
