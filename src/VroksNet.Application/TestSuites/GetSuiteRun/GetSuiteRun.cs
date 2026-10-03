using Mediator;

namespace VroksNet.Application.TestSuites.GetSuiteRun;

public sealed record GetSuiteRun(Guid Id) : IRequest<SuiteRunDetails?>;
