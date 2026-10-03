namespace VroksNet.Application.Abstractions;

/// <summary>Where a page of the run history ended: the last run's (ScheduledFor, Id) keyset position.</summary>
public sealed record TestRunCursor(DateTimeOffset ScheduledFor, Guid Id);
