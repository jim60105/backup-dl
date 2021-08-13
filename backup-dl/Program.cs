using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xabe.FFmpeg;
using YoutubeDLSharp;
using YoutubeDLSharp.Helpers;
using YoutubeDLSharp.Metadata;
using YoutubeDLSharp.Options;

namespace backup_dl
{
    class Program
    {
        private static ILogger logger;
        private static BlobContainerClient containerClient;

        public static string YtdlPath { get; set; } = "/usr/bin/yt-dlp";
        private static string CookiesPath { get; set; } = null;

        static void Main(string[] args)
        {
            // 建立Logger
            Log.Logger = new LoggerConfiguration()
                            .MinimumLevel.Verbose()
                            .WriteTo.Console()
                            .CreateLogger();
            logger = Log.Logger;

            // 計時
            DateTime startTime = DateTime.Now;
            logger.Information("Start backup-dl {now}", startTime.ToString());

            // Create a BlobServiceClient object which will be used to create a container client
            // Get the container and return a container client object
            string connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING_VTUBER");
            BlobServiceClient blobServiceClient = new(connectionString);
            containerClient = blobServiceClient.GetBlobContainerClient("vtuber");

            // 取得要下載的連結
            string channelsToDownload = Environment.GetEnvironmentVariable("CHANNELS_IN_ARRAY");
            string[] channels = JsonSerializer.Deserialize<string[]>(channelsToDownload);

            // TempPath
            string tempDir = Path.Combine(Path.GetTempPath(), "backup-dl");
            _ = Directory.CreateDirectory(tempDir);
            string archivePath = Path.Combine(tempDir, "archive.txt");
            string ytdlArchivePath = Path.Combine(tempDir, "archive_ytdl.txt");
            string oldArchivePath = Path.Combine(tempDir, "archive_old.txt");

            try
            {
                // 取得archive.txt
                BlobClient archiveBlob = containerClient.GetBlobClient("archive.txt");
                if (archiveBlob.Exists())
                {
                    _ = archiveBlob.DownloadTo(archivePath);
                    File.Copy(archivePath, ytdlArchivePath, true);
                    File.Copy(archivePath, oldArchivePath, true);
                    _ = UploadToAzure(tempDir, oldArchivePath, ContentType: "text/plain");
                }

                OptionSet optionSet = new()
                {
                    IgnoreConfig = true,
                    Format = Environment.GetEnvironmentVariable("FORMAT") ?? "bestvideo+bestaudio/best",
                    YoutubeSkipDashManifest = true,
                    IgnoreErrors = true,
                    MergeOutputFormat = DownloadMergeFormat.Mkv,
                    NoCheckCertificate = true,
                    Output = Path.Combine(tempDir, "%(channel_id)s/%(id)s.%(ext)s"),
                    DownloadArchive = ytdlArchivePath,
                    ExternalDownloader = "aria2c",
                    ExternalDownloaderArgs = "-j 16 -s 16 -x 16 -k 1M --retry-wait 10 --max-tries 10 --enable-color=false",
                    NoResizeBuffer = true,
                    WriteThumbnail = true,
                    NoColor = true,
                    DateBefore = DateTime.Now.AddDays(-2),
                    PreferFreeFormats = true
                    //WriteInfoJson = true,

                    //這兩個會在merge結束前就執行，必定造成失敗
                    //EmbedThumbnail = true,
                    //AddMetadata = true,
                };

                // 最大下載數
                // 每次只下載一個檔案，跑{maxDownload}次
                // 或者不限制數量，跑一次到完 (就無法非同步上傳)
                if (int.TryParse(Environment.GetEnvironmentVariable("MAX_DOWNLOAD"), out int maxDownload))
                {
                    optionSet.MaxDownloads = 1;
                }
                else
                {
                    maxDownload = 1;
                }

                // 讀出cookies.txt
                if (File.Exists("cookies.txt"))
                {
                    optionSet.Cookies = CookiesPath = Path.GetFullPath("cookies.txt");
                    logger.Information("Find cookies file.");
                }

                YoutubeDLProcess ytdlProc = new(YtdlPath);
                ytdlProc.OutputReceived += (o, e) => logger.Verbose(e.Data);
                ytdlProc.ErrorReceived += (o, e) => logger.Error(e.Data);

                List<string> ProcessedIds = new();
                List<Task> tasks = new();

                // 在執行前呼叫，處理上次未上傳完成的檔案
                tasks = PostProcess(tempDir, ref ProcessedIds);
                File.WriteAllLines(
                    ytdlArchivePath,
                    File.ReadAllLines(ytdlArchivePath)
                        .ToList()
                        .Concat(ProcessedIds.Select(id => "youtube " + id + Environment.NewLine)));

                logger.Information("Start download process.");
                logger.Debug("MAX_DOWNLOAD: {maxDownload}", maxDownload);
                for (int i = 0; i < maxDownload; i++)
                {
                    logger.Debug("Excute times: {excuteTimes}", i + 1);
                    ytdlProc.RunAsync(
                        channels,
                        optionSet,
                        new CancellationToken()).Wait();

                    tasks = tasks.Concat(PostProcess(tempDir, ref ProcessedIds)).ToList();
                }

                Task.WaitAll(tasks.ToArray());
                logger.Debug("All tasks are completed. Total time spent: {timeSpent}", (DateTime.Now - startTime).ToString("hh\\:mm\\:ss"));
            }
            finally
            {
                Directory.Delete(tempDir, true);
                Log.CloseAndFlush();
            }
        }

