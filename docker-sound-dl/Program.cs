using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace docker_sound_dl
{
    class Program
    {
        static void Main(string[] args)
        {
            // Create a BlobServiceClient object which will be used to create a container client
            string connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
            BlobServiceClient blobServiceClient = new(connectionString);

            // Get the container and return a container client object
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient("sound-buttons");

            // 取得要下載的連結
            string channelsToDownload = Environment.GetEnvironmentVariable("CHANNELS_IN_ARRAY");
            string[] channels = JsonSerializer.Deserialize<string[]>(channelsToDownload);

            // TempPath
            string tempDir = Path.Combine(Path.GetTempPath(), "audio-dl");
            _ = Directory.CreateDirectory(tempDir);
            string archivePath = Path.Combine(tempDir, "archive.txt");

            try
            {
                // 取得archive.txt
                BlobClient archiveBlob = containerClient.GetBlobClient("AudioSource/archive.txt");
                _ = archiveBlob.DownloadTo(archivePath);

                OptionSet optionSet = new()
                {
                    // 最佳音質
                    Format = "140/m4a",
                    NoCheckCertificate = true,
                    Output = Path.Combine(tempDir, "%(id)s"),
                    DownloadArchive = archivePath,
                    Continue = true,
                    IgnoreErrors = true,
                    NoOverwrites = true
                };

                // 下載音訊
                new YoutubeDLProcess("/usr/local/bin/youtube-dl").RunAsync(
                    channels,
                    optionSet,
                    new System.Threading.CancellationToken()).Wait();

                // 上傳blob storage
                List<Task> tasks = new();
                foreach (string filePath in Directory.GetFiles(tempDir))
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        using (FileStream fs = new(filePath, FileMode.Open, FileAccess.Read))
                        {
                            // 覆寫
                            _ = await containerClient
                                .GetBlobClient($"AudioSource/{Path.GetFileName(filePath)}")
                                .UploadAsync(fs, new BlobHttpHeaders { ContentType = "video/mp4" });
                        }
                    }));
                }

                Task.WaitAll(tasks.ToArray());
            } finally
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
