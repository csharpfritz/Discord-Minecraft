using Acceptance.Tests.Infrastructure;

namespace Acceptance.Tests;

/// <summary>
/// Collection definition that ensures all tests sharing FullStackFixture
/// run serially (one Minecraft instance at a time).
/// </summary>
[CollectionDefinition("FullStack")]
public sealed class FullStackCollection : ICollectionFixture<FullStackFixture>
{
    // This class is never instantiated — it just defines the collection.
}