        /// <summary>
        /// 影片下載後處理 (重命名、加封面、加metadata、上傳)
        /// </summary>
        /// <param name="tempDir"></param>
        /// <param name="ProcessedIds">已處理中的id清單</param>
        /// <returns></returns>
        private static List<Task> PostProcess(string tempDir, ref List<string> ProcessedIds)
        {
            List<string> files = Directory.EnumerateFiles(tempDir, "*.mkv", SearchOption.AllDirectories).ToList();
            List<Task> tasks = new();

            // 同步執行
            bool sync = null != Environment.GetEnvironmentVariable("SCYNCHRONOUS");

            foreach (string filePath in files)
            {
                // 剛由ytdl下載完的檔案，檔名為 {id}
                // 處理完成的檔案，檔名為 {date:yyyyMMdd} {title} ({id}).mkv)}
                // 此處比對在()中的id
                Match match = Regex.Match(Path.GetFileNameWithoutExtension(filePath), @"\s\((.*)\)$");
                string id = match.Success
                    ? match.Groups[1].Value
                    : Path.GetFileNameWithoutExtension(filePath);
                string title = id;
                float? duration = null;

                if (ProcessedIds.Contains(id)) continue;
                ProcessedIds.Add(id);

                Task<string> task;
                Task finalTask;

                string archivePath = Path.Combine(tempDir, "archive.txt");

                OptionSet overrideOptions = new();
                // 讀出cookies.txt
                if (!string.IsNullOrEmpty(CookiesPath))
                {
                    overrideOptions.Cookies = CookiesPath;
                }

                // 如果檔名比對成功，則為運算完成的檔案，直接跳到上傳
                // 這會在上傳中斷，重新啟動時發生
                if (match.Success)
                {
                    task = Task.Factory.StartNew(() => filePath);
                }
                else
                {
                    CancellationTokenSource cancel = new();
                    task = new YoutubeDL() { YoutubeDLPath = YtdlPath }
                        .RunVideoDataFetch_Fix($"https://www.youtube.com/watch?v={id}", overrideOptions: overrideOptions)
                        .ContinueWith((res) =>
                        {
                            VideoData videoData = null;
                            if (res.IsCompletedSuccessfully && res.Result.Success)
                            {
                                videoData = res.Result.Data;
                            }

                            // Aria2c會對id做處理，若取影片失敗則試著反相處理再重fetch
                            if (null == videoData)
                            {
                                string _id = id.Replace('_', '-');
                                videoData = TryAgainWithId(_id);
                                if (null != videoData) id = _id;
                            }

                            if (null == videoData)
                            {
                                string _id = $"_{id}";
                                videoData = TryAgainWithId(_id);
                                if (null != videoData) id = _id;
                            }

                            if (null == videoData)
                            {
                                cancel.Cancel();
                                videoData = null;
                            }

                            title = videoData?.Title;
                            duration = videoData.Duration;

                            string newPath = CalculatePath(filePath, videoData?.Title, videoData?.UploadDate);
                            return (newPath, videoData);

                            VideoData TryAgainWithId(string id)
                            {
                                VideoData resultVideoData = null;
                                using (Task<RunResult<VideoData>> _task = new YoutubeDL() { YoutubeDLPath = YtdlPath }
                                    .RunVideoDataFetch_Fix($"https://www.youtube.com/watch?v={id}", overrideOptions: overrideOptions))
                                {
                                    _task.Wait();
                                    if (_task.IsCompletedSuccessfully && _task.Result.Success)
                                    {
                                        resultVideoData = _task.Result.Data;
                                    }
                                }
                                return resultVideoData;
                            }
                        }, cancel.Token)
                        .ContinueWith((res) =>
                        {
                            if (!res.IsCompletedSuccessfully) cancel.Cancel();

                            (string newPath, VideoData videoData) = res.Result;
                            AddMetaData(newPath, videoData).Wait();
                            return newPath;
                        }, cancel.Token)
                        .ContinueWith((res) =>
                        {
                            string newPath = res.Result;
                            AddThumbNailImage(filePath, newPath).Wait();
                            logger.Information("PostProcess Finish: {path}", res.Result);
                            return newPath;
                        });
                }
                finalTask = task.ContinueWith((res) =>
                    {
                        string newPath = res.Result;
                        logger.Information("Start Uploading: {path}", res.Result);
                        Task<bool> task = UploadToAzure(tempDir, newPath, ID: id, duration: duration);
                        task.Wait();
                        return task.IsCompletedSuccessfully && task.Result ? newPath : null;
                    })
                    .ContinueWith((res) =>
                    {
                        string newPath = res.Result;
                        if (!string.IsNullOrEmpty(newPath))
                        {
                            File.AppendAllText(archivePath, "youtube " + id + Environment.NewLine);
                            UploadToAzure(tempDir, archivePath, ContentType: "text/plain").Wait();
                            logger.Information("Task done: {path}", res.Result);
                        }
                        else logger.Error("Excute Failed: {id}", id);
                        return newPath;
                    }, TaskContinuationOptions.ExecuteSynchronously);

                tasks.Add(finalTask);

                if (sync) task.Wait();
            }
            return tasks;
        }

