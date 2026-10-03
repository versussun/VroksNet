namespace VroksNet.Domain.TestScenarios;

/// <summary>One segment of a <see cref="ChannelPattern"/>: literal text, or a whole AsyncAPI parameter ("{region}") that matches any one segment.</summary>
public readonly record struct ChannelSegment(string Text, bool IsParameter);
