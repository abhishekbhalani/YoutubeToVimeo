using MovoVideoMigration.Models;

namespace MovoVideoMigration.Services;

public class VideoMigrationService
{
    private readonly DatabaseService _databaseService;
    private readonly VimeoService _vimeoService;
    private readonly string _videosFolder;
    private readonly string _thumbnailsFolder;

    public VideoMigrationService(
        DatabaseService databaseService,
        VimeoService vimeoService,
        string videosFolder,
        string thumbnailsFolder)
    {
        _databaseService = databaseService;
        _vimeoService = vimeoService;
        _videosFolder = videosFolder;
        _thumbnailsFolder = thumbnailsFolder;
    }

    public async Task ScanVideosAsync()
    {
        if (!Directory.Exists(_videosFolder))
        {
            Console.WriteLine($"Videos folder '{_videosFolder}' does not exist. Creating it...");
            Directory.CreateDirectory(_videosFolder);
            return;
        }

        var videoExtensions = new[] { ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".flv", ".webm" };
        var videoFiles = Directory.GetFiles(_videosFolder)
            .Where(f => videoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();

        Console.WriteLine($"Found {videoFiles.Count} video file(s) in '{_videosFolder}'");

        foreach (var videoFile in videoFiles)
        {
            var filename = Path.GetFileName(videoFile);
            if (!_databaseService.RecordExists(filename))
            {
                var record = new UploadRecord
                {
                    VideoFilename = filename,
                    Status = "pending",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                // Try to find matching thumbnail
                var thumbnailPath = Path.Combine(_thumbnailsFolder, Path.ChangeExtension(filename, ".jpg"));
                if (File.Exists(thumbnailPath))
                {
                    record.ThumbnailFilename = Path.GetFileName(thumbnailPath);
                }

                _databaseService.InsertOrUpdateRecord(record);
                Console.WriteLine($"Added '{filename}' to upload queue");
            }
        }
    }

    public async Task ProcessVideosAsync()
    {
        // Ensure folder exists on Vimeo (may return null if Projects API is not available)
        var folderUri = await _vimeoService.EnsureFolderExistsAsync();
        if (!string.IsNullOrEmpty(folderUri))
        {
            Console.WriteLine($"Using Vimeo folder: {folderUri}");
        }
        else
        {
            Console.WriteLine("Note: Videos will be uploaded without folder organization (Projects API not available)");
        }

        var records = _databaseService.GetPendingOrFailedRecords();
        Console.WriteLine($"Found {records.Count} video(s) to process");

        foreach (var record in records)
        {
            Console.WriteLine($"\nProcessing: {record.VideoFilename}");

            try
            {
                var videoPath = Path.Combine(_videosFolder, record.VideoFilename);
                if (!File.Exists(videoPath))
                {
                    throw new FileNotFoundException($"Video file not found: {videoPath}");
                }

                // Check for thumbnail
                string? thumbnailPath = null;
                if (!string.IsNullOrEmpty(record.ThumbnailFilename))
                {
                    thumbnailPath = Path.Combine(_thumbnailsFolder, record.ThumbnailFilename);
                    if (!File.Exists(thumbnailPath))
                    {
                        // Try to find thumbnail with same name
                        thumbnailPath = Path.Combine(_thumbnailsFolder, Path.ChangeExtension(record.VideoFilename, ".jpg"));
                        if (!File.Exists(thumbnailPath))
                        {
                            Console.WriteLine($"Warning: Thumbnail not found for {record.VideoFilename}, proceeding without thumbnail");
                            thumbnailPath = null;
                        }
                    }
                }
                else
                {
                    // Try to find thumbnail with same name
                    thumbnailPath = Path.Combine(_thumbnailsFolder, Path.ChangeExtension(record.VideoFilename, ".jpg"));
                    if (!File.Exists(thumbnailPath))
                    {
                        Console.WriteLine($"Warning: Thumbnail not found for {record.VideoFilename}, proceeding without thumbnail");
                        thumbnailPath = null;
                    }
                }

                // Upload video
                Console.WriteLine($"Uploading video to Vimeo...");
                var videoUri = await _vimeoService.UploadVideoAsync(videoPath, folderUri);
                var videoId = videoUri.Split('/').Last();
                Console.WriteLine($"Video uploaded successfully! Video ID: {videoId}");
                
                // Upload thumbnail immediately after upload (custom images don't require video processing)
                // This saves time - we don't need to wait for video processing to complete
                if (!string.IsNullOrEmpty(thumbnailPath) && File.Exists(thumbnailPath))
                {
                    Console.WriteLine($"Setting thumbnail (can be done immediately, no need to wait for processing)...");
                    try
                    {
                        await _vimeoService.SetThumbnailAsync(videoUri, thumbnailPath);
                        Console.WriteLine($"✓ Thumbnail set successfully!");
                    }
                    catch (Exception thumbEx)
                    {
                        Console.WriteLine($"⚠ Warning: Failed to set thumbnail: {thumbEx.Message}");
                        Console.WriteLine($"  Video uploaded successfully. Thumbnail may need to be set manually.");
                        // Don't fail the whole upload if thumbnail fails
                        // Note: If thumbnail fails, you can retry later when video processing completes
                    }
                }
                
                // Note: Video processing continues in background on Vimeo
                // The video will be available for viewing once processing completes
                // Custom thumbnails work immediately, but video playback may take a few minutes

                // Update record as uploaded
                _databaseService.UpdateRecordStatus(record.Id, "uploaded", videoId, videoUri);
                Console.WriteLine($"✓ Successfully processed: {record.VideoFilename}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error processing {record.VideoFilename}: {ex.Message}");
                _databaseService.UpdateRecordStatus(record.Id, "failed", null, null, ex.Message);
            }
        }

        Console.WriteLine("\nProcessing complete!");
    }
}
