namespace VroksNet.Domain.MockEndpoints;

/// <summary>
/// Whether two HTTP operation keys ("METHOD /path/{param}") could both match the same request:
/// same method and same number of path segments, with every segment pair either equal or at least
/// one of them a parameter. "GET /pets/{id}" overlaps "GET /pets/mine" (a request for /pets/mine
/// matches both) but not "GET /pets/{id}/toys". Provider mode refuses overlapping operations so
/// any request on the provider port maps to exactly one of them.
/// </summary>
public static class OperationOverlap
{
    public static bool Overlaps(string operationKey, string otherOperationKey)
    {
        if (!TrySplit(operationKey, out var method, out var segments) || !TrySplit(otherOperationKey, out var otherMethod, out var otherSegments))
        {
            return false;
        }

        if (!string.Equals(method, otherMethod, StringComparison.OrdinalIgnoreCase) || segments.Length != otherSegments.Length)
        {
            return false;
        }

        for (var i = 0; i < segments.Length; i++)
        {
            if (!IsParameter(segments[i]) && !IsParameter(otherSegments[i]) && !string.Equals(segments[i], otherSegments[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TrySplit(string operationKey, out string method, out string[] segments)
    {
        var separatorIndex = operationKey.IndexOf(' ');
        if (separatorIndex < 0)
        {
            method = string.Empty;
            segments = [];
            return false;
        }

        method = operationKey[..separatorIndex];
        segments = operationKey[(separatorIndex + 1)..].Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return true;
    }

    private static bool IsParameter(string segment) => segment.StartsWith('{') && segment.EndsWith('}');
}