        /// <summary>
        /// 上傳檔案至Azure Blob Storage
        /// </summary>
        /// <param name="containerClient"></param>
        /// <param name="tempDir">用來計算Storage內路徑的基準路徑</param>
        /// <param name="filePath">上傳檔案路徑</param>
        /// <returns></returns>
        private static async Task<bool> UploadToAzure(string tempDir, string filePath, bool retry = true, string ContentType = "video/x-matroska", string ID = null, float? duration = null)
        {
            bool isVideo = ContentType == "video/x-matroska";
            try
            {
                using (FileStream fs = new(filePath, FileMode.Open, FileAccess.Read))
                {
                    logger.Debug("Start Upload {path} to azure storage", filePath);
                    AccessTier accessTire = isVideo
                                            ? AccessTier.Archive
                                            : AccessTier.Hot;

                    long fileSize = new FileInfo(filePath).Length;

                    double percentage = 0;
                    Dictionary<string, string> tags =
                        (null == ID || null == duration)
                        ? new()
                        : new()
                        {
                            { "id", ID },
                            { "duration", duration?.ToString() }
                        };
                    // 覆寫
                    BlobClient blobClient = containerClient.GetBlobClient($"{GetRelativePath(filePath, Path.Combine(tempDir, "backup-dl"))}");
                    _ = await blobClient.UploadAsync(
                        content: fs,
                        httpHeaders: new BlobHttpHeaders { ContentType = ContentType },
                        accessTier: accessTire,
                        metadata: tags,
                        progressHandler: new Progress<long>(progress =>
                        {
                            double _percentage = Math.Round(((double)progress) / fileSize * 100);
                            if (_percentage != percentage)
                            {
                                percentage = _percentage;
                                logger.Verbose("Uploading...{progress}% {path}", _percentage, filePath);
                            }
                        })
                    );
                    _ = await blobClient.SetTagsAsync(tags);
                    logger.Debug("Finish Upload {path} to azure storage", filePath);

                    if (isVideo) File.Delete(filePath);
                    return true;
                }
            }
            catch (Exception e)
            {
                if (retry)
                {
                    // Retry Once
                    return await UploadToAzure(tempDir, filePath, false, ContentType, ID, duration);
                }
                else
                {
                    logger.Error("Upload Failed: {fileName}", Path.GetFileName(filePath));
                    logger.Error("{errorMessage}", e.Message);
                    return false;
                }
            }
        }

