namespace MovoVideoMigration.Models;

public class UploadRecord
{
    public int Id { get; set; }
    public string VideoFilename { get; set; } = string.Empty;
    public string? ThumbnailFilename { get; set; }
    public string? VimeoVideoId { get; set; }
    public string? VimeoVideoUri { get; set; }
    public string Status { get; set; } = "pending"; // pending, uploaded, failed
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
