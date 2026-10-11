namespace VroksNet.Application.Abstractions;

/// <summary>
/// The given specification files don't parse. It's an <see cref="InvalidOperationException"/>, as
/// the other parsers throw, so the import endpoints answer 400 with its message; provisioning
/// reports each of <see cref="Errors"/> against its own file.
/// </summary>
public sealed class SpecificationParseException(IReadOnlyList<SpecificationParseError> errors)
    : InvalidOperationException(string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<SpecificationParseError> Errors { get; } = errors;
}
