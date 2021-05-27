using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace backup_dl
{
    class Program
    {
        static void Main(string[] args)
        {
            // Create a BlobServiceClient object which will be used to create a container client
            string connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING_VTUBER");
            BlobServiceClient blobServiceClient = new(connectionString);

            // Get the container and return a container client object
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient("vtuber");

            // 取得要下載的連結
            string channelsToDownload = Environment.GetEnvironmentVariable("CHANNELS_IN_ARRAY");
            string[] channels = JsonSerializer.Deserialize<string[]>(channelsToDownload);

            // TempPath
            string tempDir = Path.Combine(Path.GetTempPath(), "backup-dl");
            _ = Directory.CreateDirectory(tempDir);
            string archivePath = Path.Combine(tempDir, "archive.txt");

            try
            {
                // 取得archive.txt
                BlobClient archiveBlob = containerClient.GetBlobClient("archive.txt");
                if (archiveBlob.Exists())
                {
                    _ = archiveBlob.DownloadTo(archivePath);
                }

                OptionSet optionSet = new()
                {
                    // 非DASH的最佳品質
                    Format = "bestvideo+251/best",
                    YoutubeSkipDashManifest = true,
                    MergeOutputFormat = DownloadMergeFormat.Mp4,
                    NoCheckCertificate = true,
                    Output = Path.Combine(tempDir, "%(channel_id)s/%(upload_date)s_%(id)s.mp4"),
                    DownloadArchive = archivePath,
                    Continue = true,
                    IgnoreErrors = true,
                    NoOverwrites = true,
                    EmbedThumbnail = true,
                    AddMetadata = true,
                    ExternalDownloader = "aria2c",
                    ExternalDownloaderArgs = "-j 16 -s 16 -x 16 -k 1M --retry-wait 10 --max-tries 10"
                };

                // 最大下載數
                if (int.TryParse(Environment.GetEnvironmentVariable("Max_Download"), out int maxDownload))
                {
                    optionSet.MaxDownloads = maxDownload;
                }

                // 下載
#if DEBUG
                new YoutubeDLProcess().RunAsync(
#else
                new YoutubeDLProcess("/usr/local/bin/youtube-dl").RunAsync(
#endif
                    channels,
                    optionSet,
                    new System.Threading.CancellationToken()).Wait();

                // 上傳blob storage
                List<Task> tasks = new();
                foreach (string filePath in Directory.EnumerateFiles(tempDir, "*.mp4", SearchOption.AllDirectories))
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        using (FileStream fs = new(filePath, FileMode.Open, FileAccess.Read))
                        {
                            // 覆寫
                            _ = await containerClient
                                .GetBlobClient($"{GetRelativePath(filePath, tempDir)}")
                                .UploadAsync(fs, new BlobHttpHeaders { ContentType = "video/mp4" });
                        }
                    }));
                }

                Task.WaitAll(tasks.ToArray());
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        // https://weblog.west-wind.com/posts/2010/Dec/20/Finding-a-Relative-Path-in-NET
        private static string GetRelativePath(string fullPath, string basePath)
        {
            // Require trailing backslash for path
            if (!basePath.EndsWith("\\"))
                basePath += "\\";

            Uri baseUri = new Uri(basePath);
            Uri fullUri = new Uri(fullPath);

            Uri relativeUri = baseUri.MakeRelativeUri(fullUri);

            // Uri's use forward slashes so convert back to backward slashes
            return relativeUri.ToString().Replace("/", "\\");
        }
    }
}
