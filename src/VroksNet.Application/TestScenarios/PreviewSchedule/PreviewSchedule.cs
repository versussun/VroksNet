using Mediator;

namespace VroksNet.Application.TestScenarios.PreviewSchedule;

/// <summary>The next <see cref="Count"/> run times of a schedule, or why it's invalid — so the form shows a mistyped expression before it's saved.</summary>
public sealed record PreviewSchedule(string? Schedule, string? TimeZone, int Count = 5) : IRequest<SchedulePreview>;
