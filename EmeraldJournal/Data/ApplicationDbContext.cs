using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using EmeraldJournal.Models;
using System.Reflection.Emit;

namespace EmeraldJournal.Data;

public class ApplicationDbContext : IdentityDbContext {
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) {
    }

    public DbSet<JournalEntry> JournalEntries { get; set; }

    protected override void OnModelCreating(ModelBuilder builder) {
        base.OnModelCreating(builder);

        builder.Entity<JournalEntry>(e => {
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Text).IsRequired();
            e.HasIndex(x => new { x.UserId, x.Date });
        });
    }
}