        /// <summary>
        /// 路徑做檢查和轉換
        /// </summary>
        /// <param name="oldPath"></param>
        /// <param name="title">影片標題，用做檔名</param>
        /// <param name="date">影片日期，用做檔名</param>
        /// <returns></returns>
        private static string CalculatePath(string oldPath, string title, DateTime? date)
        {
            title ??= "";
            // 取代掉檔名中的非法字元
            title = string.Join(string.Empty, title.Split(Path.GetInvalidFileNameChars()))
                          .Replace(".", string.Empty);
            // 截短
            title = title.Substring(0, title.Length < 150 ? title.Length : 150);

            date ??= DateTime.Now;

            string newPath = Path.Combine(Path.GetDirectoryName(oldPath), $"{date:yyyyMMdd} {title} ({Path.GetFileNameWithoutExtension(oldPath)}){Path.GetExtension(oldPath)}");
            if (string.IsNullOrEmpty(title))
            {
                // 延用舊檔名，先將原檔移到暫存路徑，ffmpeg轉換時輸出至原位
                newPath = oldPath;
            }
            if (newPath != oldPath)
            {
                File.Move(oldPath, newPath);
                File.Delete(oldPath);
            }

            logger.Debug("Rename file: {oldPath} => {newPath}", oldPath, newPath);
            return newPath;
        }

        /// <summary>
        /// 加封面圖
        /// </summary>
        /// <param name="thumbPath"></param>
        /// <param name="filePath"></param>
        /// <returns></returns>
        private static async Task AddThumbNailImage(string thumbPath, string filePath)
        {
            try
            {
                string imagePathName = Path.Combine(Path.GetDirectoryName(thumbPath), Path.GetFileNameWithoutExtension(thumbPath));
                string jpgPath = imagePathName + ".jpg";

                string[] extensions = { ".jpeg", ".gif", ".png", ".bmp", ".webp" };
                foreach (var ext in extensions)
                {
                    string sourceImgPath = imagePathName + ext;
                    if (File.Exists(sourceImgPath))
                    {
                        logger.Debug("start Convert thumbnail format {ext}: {jpgPath}", ext, jpgPath);
                        IConversion conversion = new Conversion().AddParameter($" -i \"{sourceImgPath}\" ")
                                                                 .SetOutput(jpgPath);
                        logger.Debug(conversion.Build());
                        _ = await conversion.Start();
                        logger.Debug("Finish Convert thumbnail format {ext}: {jpgPath}", ext, jpgPath);
                        break;
                    }
                }

                if (File.Exists(jpgPath))
                {
                    string tempPath = Path.GetTempFileName();

                    //FFmpeg method
                    logger.Debug("Start Embed thumbnail: {path}", filePath);
                    IConversion conversion = new Conversion().AddParameter($" -i \"{filePath}\" -y -codec copy", ParameterPosition.PreInput)
                                                             .AddParameter($" -attach \"{jpgPath}\" -map 0 -metadata:s:t:0 mimetype=image/jpeg -metadata:s:t:0 filename=cover.jpg ", ParameterPosition.PreInput)
                                                             .SetOutputFormat(Format.matroska)
                                                             .SetOutput(tempPath);
                    logger.Debug("FFmpeg arguments: {arguments}", conversion.Build());
                    _ = await conversion.Start();

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
                    logger.Debug("Finish Embed thumbnail: {path}", filePath);
                }
            }
            catch (InvalidOperationException) { logger.Error("Xabe.FFmpeg is dead in AddThumbNailImage"); }
        }

