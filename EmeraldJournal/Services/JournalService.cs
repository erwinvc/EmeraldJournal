using Microsoft.EntityFrameworkCore;
using EmeraldJournal.Data;
using EmeraldJournal.Models;
using MudBlazor;
using System.Security.Claims;
using System.Text.Json;

namespace EmeraldJournal.Services;

public class JournalService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public JournalService(ApplicationDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    private async Task<IQueryable<JournalEntry>> UserQueryAsync()
    {
        var uid = await _user.GetUserIdAsync();
        if (string.IsNullOrEmpty(uid)) return Enumerable.Empty<JournalEntry>().AsQueryable();
        return _db.JournalEntries.Where(e => e.UserId == uid);
    }

    public async Task<List<JournalEntry>> GetEntriesAsync(DateTime? from = null, DateTime? to = null)
    {
        var q = await UserQueryAsync();
        if (from.HasValue) q = q.Where(e => e.Date >= from.Value.Date);
        if (to.HasValue) q = q.Where(e => e.Date <= to.Value.Date);
        return await q.OrderByDescending(e => e.Date)
                      .ThenByDescending(e => e.UpdatedAtUtc)
                      .ToListAsync();
    }

    public async Task<(IReadOnlyList<JournalEntry> Items, int Total)>
    GetEntriesAsync(int page, int pageSize, bool newestFirst,
                    DateTime? from = null, DateTime? to = null,
                    CancellationToken ct = default)
{
    if (page <= 0) page = 1;
    if (pageSize <= 0) pageSize = 20;

    var q = (await UserQueryAsync()).AsNoTracking();

    if (from.HasValue) q = q.Where(e => e.Date >= from.Value.Date);
    if (to.HasValue)   q = q.Where(e => e.Date <= to.Value.Date);

    var total = await q.CountAsync(ct);

    q = newestFirst
        ? q.OrderByDescending(e => e.Date).ThenByDescending(e => e.UpdatedAtUtc)
        : q.OrderBy(e => e.Date).ThenBy(e => e.UpdatedAtUtc);

    var items = await q.Skip((page - 1) * pageSize)
                       .Take(pageSize)
                       .ToListAsync(ct);

    return (items, total);
}

    public async Task<JournalEntry?> GetAsync(int id)
    {
        var q = await UserQueryAsync();
        return await q.FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<JournalEntry> CreateAsync(string title, DateTime date, string text)
    {
        var uid = await _user.GetUserIdAsync() ?? throw new InvalidOperationException("No user");
        var entry = new JournalEntry
        {
            UserId = uid,
            Title = title.Trim(),
            Date = date.Date,
            Text = text.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _db.JournalEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    public async Task UpdateAsync(int id, string title, DateTime date, string text)
    {
        var e = await GetAsync(id);
        if (e is null) return;
        e.Title = title.Trim(); e.Date = date.Date; e.Text = text.Trim();
        e.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var e = await GetAsync(id);
        if (e is null) return;
        _db.JournalEntries.Remove(e);
        await _db.SaveChangesAsync();
    }

    public async Task<(IReadOnlyList<JournalEntry> Items, int Total)> SearchAsync(
        string? query, DateTime? from = null, DateTime? to = null,
        int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        var q = (await UserQueryAsync()).AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var p = $"%{query.Trim()}%";
            q = q.Where(e => EF.Functions.Like(e.Title, p) || EF.Functions.Like(e.Text, p));
        }
        if (from.HasValue) q = q.Where(e => e.Date >= from.Value.Date);
        if (to.HasValue) q = q.Where(e => e.Date <= to.Value.Date);

        var total = await q.CountAsync(ct);

        var items = await q.OrderByDescending(e => e.Date)
                           .ThenByDescending(e => e.UpdatedAtUtc)
                           .Skip((page - 1) * pageSize)
                           .Take(pageSize)
                           .ToListAsync(ct);

        return (items, total);
    }

    public async Task<Dictionary<DateTime, int>> GetCountsForMonthAsync(int year, int month)
    {
        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var q = await UserQueryAsync();
        return await q.Where(e => e.Date >= start && e.Date <= end)
                      .GroupBy(e => e.Date)
                      .Select(g => new { g.Key, Count = g.Count() })
                      .ToDictionaryAsync(x => x.Key, x => x.Count);
    }

    public enum RestoreMode { Merge, ReplaceAll }

    public class RestoreReport
    {
        public int Added { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public int Deleted { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public async Task<RestoreReport> RestoreAsync(object payload, RestoreMode mode)
    {
        // deserialize again to use service-side DTOs (or reuse the page DTOs if shared)
        var json = JsonSerializer.Serialize(payload);
        var data = JsonSerializer.Deserialize<BackupPayload>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw new InvalidOperationException("Invalid backup payload.");

        var uid = await _user.GetUserIdAsync() ?? throw new InvalidOperationException("No user");
        var report = new RestoreReport();

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // Optionally validate app/version
            // if (!string.Equals(data.App, "EmeraldJournal", StringComparison.OrdinalIgnoreCase)) ...

            var userQuery = _db.JournalEntries.Where(e => e.UserId == uid);

            if (mode == RestoreMode.ReplaceAll)
            {
                var old = await userQuery.ToListAsync();
                _db.JournalEntries.RemoveRange(old);
                report.Deleted = old.Count;
                await _db.SaveChangesAsync();
            }

            foreach (var b in data.Entries)
            {
                try
                {
                    var title = (b.Title ?? string.Empty).Trim();
                    var text = (b.Text ?? string.Empty);

                    // Try matching by Id first (if present)
                    JournalEntry? existing = null;
                    if (b.Id.HasValue)
                        existing = await userQuery.FirstOrDefaultAsync(e => e.Id == b.Id.Value);

                    // Otherwise try a "natural key": Date + Title
                    if (existing is null)
                        existing = await userQuery.FirstOrDefaultAsync(e => e.Date == b.Date.Date && e.Title == title);

                    if (existing is null)
                    {
                        _db.JournalEntries.Add(new JournalEntry
                        {
                            UserId = uid,
                            Title = title,
                            Date = b.Date.Date,
                            Text = text,
                            CreatedAtUtc = b.CreatedAtUtc ?? DateTime.UtcNow,
                            UpdatedAtUtc = b.UpdatedAtUtc ?? DateTime.UtcNow
                        });
                        report.Added++;
                    }
                    else
                    {
                        // Update if content differs
                        if (existing.Title != title || existing.Text != text || existing.Date != b.Date.Date)
                        {
                            existing.Title = title;
                            existing.Text = text;
                            existing.Date = b.Date.Date;
                            existing.UpdatedAtUtc = DateTime.UtcNow;
                            report.Updated++;
                        }
                        else report.Skipped++;
                    }
                }
                catch (Exception ex)
                {
                    report.Errors.Add(ex.Message);
                }
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return report;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // You can move these to a shared file if you prefer
    public class BackupPayload
    {
        public string App { get; set; } = "EmeraldJournal";
        public int Version { get; set; } = 1;
        public DateTime ExportedAtUtc { get; set; }
        public List<BackupEntry> Entries { get; set; } = new();
    }
    public class BackupEntry
    {
        public int? Id { get; set; }
        public string? Title { get; set; }
        public DateTime Date { get; set; }
        public string? Text { get; set; }
        public DateTime? CreatedAtUtc { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
    }
}