using Mediator;

namespace VroksNet.Application.TestSuites.StartStartupSuiteRuns;

/// <summary>
/// Sent once at startup, after provisioning (if any) has been applied: queues a run of every suite
/// with <c>RunOnStartup</c>, as <c>Trigger = Startup</c>. Result is how many. They don't hold up
/// readiness — "ready" means the mocks are loaded, not that the tests passed (ADR 0002).
/// </summary>
public sealed record StartStartupSuiteRuns : IRequest<int>;
