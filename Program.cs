using Microsoft.Extensions.Configuration;
using MovoVideoMigration.Services;

namespace MovoVideoMigration;

internal class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Movo Video Migration Tool ===");
        Console.WriteLine();

        // Load configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        var vimeoAccessToken = configuration["Vimeo:AccessToken"];
        var folderName = configuration["Vimeo:FolderName"] ?? "Movo Academy";
        var videosFolder = configuration["Paths:VideosFolder"] ?? "videos";
        var thumbnailsFolder = configuration["Paths:ThumbnailsFolder"] ?? "thumbnails";
        var databasePath = configuration["Paths:DatabasePath"] ?? "uploads.db";

        // Validate configuration
        if (string.IsNullOrEmpty(vimeoAccessToken) || vimeoAccessToken == "YOUR_VIMEO_ACCESS_TOKEN_HERE")
        {
            Console.WriteLine("ERROR: Please configure your Vimeo Access Token in appsettings.json");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
            return;
        }

        // Ensure thumbnails folder exists
        if (!Directory.Exists(thumbnailsFolder))
        {
            Console.WriteLine($"Thumbnails folder '{thumbnailsFolder}' does not exist. Creating it...");
            Directory.CreateDirectory(thumbnailsFolder);
        }

        // Initialize services
        var databaseService = new DatabaseService(databasePath);
        using var vimeoService = new VimeoService(vimeoAccessToken, folderName);
        var migrationService = new VideoMigrationService(
            databaseService,
            vimeoService,
            videosFolder,
            thumbnailsFolder
        );

        try
        {
            // Step 1: Scan for videos
            Console.WriteLine("Step 1: Scanning for videos...");
            await migrationService.ScanVideosAsync();
            Console.WriteLine();

            // Step 2: Process videos
            Console.WriteLine("Step 2: Processing videos...");
            await migrationService.ProcessVideosAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nFatal error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}
