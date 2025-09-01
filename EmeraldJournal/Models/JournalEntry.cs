using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace EmeraldJournal.Models;

public class JournalEntry {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = default!; 

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [DataType(DataType.Date)]
        public DateTime Date { get; set; } = DateTime.UtcNow.Date;

        [Required]
        public string Text { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }