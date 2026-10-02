using Mediator;

namespace VroksNet.Application.Publishers.SetPublisherEnabled;

/// <summary>Starts or stops publishing on the schedule. Result is false if no publisher with that id exists.</summary>
public sealed record SetPublisherEnabled(Guid Id, bool Enabled) : IRequest<bool>;
