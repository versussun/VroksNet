namespace VroksNet.E2ETests.Fixtures;

/// <summary>
/// Every test class in this collection shares one <see cref="AppHostFixture"/> instance — one
/// booted app graph and one browser — and runs sequentially against it (xUnit never parallelizes
/// tests within the same collection).
/// </summary>
[CollectionDefinition("AppHost")]
public sealed class AppHostCollection : ICollectionFixture<AppHostFixture>;
