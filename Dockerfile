#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

### Base image for yt-dlp
FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-alpine AS base
ARG UID=1001
ARG YT_DLP_VERSION=2025.08.22
WORKDIR /app

RUN apk add --no-cache aria2 ffmpeg && \
    mkdir -p /usr/bin /etc/yt-dlp-plugins/bgutil-ytdlp-pot-provider && \
    chown -R $UID:0 /etc/yt-dlp-plugins && \
    chmod -R 775 /etc/yt-dlp-plugins

# Copy POToken server (bgutil-pot) from ghcr.io/jim60105/bgutil-pot:latest
COPY --link --chown=$UID:0 --chmod=775 --from=ghcr.io/jim60105/bgutil-pot:latest /bgutil-pot /usr/bin/

# Copy POToken client plugin from ghcr.io/jim60105/bgutil-pot:latest
COPY --link --chown=$UID:0 --chmod=775 --from=ghcr.io/jim60105/bgutil-pot:latest /client /etc/yt-dlp-plugins/bgutil-ytdlp-pot-provider

# Download pre-built yt-dlp binary
ADD --link --chown=$UID:0 --chmod=775 https://github.com/yt-dlp/yt-dlp/releases/download/${YT_DLP_VERSION}/yt-dlp_linux /usr/bin/yt-dlp

ENV AZURE_STORAGE_CONNECTION_STRING_VTUBER="ChangeThis"
ENV CHANNELS_IN_ARRAY="[\"https://www.youtube.com/channel/UCBC7vYFNQoGPupe5NxPG4Bw\"]"
ENV MAX_DOWNLOAD="10"
ENV DATE_BEFORE="2"
ENV PATH="/usr/bin:$PATH"

### Debug image
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS debug
ARG UID=1001
ARG YT_DLP_VERSION=2025.08.22
WORKDIR /app

RUN apk add --no-cache aria2 ffmpeg && \
    mkdir -p /usr/bin /etc/yt-dlp-plugins/bgutil-ytdlp-pot-provider && \
    chown -R $UID:0 /etc/yt-dlp-plugins && \
    chmod -R 775 /etc/yt-dlp-plugins

# Copy POToken server (bgutil-pot) from ghcr.io/jim60105/bgutil-pot:latest
COPY --link --chown=$UID:0 --chmod=775 --from=ghcr.io/jim60105/bgutil-pot:latest /bgutil-pot /usr/bin/

# Copy POToken client plugin from ghcr.io/jim60105/bgutil-pot:latest
COPY --link --chown=$UID:0 --chmod=775 --from=ghcr.io/jim60105/bgutil-pot:latest /client /etc/yt-dlp-plugins/bgutil-ytdlp-pot-provider

# Download pre-built yt-dlp binary
ADD --link --chown=$UID:0 --chmod=775 https://github.com/yt-dlp/yt-dlp/releases/download/${YT_DLP_VERSION}/yt-dlp_linux /usr/bin/yt-dlp

ENV PATH="/usr/bin:$PATH"

### Build .NET
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

### Final image
FROM base AS final
ARG UID=1001

ENV PATH="/app:$PATH"

RUN mkdir -p /app && chown -R $UID:0 /app && chmod u+rwx /app
COPY --from=publish --chown=$UID:0 /app/publish/backup-dl /app/backup-dl

USER $UID

ENTRYPOINT ["/app/backup-dl"]