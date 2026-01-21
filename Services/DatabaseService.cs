using Microsoft.Data.Sqlite;
using MovoVideoMigration.Models;

namespace MovoVideoMigration.Services;

public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(string databasePath)
    {
        _connectionString = $"Data Source={databasePath}";
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var createTableCommand = connection.CreateCommand();
        createTableCommand.CommandText = @"
            CREATE TABLE IF NOT EXISTS uploads (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                video_filename TEXT NOT NULL UNIQUE,
                thumbnail_filename TEXT,
                vimeo_video_id TEXT,
                vimeo_video_uri TEXT,
                status TEXT NOT NULL DEFAULT 'pending',
                error_message TEXT,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS idx_status ON uploads(status);
        ";

        createTableCommand.ExecuteNonQuery();
    }

    public void InsertOrUpdateRecord(UploadRecord record)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO uploads (video_filename, thumbnail_filename, status, created_at, updated_at)
            VALUES (@videoFilename, @thumbnailFilename, @status, @createdAt, @updatedAt)
            ON CONFLICT(video_filename) DO UPDATE SET
                thumbnail_filename = @thumbnailFilename,
                status = @status,
                updated_at = @updatedAt
        ";

        command.Parameters.AddWithValue("@videoFilename", record.VideoFilename);
        command.Parameters.AddWithValue("@thumbnailFilename", (object?)record.ThumbnailFilename ?? DBNull.Value);
        command.Parameters.AddWithValue("@status", record.Status);
        command.Parameters.AddWithValue("@createdAt", record.CreatedAt);
        command.Parameters.AddWithValue("@updatedAt", record.UpdatedAt);

        command.ExecuteNonQuery();
    }

    public void UpdateRecordStatus(int id, string status, string? vimeoVideoId = null, string? vimeoVideoUri = null, string? errorMessage = null)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE uploads 
            SET status = @status,
                vimeo_video_id = @vimeoVideoId,
                vimeo_video_uri = @vimeoVideoUri,
                error_message = @errorMessage,
                updated_at = @updatedAt
            WHERE id = @id
        ";

        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@vimeoVideoId", (object?)vimeoVideoId ?? DBNull.Value);
        command.Parameters.AddWithValue("@vimeoVideoUri", (object?)vimeoVideoUri ?? DBNull.Value);
        command.Parameters.AddWithValue("@errorMessage", (object?)errorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);

        command.ExecuteNonQuery();
    }

    public List<UploadRecord> GetPendingOrFailedRecords()
    {
        var records = new List<UploadRecord>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, video_filename, thumbnail_filename, vimeo_video_id, vimeo_video_uri, 
                   status, error_message, created_at, updated_at
            FROM uploads
            WHERE status IN ('pending', 'failed')
            ORDER BY created_at ASC
        ";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            records.Add(new UploadRecord
            {
                Id = reader.GetInt32(0),
                VideoFilename = reader.GetString(1),
                ThumbnailFilename = reader.IsDBNull(2) ? null : reader.GetString(2),
                VimeoVideoId = reader.IsDBNull(3) ? null : reader.GetString(3),
                VimeoVideoUri = reader.IsDBNull(4) ? null : reader.GetString(4),
                Status = reader.GetString(5),
                ErrorMessage = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt = reader.GetDateTime(7),
                UpdatedAt = reader.GetDateTime(8)
            });
        }

        return records;
    }

    public bool RecordExists(string videoFilename)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM uploads WHERE video_filename = @videoFilename";
        command.Parameters.AddWithValue("@videoFilename", videoFilename);

        var count = Convert.ToInt32(command.ExecuteScalar());
        return count > 0;
    }
}
