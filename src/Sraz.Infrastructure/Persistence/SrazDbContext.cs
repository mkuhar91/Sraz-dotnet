using Microsoft.EntityFrameworkCore;

namespace Sraz.Infrastructure.Persistence;

public sealed class SrazDbContext(DbContextOptions<SrazDbContext> options) : DbContext(options)
{
    public DbSet<QuestionEntity> Questions => Set<QuestionEntity>();
    public DbSet<LeaderboardEntryEntity> LeaderboardEntries => Set<LeaderboardEntryEntity>();
    public DbSet<GameRoomEntity> GameRooms => Set<GameRoomEntity>();
    public DbSet<GameSnapshotEntity> GameSnapshots => Set<GameSnapshotEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<QuestionEntity>(entity =>
        {
            entity.HasKey(question => question.Id);
            entity.Property(question => question.Category).IsRequired();
            entity.Property(question => question.Text).IsRequired();
            entity.Property(question => question.OptionsJson).HasColumnType("jsonb").IsRequired();
            entity.Property(question => question.Explanation).IsRequired();
            entity.HasIndex(question => new { question.Category, question.Text }).IsUnique();
        });

        modelBuilder.Entity<LeaderboardEntryEntity>(entity =>
        {
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.PlayerName).IsRequired();
            entity.Property(entry => entry.Category).IsRequired();
            entity.Property(entry => entry.Difficulty).IsRequired();
            entity.Property(entry => entry.Mode).IsRequired();
        });

        modelBuilder.Entity<GameRoomEntity>(entity =>
        {
            entity.HasKey(room => room.Code);
            entity.Property(room => room.Code).HasMaxLength(8);
            entity.Property(room => room.State).IsRequired();
            entity.Property(room => room.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<GameSnapshotEntity>(entity =>
        {
            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.StateJson).HasColumnType("jsonb").IsRequired();
            entity.Property(snapshot => snapshot.SchemaVersion).IsRequired();
            entity.Property(snapshot => snapshot.CreatedAtUtc).IsRequired();
            entity.HasOne<GameRoomEntity>()
                .WithMany()
                .HasForeignKey(snapshot => snapshot.RoomCode)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
