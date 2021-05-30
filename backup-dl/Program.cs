using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Xabe.FFmpeg;
using YoutubeDLSharp;
using YoutubeDLSharp.Metadata;
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
                    IgnoreConfig = true,
                    // 非DASH的最佳品質
                    Format = "bestvideo+bestaudio/best",
                    YoutubeSkipDashManifest = true,
                    IgnoreErrors = true,
                    MergeOutputFormat = DownloadMergeFormat.Mkv,
                    NoCheckCertificate = true,
                    Output = Path.Combine(tempDir, "%(channel_id)s/%(id)s.%(ext)s"),
                    DownloadArchive = archivePath,
                    ExternalDownloader = "aria2c",
                    ExternalDownloaderArgs = "-j 16 -s 16 -x 16 -k 1M --retry-wait 10 --max-tries 10",
                    NoResizeBuffer = true,
                    WriteThumbnail = true,
                    //WriteInfoJson = true,

                    //這兩個會在merge結束前就執行，必定造成失敗
                    //EmbedThumbnail = true,
                    //AddMetadata = true,
                };

                // 最大下載數
                if (int.TryParse(Environment.GetEnvironmentVariable("Max_Download"), out int maxDownload))
                {
                    optionSet.MaxDownloads = maxDownload;
                }

                // 下載
                string ytdlPath = "/usr/local/bin/youtube-dlc";
                var ytdlProc = new YoutubeDLProcess(ytdlPath);
                ytdlProc.OutputReceived += (o, e) => Console.WriteLine(e.Data);
                ytdlProc.ErrorReceived += (o, e) => Console.WriteLine("ERROR: " + e.Data);

                ytdlProc.RunAsync(
                    channels,
                    optionSet,
                    new CancellationToken()).Wait();

                // 加封面圖和影片資訊
                List<Task> tasks = new();
                List<string> files = Directory.EnumerateFiles(tempDir, "*.mkv", SearchOption.AllDirectories).ToList();
                YoutubeDL ytdl = new();
                ytdl.YoutubeDLPath = ytdlPath;
                foreach (string filePath in files)
                {
                    string id = Path.GetFileNameWithoutExtension(filePath);
                    CancellationTokenSource cancel = new();

                    tasks.Add(
                        ytdl.RunVideoDataFetch($"https://www.youtube.com/watch?v={id}")
                            .ContinueWith((res) =>
                            {
                                VideoData videoData = res.IsCompletedSuccessfully ? res.Result.Data : null;
                                string newPath = CalculatePath(filePath, videoData?.Title);
                                return (newPath, videoData);
                            })
                            .ContinueWith((res) =>
                            {
                                (string newPath, VideoData videoData) = res.Result;
                                AddMetaData(newPath, videoData).Wait();
                                return newPath;
                            })
                            .ContinueWith((res) =>
                            {
                                string newPath = res.Result;
                                AddThumbNailImage(filePath, newPath).Wait();
                            })
                    );
                }
                Task.WaitAll(tasks.ToArray());

                // 上傳blob storage
                tasks.Clear();
                files = Directory.EnumerateFiles(tempDir, "*.mkv", SearchOption.AllDirectories).ToList();
                files.Add(archivePath);
                foreach (string filePath in files)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        using (FileStream fs = new(filePath, FileMode.Open, FileAccess.Read))
                        {
                            // 覆寫
                            _ = await containerClient
                                .GetBlobClient($"{GetRelativePath(filePath, Path.Combine(tempDir, "backup-dl"))}")
                                .UploadAsync(fs, new BlobHttpHeaders { ContentType = "video/x-matroska" });
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

        /// <summary>
        /// 轉成mp4
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        private static async Task<string> ConvertToMp4Async(string filePath)
        {
            string id = Path.GetFileNameWithoutExtension(filePath);
            string tempPath = Path.Combine(Path.GetDirectoryName(filePath), id + ".mp4");
            var conversion = await FFmpeg.Conversions.FromSnippet.ToMp4(filePath, tempPath);
            _ = await conversion.Start();
            return tempPath;
        }

        /// <summary>
        /// 路徑做檢查和轉換
        /// </summary>
        /// <param name="oldPath"></param>
        /// <param name="title"></param>
        /// <returns></returns>
        private static string CalculatePath(string oldPath, string title)
        {
            title ??= "";

            string newPath = Path.Combine(Path.GetDirectoryName(oldPath), title + Path.GetExtension(oldPath));
            if (!PathIsValid(newPath))
            {
                // 取代掉非法字元
                newPath = string.Join(string.Empty, newPath.Split(Path.GetInvalidFileNameChars()));
            }
            if (!PathIsValid(newPath) || string.IsNullOrEmpty(title))
            {
                // 延用舊檔名，先將原檔移到暫存路徑，ffmpeg轉換時輸出至原位
                newPath = oldPath;
            }
            if (newPath != oldPath)
            {
                File.Move(oldPath, newPath);
                File.Delete(oldPath);
            }

            return newPath;
        }

        // https://codereview.stackexchange.com/questions/120002/windows-filepath-and-filename-validation
        private static bool PathIsValid(string inputPath)
        {
            try
            {
                _ = Path.GetFullPath(inputPath);
                return true;
            }
            catch (PathTooLongException)
            {
                return false;
            }
        }

        /// <summary>
        /// 加封面圖
        /// </summary>
        /// <param name="thumbPath"></param>
        /// <param name="filePath"></param>
        /// <returns></returns>
        private static async Task AddThumbNailImage(string thumbPath, string filePath)
        {
            string imagePathName = Path.Combine(Path.GetDirectoryName(thumbPath), Path.GetFileNameWithoutExtension(thumbPath));
            string jpgPath = imagePathName + ".jpg";

            string[] extensions = { ".jpeg", ".gif", ".png", ".bmp", ".webp" };
            foreach (var ext in extensions)
            {
                string sourceImgPath = imagePathName + ext;
                if (File.Exists(sourceImgPath))
                {
                    _ = await new Conversion().AddParameter($" -i \"{sourceImgPath}\" ")
                                              .SetOutput(jpgPath)
                                              .Start();
                    break;
                }
            }

            if (File.Exists(jpgPath))
            {
                var tempPath = Path.GetTempFileName();

                //FFmpeg method
                IConversion conversion = new Conversion().AddParameter($" -i \"{filePath}\" -y -codec copy", ParameterPosition.PreInput)
                                                         .AddParameter($" -attach \"{jpgPath}\" -map 0 -metadata:s:t:0 mimetype=image/jpeg -metadata:s:t:0 filename=cover.jpg ", ParameterPosition.PreInput)
                                                         .SetOutputFormat(Format.matroska)
                                                         .SetOutput(tempPath);
                //Console.WriteLine(conversion.Build());
                IConversionResult convRes = await conversion.Start();

                ////AtomicParsley method(Only works with mp3/ mp4 / m4a)
                ////Not tested.
                //using (System.Diagnostics.Process proc = new())
                //{
                //    proc.StartInfo.RedirectStandardError = proc.StartInfo.RedirectStandardOutput = true;
                //    proc.StartInfo.FileName = "AtomicParsley";
                //    proc.StartInfo.Arguments = $"{newPath} --artwork {jpgPath} -o {tempPath}";
                //    _ = proc.Start();
                //    proc.WaitForExit();
                //}

                File.Delete(filePath);
                File.Move(tempPath, filePath);
            }
        }

        /// <summary>
        /// 加MetaData
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="video"></param>
        /// <returns></returns>
        private static async Task AddMetaData(string filePath, VideoData video)
        {
            if (null == video) return;

            // 加MetaData
            string title = video.Title;
            string artist = video.Uploader;
            DateTime date = video.UploadDate ?? DateTime.Now;
            string description = video.Description;

            var tempPath = Path.GetTempFileName();
            IConversion conversion = new Conversion().AddParameter($"-i \"{filePath}\" -y -codec copy -map 0", ParameterPosition.PreInput)
                                                     .AddParameter($"-metadata title=\"{title}\" -metadata artist=\"{artist}\" -metadata date=\"{date:u}\" -metadata description=\"{description}\" -metadata comment=\"{description}\"", ParameterPosition.PreInput)
                                                     .SetOutputFormat(Format.matroska)
                                                     .SetOutput(tempPath);
            //Console.WriteLine(conversion.Build());
            IConversionResult convRes = await conversion.Start();

            File.Delete(filePath);
            File.Move(tempPath, filePath);
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
