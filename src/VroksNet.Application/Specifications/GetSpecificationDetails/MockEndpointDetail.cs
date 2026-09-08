namespace VroksNet.Application.Specifications.GetSpecificationDetails;

/// <summary>One endpoint within a <see cref="SpecificationDetails"/> view.</summary>
public sealed record MockEndpointDetail(Guid Id, string OperationKey, bool IsEnabled, string? ExampleTemplate);
