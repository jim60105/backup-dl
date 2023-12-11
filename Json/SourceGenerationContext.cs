﻿using System.Text.Json;
using System.Text.Json.Serialization;

// Must read:
// https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation?pivots=dotnet-8-0
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(backup_dl.Models.YtdlpVideoData.ytdlpVideoData))]
[JsonSourceGenerationOptions(WriteIndented = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip)]
internal partial class SourceGenerationContext : JsonSerializerContext { }
