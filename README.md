# Movo Video Migration Tool

A .NET Console Application that automatically uploads videos from a local folder to Vimeo with thumbnails.

## Features

✅ **Automatic SQLite Database** - Creates and manages upload tracking database  
✅ **Resumable Uploads** - Safe to stop and resume, tracks upload status  
✅ **Thumbnail Matching** - Automatically matches thumbnails from `/thumbnails` folder  
✅ **Vimeo Integration** - Uploads videos to Vimeo and applies thumbnails  
✅ **Folder Organization** - Automatically places videos in "Movo Academy" folder  
✅ **Error Handling** - Tracks failed uploads for retry  

## Setup

### 1. Configure Vimeo Access Token

1. Go to [Vimeo Developer Settings](https://developer.vimeo.com/apps)
2. Create a new app or use an existing one
3. Generate an access token with the following scopes:
   - `public` - Basic access
   - `private` - Access to private videos
   - `upload` - Upload videos
   - `edit` - Edit videos
   - `interact` - Interact with videos

4. Update `appsettings.json` with your access token:

```json
{
  "Vimeo": {
    "AccessToken": "YOUR_ACTUAL_ACCESS_TOKEN_HERE",
    "FolderName": "Movo Academy"
  }
}
```

### 2. Folder Structure

Create the following folder structure in your project directory:

```
MovoVideoMigration/
├── videos/          # Place your video files here
├── thumbnails/      # Place matching .jpg thumbnails here
└── uploads.db       # SQLite database (created automatically)
```

**Important**: Thumbnail files should have the same name as the video file but with `.jpg` extension.

Example:
- Video: `videos/my-video.mp4`
- Thumbnail: `thumbnails/my-video.jpg`

### 3. Build and Run

```bash
dotnet restore
dotnet build
dotnet run
```

## How It Works

1. **Database Initialization**: Creates SQLite database with `uploads` table if it doesn't exist
2. **Video Scanning**: Scans `/videos` folder and adds new videos to the database
3. **Thumbnail Matching**: Matches thumbnails from `/thumbnails` folder (same filename, .jpg extension)
4. **Vimeo Upload**: 
   - Creates "Movo Academy" folder on Vimeo if it doesn't exist
   - Uploads video using Vimeo's TUS (resumable) upload protocol
   - Waits for video processing to complete
5. **Thumbnail Application**: Uploads and sets the thumbnail for the video
6. **Status Tracking**: Updates database with upload status (uploaded/failed)

## Database Schema

The `uploads` table tracks:

- `id` - Primary key
- `video_filename` - Name of the video file
- `thumbnail_filename` - Name of the thumbnail file (if found)
- `vimeo_video_id` - Vimeo video ID after upload
- `vimeo_video_uri` - Vimeo video URI
- `status` - Current status: `pending`, `uploaded`, or `failed`
- `error_message` - Error message if upload failed
- `created_at` - When record was created
- `updated_at` - Last update timestamp

## Resumable Uploads

The application is designed to be safe to stop and resume:

- Videos with `pending` or `failed` status will be retried on next run
- Already `uploaded` videos are skipped
- You can stop the application at any time (Ctrl+C) and resume later

## Supported Video Formats

- MP4
- MOV
- AVI
- MKV
- WMV
- FLV
- WebM

## Configuration

Edit `appsettings.json` to customize:

```json
{
  "Vimeo": {
    "AccessToken": "your_token",
    "FolderName": "Movo Academy"  // Vimeo folder name
  },
  "Paths": {
    "VideosFolder": "videos",           // Local videos folder
    "ThumbnailsFolder": "thumbnails",   // Local thumbnails folder
    "DatabasePath": "uploads.db"        // SQLite database file
  }
}
```

## Troubleshooting

### "Please configure your Vimeo Access Token"
- Make sure you've updated `appsettings.json` with a valid Vimeo access token

### "Video processing timeout"
- Large videos may take longer to process. The app waits up to 5 minutes.
- Check the video on Vimeo manually if needed.

### "Thumbnail not found"
- The app will continue without a thumbnail if one isn't found
- Make sure thumbnail files have the same name as video files with `.jpg` extension

### "Failed to get upload link"
- Check your Vimeo access token has `upload` scope
- Verify your Vimeo account has upload permissions

## Notes

- Videos are uploaded using Vimeo's TUS (resumable) protocol for reliability
- The app automatically creates the "Movo Academy" folder on Vimeo if it doesn't exist
- Thumbnails are optional - videos will upload without them if not found
