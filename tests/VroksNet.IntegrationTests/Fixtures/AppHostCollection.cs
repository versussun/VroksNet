namespace VroksNet.IntegrationTests.Fixtures;

/// <summary>
/// Every test class in this collection shares one <see cref="AppHostFixture"/> instance and runs
/// sequentially against it (xUnit never parallelizes tests within the same collection) — required
/// here since they all hit the same real ApiService process and its single-writer SQLite queue.
/// </summary>
[CollectionDefinition("AppHost")]
public sealed class AppHostCollection : ICollectionFixture<AppHostFixture>;
