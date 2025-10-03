# syntax=docker/dockerfile:1
ARG APP_UID=1654
ARG YT_DLP_VERSION=2025.08.22

#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

########################################
# Base image for yt-dlp
########################################
FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-alpine AS base
ARG APP_UID
ARG YT_DLP_VERSION
WORKDIR /app

# Install aria2
RUN apk add --no-cache aria2

# Create directories with correct permissions
RUN install -d -m 775 -o $APP_UID -g 0 /etc/yt-dlp-plugins/bgutil-ytdlp-pot-provider

# ffmpeg (statically compiled and UPX compressed)
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/static-ffmpeg-upx:8.0 /ffmpeg /usr/bin/
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/static-ffmpeg-upx:8.0 /ffprobe /usr/bin/

# Copy POToken server (bgutil-pot) from ghcr.io/jim60105/bgutil-pot:latest
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/bgutil-pot:latest /bgutil-pot /usr/bin/

# Copy POToken client plugin from ghcr.io/jim60105/bgutil-pot:latest
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/bgutil-pot:latest /client /etc/yt-dlp-plugins/bgutil-ytdlp-pot-provider

# Download pre-built yt-dlp binary
ADD --link --chown=$APP_UID:0 --chmod=775 https://github.com/yt-dlp/yt-dlp/releases/download/${YT_DLP_VERSION}/yt-dlp_linux /usr/bin/yt-dlp

ENV AZURE_STORAGE_CONNECTION_STRING_VTUBER="ChangeThis"
ENV CHANNELS_IN_ARRAY="[\"https://www.youtube.com/channel/UCBC7vYFNQoGPupe5NxPG4Bw\"]"
ENV MAX_DOWNLOAD="10"
ENV DATE_BEFORE="2"

########################################
# Debug image
########################################
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS debug
ARG APP_UID
ARG YT_DLP_VERSION
WORKDIR /app

# Install aria2
RUN apk add --no-cache aria2

# Create directories with correct permissions
RUN install -d -m 775 -o $APP_UID -g 0 /etc/yt-dlp-plugins/bgutil-ytdlp-pot-provider

# ffmpeg (statically compiled and UPX compressed)
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/static-ffmpeg-upx:8.0 /ffmpeg /usr/bin/
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/static-ffmpeg-upx:8.0 /ffprobe /usr/bin/

# Copy POToken server (bgutil-pot) from ghcr.io/jim60105/bgutil-pot:latest
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/bgutil-pot:latest /bgutil-pot /usr/bin/

# Copy POToken client plugin from ghcr.io/jim60105/bgutil-pot:latest
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/bgutil-pot:latest /client /etc/yt-dlp-plugins/bgutil-ytdlp-pot-provider

# Download pre-built yt-dlp binary
ADD --link --chown=$APP_UID:0 --chmod=775 https://github.com/yt-dlp/yt-dlp/releases/download/${YT_DLP_VERSION}/yt-dlp_linux /usr/bin/yt-dlp

########################################
# Build .NET
########################################
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH
WORKDIR /src

COPY ["backup-dl.csproj", "."]
RUN dotnet restore -a $TARGETARCH "backup-dl.csproj"

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
COPY . .
RUN dotnet publish "backup-dl.csproj" -a $TARGETARCH -c $BUILD_CONFIGURATION -o /app/publish --self-contained true

########################################
# Final image
########################################
FROM base AS final
ARG APP_UID

ENV PATH="/app:$PATH"

# Create directories with correct permissions
RUN install -d -m 775 -o $APP_UID -g 0 /app

COPY --from=publish --chown=$APP_UID:0 /app/publish/backup-dl /app/backup-dl

USER $APP_UID

ENTRYPOINT ["/app/backup-dl"]