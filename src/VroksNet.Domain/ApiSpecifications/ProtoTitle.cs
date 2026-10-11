namespace VroksNet.Domain.ApiSpecifications;

/// <summary>
/// The title (the re-import key, <see cref="ApiSpecification.Title"/>) of a specification made from
/// a .proto file (ADR 0004): the file's <c>package</c>, then its services' names sorted ordinally
/// in parentheses — <c>shop.orders.v1 (Orders, Payments)</c> — so reordering services in the file
/// keeps the title, while adding or removing one makes a new specification. A file without a
/// package uses its file name without the extension instead.
/// </summary>
public static class ProtoTitle
{
    public static string Of(string? package, IEnumerable<string> serviceNames, string filePath)
    {
        var services = string.Join(", ", serviceNames.Order(StringComparer.Ordinal));
        return $"{PrefixOf(package, filePath)} ({services})";
    }

    /// <summary>
    /// What a title made by <see cref="Of"/> starts with — the package, or the file name — so an
    /// import can tell that a specification it doesn't replace came from the same package. Null for
    /// a title not made by <see cref="Of"/>.
    /// </summary>
    public static string? PrefixOf(string title)
    {
        var index = title.IndexOf(" (", StringComparison.Ordinal);
        return index > 0 && title.EndsWith(')') ? title[..index] : null;
    }

    private static string PrefixOf(string? package, string filePath)
        => string.IsNullOrWhiteSpace(package)
            ? Path.GetFileNameWithoutExtension(filePath.Replace('\\', '/').Split('/')[^1])
            : package;
}