        /// <summary>
        /// 加MetaData
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="video"></param>
        /// <returns></returns>
        private static async Task AddMetaData(string filePath, VideoData video)
        {
            try
            {
                if (null == video) return;

                // 加MetaData
                string title = video.Title;
                string artist = video.Uploader;
                DateTime date = video.UploadDate ?? DateTime.Now;
                string description = video.Description;

                var tempPath = Path.GetTempFileName();
                logger.Debug("Start Add matadata: {path}", filePath);
                IConversion conversion = new Conversion().AddParameter($"-i \"{filePath}\" -y -codec copy -map 0", ParameterPosition.PreInput)
                                                         .AddParameter($"-metadata title=\"{title}\" -metadata artist=\"{artist}\" -metadata date=\"{date:u}\" -metadata description=\"{description}\" -metadata comment=\"{description}\"", ParameterPosition.PreInput)
                                                         .SetOutputFormat(Format.matroska)
                                                         .SetOutput(tempPath);
                logger.Debug("FFmpeg arguments: {arguments}", conversion.Build());
                _ = await conversion.Start();

                File.Delete(filePath);
                File.Move(tempPath, filePath);
                logger.Debug("Finish Add matadata: {path}", filePath);
            }
            catch (InvalidOperationException) { logger.Error("Xabe.FFmpeg is dead in AddMetaData"); }
        }

        // https://weblog.west-wind.com/posts/2010/Dec/20/Finding-a-Relative-Path-in-NET
        // https://stackoverflow.com/q/5706555/8706033
        private static string GetRelativePath(string fullPath, string basePath)
        {
            // Require trailing backslash for path
            if (!basePath.EndsWith("\\"))
                basePath += "\\";

            Uri baseUri = new(basePath);
            Uri fullUri = new(fullPath);

            Uri relativeUri = baseUri.MakeRelativeUri(fullUri);

            // Uri's use forward slashes so convert back to backward slashes
            return Uri.UnescapeDataString(relativeUri.ToString()).Replace("/", "\\");
        }
    }

    static class Extension
    {
        /// <summary>
        /// Copy of YoutubeDL.RunVideoDataFetch()
        /// </summary>
        /// <param name="ytdl"></param>
        /// <param name="url"></param>
        /// <param name="ct"></param>
        /// <param name="flat"></param>
        /// <param name="overrideOptions"></param>
        /// <returns></returns>
        public static async Task<RunResult<VideoData>> RunVideoDataFetch_Fix(this YoutubeDL ytdl, string url, CancellationToken ct = default(CancellationToken), bool flat = true, OptionSet overrideOptions = null)
        {
            OptionSet optionSet = new OptionSet
            {
                IgnoreErrors = ytdl.IgnoreDownloadErrors,
                IgnoreConfig = true,
                NoPlaylist = true,
                HlsPreferNative = true,
                ExternalDownloaderArgs = "-nostats -loglevel 0",
                Output = Path.Combine(ytdl.OutputFolder, ytdl.OutputFileTemplate),
                RestrictFilenames = ytdl.RestrictFilenames,
                NoContinue = ytdl.OverwriteFiles,
                NoOverwrites = !ytdl.OverwriteFiles,
                NoPart = true,
                FfmpegLocation = Utils.GetFullPath(ytdl.FFmpegPath),
                Exec = "echo {}"
            };
            if (overrideOptions != null)
            {
                optionSet = optionSet.OverrideOptions(overrideOptions);
            }

            optionSet.DumpSingleJson = true;
            optionSet.FlatPlaylist = flat;
            VideoData videoData = null;
            YoutubeDLProcess youtubeDLProcess = new YoutubeDLProcess(ytdl.YoutubeDLPath);
            youtubeDLProcess.OutputReceived += delegate (object o, System.Diagnostics.DataReceivedEventArgs e)
            {
                // Workaround: Fix invalid json directly
                var data = e.Data.Replace("\"[{", "[{")
                                 .Replace("}]\"", "}]")
                                 .Replace("False", "false")
                                 .Replace("True", "true");
                // Change 'sth' to "sth"
                data = new Regex(@"'([^'\r\n\s]*(?:\\.[^'\r\n\s]*)*)'")
                                .Replace(data, @"""$1""");
                videoData = Newtonsoft.Json.JsonConvert.DeserializeObject<VideoData>(data);
            };
            FieldInfo fieldInfo = typeof(YoutubeDL).GetField("runner", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.GetField | BindingFlags.SetField);
            (int, string[]) obj = await (fieldInfo.GetValue(ytdl) as ProcessRunner).RunThrottled(youtubeDLProcess, new string[1]
            {
                url
            }, optionSet, ct);
            int item = obj.Item1;
            string[] item2 = obj.Item2;
            return new RunResult<VideoData>(item == 0, item2, videoData);
        }
    }
}
