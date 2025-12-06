# syntax=docker/dockerfile:1
ARG VERSION=EDGE
ARG RELEASE=0

########################################
# Base stage
########################################
FROM mcr.microsoft.com/dotnet/runtime-deps:8.0 AS base
ARG APP_UID=1654
ARG TARGETARCH
ARG TARGETVARIANT
WORKDIR /app

# Install aria2
RUN --mount=type=cache,id=apt-$TARGETARCH$TARGETVARIANT,sharing=locked,target=/var/cache/apt \
    --mount=type=cache,id=aptlists-$TARGETARCH$TARGETVARIANT,sharing=locked,target=/var/lib/apt/lists \
    apt-get update && apt-get install -y --no-install-recommends aria2

# Create directories with correct permissions (OpenShift compatible)
RUN install -d -m 775 -o "$APP_UID" -g 0 /etc/yt-dlp-plugins/bgutil-ytdlp-pot-provider && \
    install -d -m 775 -o "$APP_UID" -g 0 /deno-dir && \
    install -d -m 775 -o "$APP_UID" -g 0 /licenses && \
    install -d -m 775 -o "$APP_UID" -g 0 /data

# ffmpeg (statically compiled and UPX compressed)
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/static-ffmpeg-upx:8.0 /ffmpeg /usr/bin/
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/static-ffmpeg-upx:8.0 /ffprobe /usr/bin/

# dumb-init (signal handling)
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/static-ffmpeg-upx:8.0 /dumb-init /usr/bin/

# Copy POToken server (bgutil-pot)
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/bgutil-pot:latest /bgutil-pot /usr/bin/

# Copy POToken client plugin
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/bgutil-pot:latest /client /etc/yt-dlp-plugins/bgutil-ytdlp-pot-provider

# yt-dlp (using Linux build for Debian with glibc)
ADD --link --chown=$APP_UID:0 --chmod=775 https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux /usr/bin/yt-dlp

# Deno JS runtime for yt-dlp
ENV DENO_USE_CGROUPS=1 \
    DENO_DIR=/deno-dir/ \
    DENO_INSTALL_ROOT=/usr/local

ENV DENO_VERSION=2.5.6
COPY --link --chown=$APP_UID:0 --chmod=775 --from=docker.io/denoland/deno:bin-2.5.6 /deno /usr/bin/

# Copy licenses (OpenShift Policy)
COPY --link --chown=$APP_UID:0 --chmod=775 LICENSE /licenses/LICENSE

# Default environment variables
ENV AZURE_STORAGE_CONNECTION_STRING_VTUBER="ChangeThis" \
    CHANNELS_IN_ARRAY="[\"https://www.youtube.com/channel/UCBC7vYFNQoGPupe5NxPG4Bw\"]" \
    MAX_DOWNLOAD="10" \
    DATE_BEFORE="2"

########################################
# Debug stage
########################################
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS debug
ARG APP_UID=1654
ARG TARGETARCH
ARG TARGETVARIANT
WORKDIR /app

# Install aria2
RUN --mount=type=cache,id=apt-$TARGETARCH$TARGETVARIANT,sharing=locked,target=/var/cache/apt \
    --mount=type=cache,id=aptlists-$TARGETARCH$TARGETVARIANT,sharing=locked,target=/var/lib/apt/lists \
    apt-get update && apt-get install -y --no-install-recommends aria2

# Create directories with correct permissions
RUN install -d -m 775 -o "$APP_UID" -g 0 /etc/yt-dlp-plugins/bgutil-ytdlp-pot-provider && \
    install -d -m 775 -o "$APP_UID" -g 0 /deno-dir

# ffmpeg (statically compiled and UPX compressed)
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/static-ffmpeg-upx:8.0 /ffmpeg /usr/bin/
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/static-ffmpeg-upx:8.0 /ffprobe /usr/bin/

# Copy POToken server (bgutil-pot)
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/bgutil-pot:latest /bgutil-pot /usr/bin/

# Copy POToken client plugin
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/bgutil-pot:latest /client /etc/yt-dlp-plugins/bgutil-ytdlp-pot-provider

# yt-dlp (using Linux build for Debian with glibc)
ADD --link --chown=$APP_UID:0 --chmod=775 https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux /usr/bin/yt-dlp

# Deno JS runtime for yt-dlp
ENV DENO_USE_CGROUPS=1 \
    DENO_DIR=/deno-dir/ \
    DENO_INSTALL_ROOT=/usr/local

ENV DENO_VERSION=2.5.6
COPY --link --chown=$APP_UID:0 --chmod=775 --from=docker.io/denoland/deno:bin-2.5.6 /deno /usr/bin/

########################################
# Build stage
########################################
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH
WORKDIR /src

# Copy project file and restore dependencies
COPY ["backup-dl.csproj", "."]
RUN dotnet restore -a "$TARGETARCH" "backup-dl.csproj"

########################################
# Publish stage
########################################
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH
COPY . .
RUN dotnet publish "backup-dl.csproj" -a "$TARGETARCH" -c "$BUILD_CONFIGURATION" -o /app/publish --self-contained true

########################################
# Final stage
########################################
FROM base AS final
ARG APP_UID=1654
ARG VERSION
ARG RELEASE

ENV PATH="/app:$PATH"

# Working directory
WORKDIR /data

# Persistent directories
VOLUME ["/data", "/tmp"]

# Copy application binary
COPY --from=publish --chown=$APP_UID:0 /app/publish/backup-dl /app/backup-dl

# Switch to non-root user
USER $APP_UID

# Signal handling
STOPSIGNAL SIGINT

# Use dumb-init as PID 1 to handle signals properly
ENTRYPOINT ["dumb-init", "--", "/app/backup-dl"]

# Metadata labels
LABEL name="backup-dl" \
      vendor="jim60105" \
      maintainer="jim60105" \
      url="https://github.com/jim60105/backup-dl" \
      version=${VERSION} \
      release=${RELEASE} \
      io.k8s.display-name="YouTube Backup Downloader" \
      summary="A .NET Core application to backup YouTube videos to Azure Blob Storage" \
      description="This application checks YouTube channels and playlists, and backs up videos to Azure Blob Storage. Built with yt-dlp and ffmpeg. For more information, visit: https://github.com/jim60105/backup-dl"
