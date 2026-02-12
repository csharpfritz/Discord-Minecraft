using Bridge.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Bridge.Api.Tests.Infrastructure;

/// <summary>
/// Shared test fixture that spins up a Redis container and configures
/// Bridge.Api with SQLite in-memory + real Redis.
/// </summary>
public sealed class BridgeApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    // Keep this connection open for the lifetime of the fixture so the
    // shared in-memory SQLite database doesn't get destroyed.
    private SqliteConnection _keepAliveConnection = null!;
    private string _sqliteConnectionString = null!;

    public IConnectionMultiplexer Redis { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        Redis = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());

        _sqliteConnectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAliveConnection = new SqliteConnection(_sqliteConnectionString);
        await _keepAliveConnection.OpenAsync();

        // Create the schema before the host starts
        var options = new DbContextOptionsBuilder<BridgeDbContext>()
            .UseSqlite(_sqliteConnectionString)
            .Options;
        await using var db = new BridgeDbContext(options);
        await db.Database.EnsureCreatedAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Aspire's AddNpgsqlDbContext and AddRedisClient validate connection strings
        // at registration time. Provide placeholders so validation passes.
        builder.UseSetting("ConnectionStrings:bridgedb",
            "Host=localhost;Database=fake;Username=fake;Password=fake");
        builder.UseSetting("ConnectionStrings:redis", _redis.GetConnectionString());

        builder.ConfigureServices(services =>
        {
            // Remove ALL EF Core and Npgsql-related service registrations
            // so we can cleanly re-register with SQLite
            var toRemove = services
                .Where(d =>
                {
                    var st = d.ServiceType.FullName ?? "";
                    var it = d.ImplementationType?.FullName ?? "";
                    return st.Contains("EntityFramework", StringComparison.OrdinalIgnoreCase)
                        || st.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
                        || st.Contains("DbContext", StringComparison.OrdinalIgnoreCase)
                        || it.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
                        || it.Contains("EntityFramework", StringComparison.OrdinalIgnoreCase)
                        || d.ServiceType == typeof(BridgeDbContext);
                })
                .ToList();

            foreach (var d in toRemove)
                services.Remove(d);

            // Re-register with SQLite in-memory
            services.AddDbContext<BridgeDbContext>(options =>
                options.UseSqlite(_sqliteConnectionString));

            // Replace the IConnectionMultiplexer with the Testcontainers instance
            services.RemoveAll<IConnectionMultiplexer>();
            services.AddSingleton(Redis);
        });
    }

    /// <summary>
    /// Creates a fresh BridgeDbContext pointing at the same SQLite database.
    /// </summary>
    public BridgeDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<BridgeDbContext>()
            .UseSqlite(_sqliteConnectionString)
            .Options;
        return new BridgeDbContext(options);
    }

    public new async Task DisposeAsync()
    {
        Redis?.Dispose();
        await _keepAliveConnection.DisposeAsync();
        await _redis.DisposeAsync();
        await base.DisposeAsync();
    }
}
