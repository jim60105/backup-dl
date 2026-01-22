# backup-dl

[![CodeFactor](https://www.codefactor.io/repository/github/jim60105/backup-dl/badge?style=for-the-badge)](https://www.codefactor.io/repository/github/jim60105/backup-dl)  
![License](https://img.shields.io/github/license/jim60105/backup-dl?style=for-the-badge)
![.NET Core](https://img.shields.io/static/v1?style=for-the-badge&message=.NET+Core&color=512BD4&logo=.NET&logoColor=FFFFFF&label=)
![Microsoft Azure](https://img.shields.io/static/v1?style=for-the-badge&message=Microsoft+Azure&color=0078D4&logo=Microsoft+Azure&logoColor=FFFFFF&label=)
![Podman](https://img.shields.io/static/v1?style=for-the-badge&message=Podman&color=892CA0&logo=Podman&logoColor=FFFFFF&label=)
![YouTube](https://img.shields.io/static/v1?style=for-the-badge&message=YouTube&color=FF0000&logo=YouTube&logoColor=FFFFFF&label=)

**Automated YouTube Video Archiver for Azure Blob Storage**

A .NET 8.0 console application that monitors YouTube channels and playlists, automatically downloading new videos and uploading them to Azure Blob Storage with Archive tier optimization. Built as a containerized Linux application, it integrates seamlessly with automated recording workflows.

## Features

### Intelligent Video Download

- **Powered by yt-dlp**: Leverages the actively maintained YouTube downloader with enhanced features
- **Smart Archive Management**: Uses `archive.txt` to track downloads and prevent duplicates
- **Multi-Source Support**: Monitor multiple YouTube channels and playlists simultaneously
- **Optimal Format Selection**: Downloads non-DASH formats for best quality (avoids [Dynamic Adaptive Streaming over HTTP](https://en.wikipedia.org/wiki/Dynamic_Adaptive_Streaming_over_HTTP) which is optimized for streaming, not archival)
- **Configurable Format String**: Customize video format selection via environment variables
- **Rate Limiting**: Control maximum downloads per execution to manage bandwidth
- **Transcoding Guard**: Only downloads videos older than specified threshold to avoid incomplete transcodes

### Advanced Post-Processing

- **FFmpeg Integration**: Professional video processing pipeline
- **Thumbnail Embedding**: Attaches cover image directly into video container
- **Rich Metadata**: Embeds title, artist, date, and full description
- **Unified Container**: Outputs all videos in MKV format for consistency

### Cloud Storage Optimization

- **Azure Blob Storage Integration**: Direct upload with Azure SDK
- **Archive Tier Storage**: Automatically applies [Archive access tier](https://learn.microsoft.com/en-us/azure/storage/blobs/access-tiers-overview) for cost-effective long-term storage
- **Incremental Progress Tracking**: Updates archive index after each successful upload

### Performance & Reliability

- **Asynchronous Architecture**: .NET 8.0 async/await patterns throughout
- **Parallel Processing**: Each downloaded video immediately enters post-processing and upload pipeline
- **Multi-Threaded Execution**: Maximizes resource utilization across all available cores
- **Progress Persistence**: Tracks completed uploads in real-time; interruptions don't cause re-downloads
- **Resume Capability**: Automatically detects and uploads unfinished videos from previous runs

## Installation

Pull the latest container image from your preferred registry:

```bash
# GitHub Container Registry
podman pull ghcr.io/jim60105/backup-dl:latest

# Quay.io
podman pull quay.io/jim60105/backup-dl:latest

# Docker Hub
podman pull docker.io/jim60105/backup-dl:latest
```

## Configuration

### Required Environment Variables

| Variable | Description |
|----------|-------------|
| `AZURE_STORAGE_CONNECTION_STRING_VTUBER` | Azure Blob Storage connection string ([view documentation](https://learn.microsoft.com/en-us/azure/storage/common/storage-account-keys-manage?tabs=azure-portal#view-account-access-keys)) |
| `CHANNELS_IN_ARRAY` | JSON array of YouTube channel or playlist URLs to monitor |

### Optional Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `MAX_DOWNLOAD` | `10` | Maximum number of videos to download per execution |
| `DATE_BEFORE` | `2` | Only download videos older than N days (prevents incomplete transcodes) |
| `FORMAT` | `(bv*+ba/b)[protocol^=http][protocol!*=dash]` | yt-dlp format selector ([format selection syntax](https://github.com/yt-dlp/yt-dlp#format-selection)) |
| `SCYNCHRONOUS` | - | If set, runs in synchronous mode (use only on single-core systems) |

### Volume Mounts

| Host Path | Container Path | Description |
|-----------|----------------|-------------|
| `cookies.txt` | `/app/cookies.txt` | Optional YouTube authentication cookies for private/members-only content |

## Usage

### Podman

Store your Azure Storage connection string in an environment variable:

```bash
export AZURE_STORAGE_CONNECTION_STRING_VTUBER="DefaultEndpointsProtocol=https;AccountName=..."
```

Run the container with your configuration:

```bash
podman run --rm \
  --env CHANNELS_IN_ARRAY='["https://www.youtube.com/@channelname", "https://www.youtube.com/playlist?list=PLxxxxx"]' \
  --env AZURE_STORAGE_CONNECTION_STRING_VTUBER \
  --env MAX_DOWNLOAD="10" \
  ghcr.io/jim60105/backup-dl:latest
```

With authentication cookies:

```bash
podman run --rm \
  --env CHANNELS_IN_ARRAY='["https://www.youtube.com/@channelname"]' \
  --env AZURE_STORAGE_CONNECTION_STRING_VTUBER \
  --volume ./cookies.txt:/app/cookies.txt:ro \
  ghcr.io/jim60105/backup-dl:latest
```

### Kubernetes (Helm)

Deploy as a scheduled CronJob:

```bash
git clone --depth=1 https://github.com/jim60105/backup-dl.git
cd backup-dl/helm

# Edit values.yaml with your configuration
vim values.yaml

# Deploy to cluster
kubectl create namespace backup-dl
helm install backup-dl . --namespace backup-dl
```

The Helm chart configures:

- CronJob schedule (default: daily at 1 AM)
- SecurityContext with non-root user
- ConfigMap for cookies.txt
- Environment variable configuration

## Technical Details

- **Runtime**: .NET 8.0 with self-contained, trimmed deployment
- **Container Base**: `mcr.microsoft.com/dotnet/runtime-deps:8.0`
- **Architecture**: linux/amd64
- **User**: Non-root (UID 1654) with OpenShift compatibility
- **CI/CD**: GitHub Actions with multi-registry publishing and Trivy security scanning

## License

> **Dependencies:**  
> - Xabe.FFmpeg: Licensed under Agreement for non-commercial use  
> - YoutubeDLSharp: BSD 3-Clause License  
> - yt-dlp: Unlicensed

<img src="https://github.com/jim60105/backup-dl/assets/16995691/c15741ac-04f9-44e3-b97a-32ecb731c823" alt="gplv3" width="300" />

[GNU GENERAL PUBLIC LICENSE Version 3](LICENSE)

Copyright (C) 2021 Jim Chen <Jim@ChenJ.im>.

This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.

You should have received a copy of the GNU General Public License along with this program. If not, see <https://www.gnu.org/licenses/>.

## Related Resources

- [Blog Post: Building a YouTube Backup Server](https://blog.maki0419.com/2020/11/docker-youtube-dl-auto-recording-live-dl.html) (Traditional Chinese)
- [yt-dlp Documentation](https://github.com/yt-dlp/yt-dlp)
- [Azure Blob Storage Tiers](https://learn.microsoft.com/en-us/azure/storage/blobs/access-tiers-overview)
