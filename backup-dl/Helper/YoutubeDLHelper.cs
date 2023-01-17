﻿using backup_dl.Models;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using YoutubeDLSharp;
using YoutubeDLSharp.Helpers;
using YoutubeDLSharp.Options;

namespace backup_dl.Helper;

internal static partial class YoutubeDL
{
#nullable disable
    /// <summary>
    /// Modified from YoutubeDL.RunVideoDataFetch()
    /// </summary>
    /// <param name="ytdl"></param>
    /// <param name="url"></param>
    /// <param name="ct"></param>
    /// <param name="flat"></param>
    /// <param name="overrideOptions"></param>
    /// <returns></returns>
#pragma warning disable CA1068 // CancellationToken 參數必須位於最後
    public static async Task<RunResult<YtdlpVideoData>> RunVideoDataFetch_Alt(this YoutubeDLSharp.YoutubeDL ytdl, string url, CancellationToken ct = default, bool flat = true, OptionSet overrideOptions = null)
#pragma warning restore CA1068 // CancellationToken 參數必須位於最後
    {
        OptionSet optionSet = new()
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
        YtdlpVideoData videoData = null;
        YoutubeDLProcess youtubeDLProcess = new(ytdl.YoutubeDLPath);
        youtubeDLProcess.OutputReceived += (o, e) =>
        {
            // Workaround: Fix invalid json directly
            var data = e.Data.Replace("\"[{", "[{")
                             .Replace("}]\"", "}]")
                             .Replace("False", "false")
                             .Replace("True", "true");
            // Change json string from 'sth' to "sth"
            data = ChangeJsonStringSingleQuotesToDoubleQuotes().Replace(data, @"""$1""");
            videoData = Newtonsoft.Json.JsonConvert.DeserializeObject<YtdlpVideoData>(data);
        };
        FieldInfo fieldInfo = typeof(YoutubeDLSharp.YoutubeDL).GetField("runner", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.GetField | BindingFlags.SetField);
        (int code, string[] errors) = await (fieldInfo.GetValue(ytdl) as ProcessRunner).RunThrottled(youtubeDLProcess, new[] { url }, optionSet, ct);
        return new RunResult<YtdlpVideoData>(code == 0, errors, videoData);
    }
#nullable enable 

    [GeneratedRegex("(?:[\\s:\\[\\{\\(])'([^'\\r\\n\\s]*)'(?:\\s,]}\\))")]
    private static partial Regex ChangeJsonStringSingleQuotesToDoubleQuotes();
}
