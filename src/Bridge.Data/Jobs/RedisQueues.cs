namespace Bridge.Data.Jobs;

/// <summary>
/// Well-known Redis list keys used for job queuing.
/// </summary>
public static class RedisQueues
{
    public const string WorldGen = "queue:worldgen";
}
