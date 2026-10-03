using Mediator;

namespace VroksNet.Application.TestRuns.InterruptRunningTestRuns;

/// <summary>Sent once at startup: every run still Running was cut off by the previous shutdown, so it becomes Interrupted. Result is how many.</summary>
public sealed record InterruptRunningTestRuns : IRequest<int>;
