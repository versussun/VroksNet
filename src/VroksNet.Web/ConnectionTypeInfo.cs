namespace VroksNet.Web;

/// <summary>
/// A connection type as the server describes it (GET /api/system/connection-types). The UI never
/// lists the types itself, so a type the server adds shows up here without a Web change.
/// </summary>
public sealed record ConnectionTypeInfo(
    string Type,
    string DisplayName,
    string ValueLabel,
    string ValueHint,
    bool IsHttp,
    bool CanListen,
    string? ListenNote);
