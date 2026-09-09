using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.Connections;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Application.TestScenarios;

/// <summary>The specification, operation, and connection a <see cref="TestScenario"/> (or a create/update request shaped like one) points at — resolved and validated once by <see cref="TestScenarioTargetResolver"/>.</summary>
public sealed record ResolvedTestScenarioTarget(ApiSpecification Specification, MockEndpoint Endpoint, Connection Connection);
