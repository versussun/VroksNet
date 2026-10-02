using Mediator;

namespace VroksNet.Application.CallRecords.ClearCallRecords;

/// <summary>Deletes the entire call history; the result is how many records were deleted.</summary>
public sealed record ClearCallRecords : IRequest<int>;
