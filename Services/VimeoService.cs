using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MovoVideoMigration.Services;

public class VimeoService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _accessToken;
    private readonly string _folderName;
    private string? _folderUri;

    public VimeoService(string accessToken, string folderName)
    {
        _accessToken = accessToken;
        _folderName = folderName;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.vimeo.*+json;version=3.4");
        _httpClient.Timeout = TimeSpan.FromMinutes(30); // Long timeout for large video uploads
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }

    /// <summary>
    /// Verifies the access token and returns user information
    /// </summary>
    public async Task<JObject> VerifyTokenAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("https://api.vimeo.com/oauth/verify");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JObject.Parse(content);
            }
            
            // Fallback to /me endpoint
            response = await _httpClient.GetAsync("https://api.vimeo.com/me");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JObject.Parse(content);
            }
            
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Token verification failed: {response.StatusCode} - {errorContent}");
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to verify access token: {ex.Message}", ex);
        }
    }

    public async Task<string?> EnsureFolderExistsAsync()
    {
        if (!string.IsNullOrEmpty(_folderUri))
            return _folderUri;

        try
        {
            // First, verify token works
            Console.WriteLine("Verifying Vimeo access token...");
            var userInfo = await VerifyTokenAsync();
            var userName = userInfo["name"]?.ToString() ?? "Unknown";
            Console.WriteLine($"✓ Token verified. Connected as: {userName}");

            // Try to get projects - Projects API may not be available for all account types
            Console.WriteLine($"Checking for existing folder '{_folderName}'...");
            
            // Get all projects (folders) - handle pagination
            var allProjects = new List<JObject>();
            var page = 1;
            var perPage = 25;

            while (true)
            {
                var queryParams = new List<string>
                {
                    $"per_page={perPage}",
                    $"page={page}"
                };
                var queryString = string.Join("&", queryParams);
                var requestUrl = $"https://api.vimeo.com/me/projects?{queryString}";
                
                var response = await _httpClient.GetAsync(requestUrl);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    var errorCode = "";
                    try
                    {
                        var errorJson = JObject.Parse(errorContent);
                        errorCode = errorJson["error_code"]?.ToString() ?? "";
                    }
                    catch { }

                    // If it's a 400/403 error, Projects API might not be available
                    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest || 
                        response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        Console.WriteLine($"⚠ Warning: Projects API is not available for your account.");
                        Console.WriteLine($"  This might require a Vimeo Pro/Business account.");
                        Console.WriteLine($"  Videos will be uploaded without folder organization.");
                        Console.WriteLine($"  Error: {response.StatusCode} (Code: {errorCode})");
                        return null; // Return null to indicate folders are not available
                    }
                    
                    // For other errors, throw exception
                    string errorMessage;
                    try
                    {
                        var errorJson = JObject.Parse(errorContent);
                        var errorDetail = errorJson["error"]?.ToString() ?? errorContent;
                        var invalidParams = errorJson["invalid_parameters"]?.ToString();
                        var developerMessage = errorJson["developer_message"]?.ToString();
                        
                        errorMessage = $"Failed to fetch projects from Vimeo: {response.StatusCode}";
                        if (!string.IsNullOrEmpty(errorCode))
                            errorMessage += $" (Error Code: {errorCode})";
                        errorMessage += $" - {errorDetail}";
                        
                        if (!string.IsNullOrEmpty(invalidParams))
                            errorMessage += $"\nInvalid Parameters: {invalidParams}";
                        if (!string.IsNullOrEmpty(developerMessage))
                            errorMessage += $"\nDeveloper Message: {developerMessage}";
                    }
                    catch
                    {
                        errorMessage = $"Failed to fetch projects from Vimeo: {response.StatusCode} - {errorContent}";
                    }
                    throw new Exception(errorMessage);
                }

                var content = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(content);

                var projects = json["data"] as JArray ?? new JArray();
                foreach (var project in projects)
                {
                    allProjects.Add(project as JObject ?? new JObject());
                }

                // Check if there are more pages
                var paging = json["paging"];
                if (paging?["next"] == null)
                    break;

                page++;
            }

            // Search for existing folder
            foreach (var project in allProjects)
            {
                var name = project["name"]?.ToString();
                if (name == _folderName)
                {
                    _folderUri = project["uri"]?.ToString();
                    Console.WriteLine($"✓ Found existing folder: {_folderName}");
                    return _folderUri;
                }
            }

            // Folder doesn't exist, create it
            Console.WriteLine($"Creating folder '{_folderName}' on Vimeo...");
            var createFolderData = new
            {
                name = _folderName
            };

            var createContent = new StringContent(
                JsonConvert.SerializeObject(createFolderData),
                Encoding.UTF8,
                "application/json"
            );

            var createResponse = await _httpClient.PostAsync("https://api.vimeo.com/me/projects", createContent);
            
            if (!createResponse.IsSuccessStatusCode)
            {
                var errorContent = await createResponse.Content.ReadAsStringAsync();
                var errorCode = "";
                try
                {
                    var errorJson = JObject.Parse(errorContent);
                    errorCode = errorJson["error_code"]?.ToString() ?? "";
                }
                catch { }

                // If creation fails with 400/403, Projects API might not be available
                if (createResponse.StatusCode == System.Net.HttpStatusCode.BadRequest || 
                    createResponse.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    Console.WriteLine($"⚠ Warning: Cannot create folder. Projects API may not be available.");
                    Console.WriteLine($"  This might require a Vimeo Pro/Business account.");
                    Console.WriteLine($"  Videos will be uploaded without folder organization.");
                    Console.WriteLine($"  Error: {createResponse.StatusCode} (Code: {errorCode})");
                    return null; // Return null to indicate folders are not available
                }

                string errorMessage;
                try
                {
                    var errorJson = JObject.Parse(errorContent);
                    var errorDetail = errorJson["error"]?.ToString() ?? errorContent;
                    var invalidParams = errorJson["invalid_parameters"]?.ToString();
                    var developerMessage = errorJson["developer_message"]?.ToString();
                    
                    errorMessage = $"Failed to create folder '{_folderName}' on Vimeo: {createResponse.StatusCode}";
                    if (!string.IsNullOrEmpty(errorCode))
                        errorMessage += $" (Error Code: {errorCode})";
                    errorMessage += $" - {errorDetail}";
                    
                    if (!string.IsNullOrEmpty(invalidParams))
                        errorMessage += $"\nInvalid Parameters: {invalidParams}";
                    if (!string.IsNullOrEmpty(developerMessage))
                        errorMessage += $"\nDeveloper Message: {developerMessage}";
                }
                catch
                {
                    errorMessage = $"Failed to create folder '{_folderName}' on Vimeo: {createResponse.StatusCode} - {errorContent}";
                }
                throw new Exception(errorMessage);
            }

            var createResult = await createResponse.Content.ReadAsStringAsync();
            var createJson = JObject.Parse(createResult);
            _folderUri = createJson["uri"]?.ToString();

            if (string.IsNullOrEmpty(_folderUri))
                throw new Exception("Failed to get folder URI after creation");

            Console.WriteLine($"✓ Folder '{_folderName}' created successfully");
            return _folderUri;
        }
        catch (Exception ex) when (ex.Message.Contains("projects") || ex.Message.Contains("2286"))
        {
            // If Projects API is not available, log warning and continue without folders
            Console.WriteLine($"⚠ Warning: Projects API is not available: {ex.Message}");
            Console.WriteLine($"  Videos will be uploaded without folder organization.");
            Console.WriteLine($"  To use folders, ensure your Vimeo account has Projects feature enabled.");
            return null;
        }
    }

    public async Task<string> UploadVideoAsync(string videoPath, string? folderUri = null)
    {
        var fileInfo = new FileInfo(videoPath);
        var fileSize = fileInfo.Length;

        // Step 1: Create upload ticket with TUS approach (for server-side uploads)
        var uploadData = new Dictionary<string, object>
        {
            { "upload", new Dictionary<string, object>
                {
                    { "approach", "tus" },
                    { "size", fileSize }
                }
            },
            { "name", Path.GetFileNameWithoutExtension(videoPath) }
        };

        // Only add folder_uri if folder is available
        if (!string.IsNullOrEmpty(folderUri))
        {
            uploadData["folder_uri"] = folderUri;
        }

        var content = new StringContent(
            JsonConvert.SerializeObject(uploadData),
            Encoding.UTF8,
            "application/json"
        );

        var response = await _httpClient.PostAsync("https://api.vimeo.com/me/videos", content);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to create upload ticket: {response.StatusCode} - {errorContent}");
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        var json = JObject.Parse(responseContent);

        var uploadLink = json["upload"]?["upload_link"]?.ToString();
        var videoUri = json["uri"]?.ToString();
        var uploadApproach = json["upload"]?["approach"]?.ToString();

        if (string.IsNullOrEmpty(videoUri))
            throw new Exception("Failed to get video URI from Vimeo");

        if (uploadApproach != "tus" || string.IsNullOrEmpty(uploadLink))
            throw new Exception($"Unexpected upload approach: {uploadApproach}. Expected 'tus' with upload_link.");

        // Step 2: Upload video file using TUS protocol
        await UploadVideoFileUsingTusAsync(uploadLink, videoPath, fileSize);

        // Step 3: Verify upload is complete and wait for processing
        await WaitForVideoProcessingAsync(videoUri);

        return videoUri;
    }

    private async Task UploadVideoFileUsingTusAsync(string uploadLink, string videoPath, long fileSize)
    {
        const int chunkSize = 5 * 1024 * 1024; // 5 MB chunks
        long offset = 0;

        // Check current upload offset (in case of resume)
        offset = await GetUploadOffsetAsync(uploadLink);

        Console.WriteLine($"Starting upload from offset: {offset} / {fileSize} bytes");

        using var fileStream = new FileStream(videoPath, FileMode.Open, FileAccess.Read, FileShare.Read, chunkSize, useAsync: true);
        
        // Seek to current offset
        if (offset > 0)
        {
            fileStream.Seek(offset, SeekOrigin.Begin);
            Console.WriteLine($"Resuming upload from byte {offset}");
        }

        var buffer = new byte[chunkSize];
        long totalUploaded = offset;

        while (totalUploaded < fileSize)
        {
            // Read chunk
            int bytesRead = await fileStream.ReadAsync(buffer, 0, chunkSize);
            if (bytesRead == 0)
                break;

            // Upload chunk using PATCH
            var chunkContent = new ByteArrayContent(buffer, 0, bytesRead);
            chunkContent.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");

            var request = new HttpRequestMessage(HttpMethod.Patch, uploadLink)
            {
                Content = chunkContent
            };
            
            // Add TUS headers
            request.Headers.Add("Tus-Resumable", "1.0.0");
            request.Headers.Add("Upload-Offset", offset.ToString());

            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to upload chunk at offset {offset}: {response.StatusCode} - {errorContent}");
            }

            // Update offset from response header
            if (response.Headers.TryGetValues("Upload-Offset", out var offsetValues))
            {
                var offsetStr = offsetValues.FirstOrDefault();
                if (long.TryParse(offsetStr, out long newOffset))
                {
                    offset = newOffset;
                    totalUploaded = offset;
                }
                else
                {
                    offset += bytesRead;
                    totalUploaded = offset;
                }
            }
            else
            {
                offset += bytesRead;
                totalUploaded = offset;
            }

            // Show progress
            var progress = (double)totalUploaded / fileSize * 100;
            Console.WriteLine($"Upload progress: {progress:F1}% ({totalUploaded:N0} / {fileSize:N0} bytes)");
        }

        // Verify upload is complete
        var finalOffset = await GetUploadOffsetAsync(uploadLink);
        if (finalOffset < fileSize)
        {
            throw new Exception($"Upload incomplete. Expected {fileSize} bytes, got {finalOffset} bytes");
        }

        Console.WriteLine("✓ Video file upload completed");
    }

    private async Task<long> GetUploadOffsetAsync(string uploadLink)
    {
        var request = new HttpRequestMessage(HttpMethod.Head, uploadLink);
        request.Headers.Add("Tus-Resumable", "1.0.0");

        var response = await _httpClient.SendAsync(request);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Failed to get upload offset: {response.StatusCode} - {errorContent}");
        }

        if (response.Headers.TryGetValues("Upload-Offset", out var values))
        {
            var offsetValue = values.FirstOrDefault();
            if (long.TryParse(offsetValue, out long offset))
            {
                return offset;
            }
        }

        return 0;
    }

    private async Task WaitForVideoProcessingAsync(string videoUri)
    {
        var maxAttempts = 60; // Wait up to 5 minutes (5 seconds * 60)
        var attempt = 0;

        while (attempt < maxAttempts)
        {
            await Task.Delay(5000); // Wait 5 seconds

            var response = await _httpClient.GetAsync($"https://api.vimeo.com{videoUri}");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(content);

            var status = json["status"]?.ToString();
            if (status == "available")
            {
                // Wait a bit more to ensure video is fully ready for thumbnail operations
                Console.WriteLine("Video processing complete. Waiting a moment before proceeding...");
                await Task.Delay(2000); // Wait 2 more seconds
                return;
            }

            if (status == "error")
                throw new Exception("Video processing failed on Vimeo");

            attempt++;
        }

        throw new Exception("Video processing timeout");
    }

    public async Task SetThumbnailAsync(string videoUri, string thumbnailPath)
    {
        // Step 1: Get video ID from URI
        var videoId = videoUri.Split('/').Last();

        Console.WriteLine($"Uploading thumbnail for video {videoId}...");

        // Step 2: Create picture record (this may return an upload_link for custom images)
        var createPictureData = new Dictionary<string, object>
        {
            { "active", true }
        };

        var createContent = new StringContent(
            JsonConvert.SerializeObject(createPictureData),
            Encoding.UTF8,
            "application/json"
        );

        var createResponse = await _httpClient.PostAsync(
            $"https://api.vimeo.com/videos/{videoId}/pictures",
            createContent
        );

        if (!createResponse.IsSuccessStatusCode)
        {
            var errorContent = await createResponse.Content.ReadAsStringAsync();
            throw new Exception($"Failed to create picture record: {createResponse.StatusCode} - {errorContent}");
        }

        var responseContent = await createResponse.Content.ReadAsStringAsync();
        var json = JObject.Parse(responseContent);

        var pictureUri = json["uri"]?.ToString();
        
        // Check for upload link in various possible locations
        var uploadLink = json["link"]?.ToString() 
            ?? json["upload_link"]?.ToString() 
            ?? json["upload"]?["upload_link"]?.ToString()
            ?? json["upload_link_secure"]?.ToString();

        if (string.IsNullOrEmpty(pictureUri))
            throw new Exception($"Failed to get picture URI from Vimeo. Response: {responseContent}");

        Console.WriteLine($"Picture record created: {pictureUri}");
        
        if (!string.IsNullOrEmpty(uploadLink))
        {
            Console.WriteLine($"Found upload link: {uploadLink}");
        }
        else
        {
            Console.WriteLine("No upload link found in response, will try multipart form upload");
        }

        // Step 3: Upload the image file if upload_link is provided
        if (!string.IsNullOrEmpty(uploadLink))
        {
            Console.WriteLine("Uploading image file...");
            var fileInfo = new FileInfo(thumbnailPath);
            var fileBytes = await File.ReadAllBytesAsync(thumbnailPath);

            using var imageContent = new ByteArrayContent(fileBytes);
            
            // Detect content type from file extension
            var extension = Path.GetExtension(thumbnailPath).ToLowerInvariant();
            var contentType = extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                _ => "image/jpeg"
            };
            
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            var uploadResponse = await _httpClient.PutAsync(uploadLink, imageContent);
            
            if (!uploadResponse.IsSuccessStatusCode)
            {
                var errorContent = await uploadResponse.Content.ReadAsStringAsync();
                throw new Exception($"Failed to upload image file: {uploadResponse.StatusCode} - {errorContent}");
            }

            Console.WriteLine("Image file uploaded successfully");
        }
        else
        {
            // If no upload_link, try multipart form upload directly
            Console.WriteLine("Using multipart form upload...");
            using var fileStream = new FileStream(thumbnailPath, FileMode.Open, FileAccess.Read);
            using var multipartContent = new MultipartFormDataContent();
            using var streamContent = new StreamContent(fileStream);
            
            var extension = Path.GetExtension(thumbnailPath).ToLowerInvariant();
            var contentType = extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                _ => "image/jpeg"
            };
            
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            multipartContent.Add(streamContent, "file", Path.GetFileName(thumbnailPath));

            var uploadResponse = await _httpClient.PostAsync(
                $"https://api.vimeo.com/videos/{videoId}/pictures",
                multipartContent
            );

            if (!uploadResponse.IsSuccessStatusCode)
            {
                var errorContent = await uploadResponse.Content.ReadAsStringAsync();
                throw new Exception($"Failed to upload thumbnail via multipart: {uploadResponse.StatusCode} - {errorContent}");
            }

            var uploadResponseContent = await uploadResponse.Content.ReadAsStringAsync();
            var uploadJson = JObject.Parse(uploadResponseContent);
            pictureUri = uploadJson["uri"]?.ToString() ?? pictureUri;
        }

        // Step 4: Ensure the picture is set as active (in case it wasn't set during creation)
        var activateData = new Dictionary<string, object>
        {
            { "active", true }
        };

        var activateContent = new StringContent(
            JsonConvert.SerializeObject(activateData),
            Encoding.UTF8,
            "application/json"
        );

        var activateResponse = await _httpClient.PatchAsync(
            $"https://api.vimeo.com{pictureUri}",
            activateContent
        );

        if (!activateResponse.IsSuccessStatusCode)
        {
            var errorContent = await activateResponse.Content.ReadAsStringAsync();
            // Don't throw - thumbnail might already be active
            Console.WriteLine($"Warning: Could not set thumbnail as active: {activateResponse.StatusCode} - {errorContent}");
        }
        else
        {
            Console.WriteLine("✓ Thumbnail set as active");
            
            // Verify thumbnail is active by checking the video's pictures
            try
            {
                await Task.Delay(1000); // Wait a moment for changes to propagate
                var verifyResponse = await _httpClient.GetAsync($"https://api.vimeo.com/videos/{videoId}/pictures");
                if (verifyResponse.IsSuccessStatusCode)
                {
                    var verifyContent = await verifyResponse.Content.ReadAsStringAsync();
                    var verifyJson = JObject.Parse(verifyContent);
                    var pictures = verifyJson["data"] as JArray;
                    if (pictures != null)
                    {
                        foreach (var pic in pictures)
                        {
                            var picObj = pic as JObject;
                            var isActive = picObj?["active"]?.ToObject<bool>() ?? false;
                            var picUri = picObj?["uri"]?.ToString();
                            if (isActive && picUri == pictureUri)
                            {
                                Console.WriteLine("✓ Verified: Thumbnail is active on Vimeo");
                                return;
                            }
                        }
                        Console.WriteLine("⚠ Warning: Could not verify thumbnail is active. Please check manually on Vimeo.");
                    }
                }
            }
            catch (Exception verifyEx)
            {
                Console.WriteLine($"⚠ Warning: Could not verify thumbnail status: {verifyEx.Message}");
            }
        }
    }
}
