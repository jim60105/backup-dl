using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using YoutubeDLSharp;
using YoutubeDLSharp.Helpers;
using YoutubeDLSharp.Options;
using YtdlpVideoData = backup_dl.Models.YtdlpVideoData.ytdlpVideoData;

namespace backup_dl.Helper;

internal static partial class YoutubeDL
{
    /// <summary>
    /// Modified from YoutubeDL.RunVideoDataFetch()
    /// </summary>
    /// <param name="ytdl"></param>
    /// <param name="url"></param>
    /// <param name="ct"></param>
    /// <param name="flat"></param>
    /// <param name="overrideOptions"></param>
    /// <returns></returns>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = $"{nameof(SourceGenerationContext)} is set.")]
#pragma warning disable CA1068 // CancellationToken 參數必須位於最後
    public static async Task<RunResult<YtdlpVideoData>> RunVideoDataFetch_Alt(this YoutubeDLSharp.YoutubeDL ytdl,
            string url,
            CancellationToken ct = default,
            bool flat = true,
            bool fetchComments = false,
            OptionSet overrideOptions = null)
#pragma warning restore CA1068 // CancellationToken 參數必須位於最後
    {
        OptionSet opts = new()
        {
            IgnoreErrors = ytdl.IgnoreDownloadErrors,
            IgnoreConfig = true,
            NoPlaylist = true,
            Downloader = "m3u8:native",
            DownloaderArgs = "ffmpeg:-nostats -loglevel 0",
            Output = Path.Combine(ytdl.OutputFolder, ytdl.OutputFileTemplate),
            RestrictFilenames = ytdl.RestrictFilenames,
            ForceOverwrites = ytdl.OverwriteFiles,
            NoOverwrites = !ytdl.OverwriteFiles,
            NoPart = true,
            FfmpegLocation = Utils.GetFullPath(ytdl.FFmpegPath),
            Exec = "echo outfile: {}",
            DumpSingleJson = true,
            FlatPlaylist = flat,
            WriteComments = fetchComments,
            ExtractorArgs = new MultiValue<string>("youtube:pot=bgutil-script"),
            Verbose = true
        };
        if (overrideOptions != null)
        {
            opts = opts.OverrideOptions(overrideOptions);
        }
        YtdlpVideoData videoData = null;
        YoutubeDLProcess youtubeDLProcess = new(ytdl.YoutubeDLPath);
        youtubeDLProcess.OutputReceived += (o, e) =>
        {
            // Skip if data is null or empty
            if (string.IsNullOrWhiteSpace(e.Data)) return;

            // Only process JSON data (starts with '{')
            if (!e.Data.TrimStart().StartsWith('{')) return;

            try
            {
                // Workaround: Fix invalid json directly
                var data = e.Data.Replace("\"[{", "[{")
                                 .Replace("}]\"", "}]")
                                 .Replace("False", "false")
                                 .Replace("True", "true");
                // Change json string from 'sth' to "sth"
                data = ChangeJsonStringSingleQuotesToDoubleQuotes().Replace(data, @"""$1""");
                videoData = JsonSerializer.Deserialize<YtdlpVideoData>(
                    data,
                    options: new()
                    {
                        TypeInfoResolver = SourceGenerationContext.Default
                    });
            }
            catch (JsonException)
            {
                // Ignore non-JSON output lines
            }
        };
        FieldInfo fieldInfo = typeof(YoutubeDLSharp.YoutubeDL).GetField("runner", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.GetField | BindingFlags.SetField);
        (int code, string[] errors) = await (fieldInfo.GetValue(ytdl) as ProcessRunner).RunThrottled(youtubeDLProcess, [url], opts, ct);
        return new RunResult<YtdlpVideoData>(code == 0, errors, videoData);
    }
#nullable enable 

    [GeneratedRegex("(?:[\\s:\\[\\{\\(])'([^'\\r\\n\\s]*)'(?:\\s,]}\\))")]
    private static partial Regex ChangeJsonStringSingleQuotesToDoubleQuotes();
}
