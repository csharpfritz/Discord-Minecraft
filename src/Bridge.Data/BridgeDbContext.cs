using Bridge.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bridge.Data;

public class BridgeDbContext(DbContextOptions<BridgeDbContext> options) : DbContext(options)
{
    public DbSet<ChannelGroup> ChannelGroups => Set<ChannelGroup>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<WorldState> WorldStates => Set<WorldState>();
    public DbSet<GenerationJob> GenerationJobs => Set<GenerationJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChannelGroup>(entity =>
        {
            entity.ToTable("channel_groups");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DiscordId).IsUnique();
            entity.HasIndex(e => new { e.CenterX, e.CenterZ }).IsUnique();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.DiscordId).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<Channel>(entity =>
        {
            entity.ToTable("channels");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DiscordId).IsUnique();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.DiscordId).HasMaxLength(64).IsRequired();

            entity.HasOne(e => e.ChannelGroup)
                .WithMany(g => g.Channels)
                .HasForeignKey(e => e.ChannelGroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Player>(entity =>
        {
            entity.ToTable("players");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DiscordId).IsUnique();
            entity.HasIndex(e => e.MinecraftUuid).IsUnique()
                .HasFilter("\"MinecraftUuid\" IS NOT NULL");
            entity.Property(e => e.DiscordId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.MinecraftUuid).HasMaxLength(36);
            entity.Property(e => e.MinecraftUsername).HasMaxLength(16);
        });

        modelBuilder.Entity<WorldState>(entity =>
        {
            entity.ToTable("world_state");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Key).IsUnique();
            entity.Property(e => e.Key).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<GenerationJob>(entity =>
        {
            entity.ToTable("generation_jobs");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Status);
            entity.Property(e => e.Type).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Payload).IsRequired();
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
        });
    }
}
