namespace VroksNet.Application;

/// <summary>
/// The naming rule for test scenarios, Publishers and connections: a name is required, stored
/// trimmed, and unique within its kind (a unique index backs it). Names are how provisioning
/// (ADR 0001) refers to these objects, so two of one kind can't share one.
/// </summary>
internal static class UniqueNames
{
    /// <summary>The name as it's stored: trimmed. Throws <see cref="ArgumentException"/> when it's blank.</summary>
    /// <param name="kind">Lower-case, for messages: "test scenario", "publisher", "connection".</param>
    public static string Normalize(string? name, string kind)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException($"A {kind} needs a name.");
        }

        return name.Trim();
    }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when <paramref name="holderId"/> — the id of whatever
    /// already has the name, if anything — is someone other than <paramref name="selfId"/> (null
    /// when creating).
    /// </summary>
    public static void EnsureFree(Guid? holderId, Guid? selfId, string kind, string name)
    {
        if (holderId is { } holder && holder != selfId)
        {
            throw new ArgumentException($"A {kind} named \"{name}\" already exists.");
        }
    }
}
