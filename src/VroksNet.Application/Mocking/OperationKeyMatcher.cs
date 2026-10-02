namespace VroksNet.Application.Mocking;

/// <summary>
/// Matches an incoming HTTP method + path against a stored operation key (e.g.
/// "GET /pets/{petId}", the format <c>ISpecificationParser</c> produces) — segments wrapped in
/// curly braces match any single path segment.
/// </summary>
public static class OperationKeyMatcher
{
    public static bool Matches(string operationKey, string method, string path) => TryMatch(operationKey, method, path, out _);

    /// <summary>Like <see cref="Matches"/>, also returning the values the request gave each <c>{name}</c> segment (e.g. petId → "1").</summary>
    public static bool TryMatch(string operationKey, string method, string path, out IReadOnlyDictionary<string, string> pathParameters)
    {
        pathParameters = new Dictionary<string, string>();

        var separatorIndex = operationKey.IndexOf(' ');
        if (separatorIndex < 0)
        {
            return false;
        }

        var keyMethod = operationKey[..separatorIndex];
        var keyPath = operationKey[(separatorIndex + 1)..];

        if (!string.Equals(keyMethod, method, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var keySegments = keyPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var pathSegments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (keySegments.Length != pathSegments.Length)
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < keySegments.Length; i++)
        {
            var isPlaceholder = keySegments[i].StartsWith('{') && keySegments[i].EndsWith('}');
            if (isPlaceholder)
            {
                values[keySegments[i][1..^1]] = pathSegments[i];
            }
            else if (!string.Equals(keySegments[i], pathSegments[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        pathParameters = values;
        return true;
    }
}
