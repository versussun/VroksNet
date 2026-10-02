using VroksNet.Domain.MockEndpoints;

namespace VroksNet.Domain.ApiSpecifications;

/// <summary>
/// What <see cref="ApiSpecification.ApplyReimport"/> changed: the operations it added and removed
/// (persistence must insert/delete exactly these), and whether anything at all changed — the
/// specification's own fields or any kept operation's content.
/// </summary>
public sealed record ReimportChanges(
    IReadOnlyList<MockEndpoint> Added,
    IReadOnlyList<MockEndpoint> Removed,
    bool AnyChange);
