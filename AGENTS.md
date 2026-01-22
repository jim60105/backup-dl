# backup-dl Copilot Instructions

## Project Overview

**backup-dl** is a .NET 8.0 console application that automatically backs up YouTube videos to Azure Blob Storage. The application monitors YouTube channels and playlists, downloads new videos using yt-dlp, processes them with FFmpeg, and uploads them to Azure Blob Storage with Archive tier storage optimization.

## Project Structure

### Core Components

- **Program.cs**: Main entry point containing the download orchestration, video processing pipeline, and Azure upload logic
- **Helper/YoutubeDLHelper.cs**: yt-dlp integration wrapper providing video metadata retrieval
- **Models/YtdlpVideoData.cs**: Data models for yt-dlp JSON output
- **Json/SourceGenerationContext.cs**: Source-generated JSON serialization context for trimmed deployment

### Infrastructure

- **Dockerfile**: Multi-stage build with base, debug, build, publish, and final stages
- **helm/**: Kubernetes Helm chart for deployment as CronJob
- **.github/workflows/**: CI/CD pipelines for Docker build/publish and security scanning

## Technical Stack

### Runtime & Framework

- **.NET 8.0** (net8.0) - Console application with AOT-ready trimming
- **C# 12** with modern language features (partial classes, pattern matching, source generators)

### Key Dependencies

- **Azure.Storage.Blobs** (12.19.1) - Azure Blob Storage client
- **YoutubeDLSharp** (1.1.2) - yt-dlp wrapper for .NET
- **Xabe.FFmpeg** (5.2.6) - FFmpeg wrapper for video processing
- **Serilog** (3.1.1) with Console and Seq sinks - Structured logging

### External Tools (in Container)

- **yt-dlp** - YouTube video downloader
- **aria2c** - Multi-connection download accelerator
- **FFmpeg** - Video processing and metadata manipulation
- **Deno** (2.5.6) - JavaScript runtime for yt-dlp plugins

## Build & Deployment

### Local Development

```bash
# Restore dependencies
dotnet restore

# Build in Debug mode
dotnet build

# Run with environment variables
dotnet run
```

### Docker Build

```bash
# Build production image
docker build -t backup-dl:latest .

# Build with specific target (base, debug, build, publish, final)
docker build --target final -t backup-dl:latest .
```

The Dockerfile uses:

- Base image: `mcr.microsoft.com/dotnet/runtime-deps:8.0` (production) or `mcr.microsoft.com/dotnet/runtime:8.0` (debug)
- SDK image: `mcr.microsoft.com/dotnet/sdk:8.0` (build stage)
- Self-contained, trimmed, single-file deployment
- Non-root user (UID 1654) with OpenShift compatibility

### Deployment Options

**Docker Run:**

```bash
docker run \
  --env CHANNELS_IN_ARRAY='["https://www.youtube.com/channel/CHANNEL_ID"]' \
  --env AZURE_STORAGE_CONNECTION_STRING_VTUBER="DefaultEndpointsProtocol=https;..." \
  --env MAX_DOWNLOAD="10" \
  ghcr.io/jim60105/backup-dl:latest
```

**Kubernetes/Helm:**

```bash
helm install backup-dl ./helm \
  --set env[0].value='["https://www.youtube.com/channel/CHANNEL_ID"]' \
  --set env[1].value="CONNECTION_STRING"
```

### CI/CD Pipelines

**docker_publish.yml**: Triggered on push to master or tags

- Multi-platform build (linux/amd64)
- Pushes to Docker Hub, GHCR, and Quay.io
- Uses Docker Buildx with caching

**scan.yml**: Runs after docker_publish

- Trivy vulnerability scanning
- Generates HTML report artifact

## Environment Variables

### Required

- `AZURE_STORAGE_CONNECTION_STRING_VTUBER`: Azure Storage connection string
- `CHANNELS_IN_ARRAY`: JSON array of YouTube channel/playlist URLs

### Optional

- `MAX_DOWNLOAD`: Maximum number of videos per execution (default: 10)
- `DATE_BEFORE`: Only download videos older than N days (default: 2)
- `FORMAT`: yt-dlp format selector (default: `(bv*+ba/b)[protocol^=http][protocol!*=dash]`)
- `SCYNCHRONOUS`: Run in synchronous mode for single-threaded machines

### Volume Mounts

- `cookies.txt:/app/cookies.txt`: Optional YouTube authentication cookies

## Application Architecture

### Execution Flow

1. **Initialization**
   - Configure Serilog logger (Console output)
   - Connect to Azure Blob Storage container "vtuber"
   - Download `archive.txt` from Azure (tracks already-downloaded videos)

2. **Download Loop**
   - Parse `CHANNELS_IN_ARRAY` JSON array
   - Configure yt-dlp options (format, archive, aria2c downloader)
   - Run download in loop `MAX_DOWNLOAD` times
   - Videos downloaded to `/tmp/backup-dl/%(id)s.%(ext)s`

3. **Post-Processing Pipeline** (async per video)
   - Detect completed downloads by `.mkv` extension
   - Fetch video metadata via yt-dlp (JSON dump)
   - Rename file: `[YYYYMMDD] Title (VideoID).mkv`
   - Add thumbnail image to MKV container (FFmpeg)
   - Add metadata: title, artist, date, description (FFmpeg)
   - Upload to Azure Blob Storage (Archive tier)
   - Update `archive.txt` in Azure

4. **Cleanup**
   - Delete temporary directory
   - Flush logs

### Concurrency Model

- **Asynchronous design**: Each video enters post-processing immediately after download
- **Parallel processing**: Multiple videos can be processed/uploaded simultaneously
- **Progress tracking**: `archive.txt` updated per video to prevent re-download on interruption

### FFmpeg Operations

**Add Thumbnail:**

```
ffmpeg -i video.mkv -i thumbnail.jpg -map 0 -map 1 -c copy -disposition:v:1 attached_pic output.mkv
```

**Add Metadata:**

```
ffmpeg -i video.mkv -metadata title="..." -metadata artist="..." -metadata date="..." -metadata description="..." -codec copy output.mkv
```

### Azure Blob Storage

- **Upload path**: Relative to temp directory base
- **Archive tier**: Videos uploaded with `AccessTier.Archive` for cost optimization
- **Hot tier**: `archive.txt` uploaded with `AccessTier.Hot` for frequent access
- **Retry logic**: Automatic retry once on upload failure
- **Overwrite behavior**: Archives deleted before overwrite; others overwritten directly

## Code Style & Conventions

### Language

- **Code comments**: English
- **Console logs**: English
- **Documentation**: 正體中文 (Traditional Chinese) for README

### Naming Conventions

- Private fields: `_camelCase` with underscore prefix
- Public properties: `PascalCase`
- Local variables: `camelCase`
- Constants: `PascalCase`

### C# Features in Use

- Source-generated JSON serialization (`JsonSerializerContext`)
- Partial classes and methods
- Pattern matching and switch expressions
- Null-coalescing operators
- `async`/`await` task-based asynchrony
- LINQ for collection operations
- Structured logging with Serilog

### Project Settings

- `PublishTrimmed`: true - Enable IL trimming
- `PublishSingleFile`: true - Single executable output
- `InvariantGlobalization`: true - No culture-specific data
- `ServerGarbageCollection`: false - Workstation GC for console app

## Testing & Validation

### Manual Testing

```bash
# Test with debug configuration
docker build --target debug -t backup-dl:debug .
docker run --rm -it backup-dl:debug
```

### GitHub Actions Validation

- Docker build succeeds on master branch
- Trivy scan passes without critical/high vulnerabilities
- Multi-registry push succeeds (Docker Hub, GHCR, Quay.io)

## Common Issues & Solutions

### Video Download Failures

- **Symptom**: yt-dlp fails with 403/429 errors
- **Solution**: Mount cookies.txt with authenticated YouTube session

### Archive Tier Access

- **Symptom**: Blob already exists error on overwrite
- **Solution**: Archive tier blobs are deleted before overwrite (implemented)

### FFmpeg Errors

- **Symptom**: "Xabe.FFmpeg is dead" in logs
- **Solution**: Check FFmpeg binary availability and file permissions

### Trimming Errors

- **Symptom**: MissingMethodException at runtime
- **Solution**: Add to `TrimmerRootAssembly` in `.csproj`

## Important Notes

- **Video format**: All videos converted to MKV container (DownloadMergeFormat.Mkv)
- **Download strategy**: Only videos older than `DATE_BEFORE` days (avoids incomplete transcodes)
- **Live streams**: Excluded via `MatchFilters: "!is_live"`
- **Concurrency**: Disable with `SCYNCHRONOUS` env var on single-core systems
- **Xabe.FFmpeg license**: Non-commercial use only
- **YoutubeDLSharp license**: BSD 3-Clause
- **yt-dlp license**: Unlicensed
- **Project license**: GNU GPL v3

## Kubernetes Deployment

The Helm chart deploys as a CronJob:

- **Schedule**: Default `0 1 * * *` (1 AM daily)
- **Security**: Non-root, no privilege escalation, capabilities dropped
- **ConfigMap**: cookies.txt stored as ConfigMap
- **Environment**: All config via env vars in values.yaml

## Docker Registry Availability

Images published to:

- `ghcr.io/jim60105/backup-dl:latest`
- `docker.io/jim60105/backup-dl:latest`
- `quay.io/jim60105/backup-dl:latest`

Tags:

- `latest`: Latest master branch build
- `<git-tag>`: Specific version tags
- `master`: Master branch builds

## Additional Resources

- **Blog post**: [琳的備忘手札 - [Docker] Backup-dl - 備份 Youtube 影片至 Azure Blob Storage](https://xn--jgy.tw/Livestream/backup-dl/)
- **yt-dlp docs**: <https://github.com/yt-dlp/yt-dlp>
- **Azure Blob Storage tiers**: <https://learn.microsoft.com/en-us/azure/storage/blobs/access-tiers-overview>
