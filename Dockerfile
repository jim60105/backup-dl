# syntax=docker/dockerfile:1
ARG APP_UID=1654

#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

########################################
# Base image for yt-dlp
########################################
FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-alpine AS base
ARG APP_UID
WORKDIR /app

# Install aria2
RUN apk add --no-cache aria2

COPY --link --chown=$APP_UID:0 --chmod=775 --from=docker.io/denoland/deno:distroless-2.5.6 /lib/*-linux-gnu/* /usr/local/lib/

# Create directories with correct permissions
RUN install -d -m 775 -o $APP_UID -g 0 /etc/yt-dlp-plugins/bgutil-ytdlp-pot-provider && \
    install -d -m 775 -o $APP_UID -g 0 /deno-dir && \
    install -d -m 775 -o $APP_UID -g 0 /lib64 && \
    ln -s /usr/local/lib/ld-linux-* /lib64/

# ffmpeg (statically compiled and UPX compressed)
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/static-ffmpeg-upx:8.0 /ffmpeg /usr/bin/
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/static-ffmpeg-upx:8.0 /ffprobe /usr/bin/

# dumb-init
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/static-ffmpeg-upx:8.0 /dumb-init /usr/bin/

# Copy POToken server (bgutil-pot) from ghcr.io/jim60105/bgutil-pot:latest
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/bgutil-pot:latest /bgutil-pot /usr/bin/

# Copy POToken client plugin from ghcr.io/jim60105/bgutil-pot:latest
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/bgutil-pot:latest /client /etc/yt-dlp-plugins/bgutil-ytdlp-pot-provider

# yt-dlp (using musllinux build for compatibility with musl libc from Alpine)
ADD --link --chown=$APP_UID:0 --chmod=775 https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_musllinux /usr/bin/yt-dlp

# Deno JS runtime for yt-dlp
ENV LD_LIBRARY_PATH="/usr/local/lib"
ENV DENO_USE_CGROUPS=1
ENV DENO_DIR=/deno-dir/
ENV DENO_INSTALL_ROOT=/usr/local

ARG DENO_VERSION
ENV DENO_VERSION=2.5.6
COPY --link --chown=$APP_UID:0 --chmod=775 --from=docker.io/denoland/deno:distroless-2.5.6 /bin/deno /usr/bin/

ENV AZURE_STORAGE_CONNECTION_STRING_VTUBER="ChangeThis"
ENV CHANNELS_IN_ARRAY="[\"https://www.youtube.com/channel/UCBC7vYFNQoGPupe5NxPG4Bw\"]"
ENV MAX_DOWNLOAD="10"
ENV DATE_BEFORE="2"

########################################
# Debug image
########################################
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS debug
ARG APP_UID
WORKDIR /app

# Install aria2
RUN apk add --no-cache aria2

COPY --link --chown=$APP_UID:0 --chmod=775 --from=docker.io/denoland/deno:distroless-2.5.6 /lib/*-linux-gnu/* /usr/local/lib/

# Create directories with correct permissions
RUN install -d -m 775 -o $APP_UID -g 0 /etc/yt-dlp-plugins/bgutil-ytdlp-pot-provider && \
    install -d -m 775 -o $APP_UID -g 0 /deno-dir && \
    install -d -m 775 -o $APP_UID -g 0 /lib64 && \
    ln -s /usr/local/lib/ld-linux-* /lib64/

# ffmpeg (statically compiled and UPX compressed)
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/static-ffmpeg-upx:8.0 /ffmpeg /usr/bin/
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/static-ffmpeg-upx:8.0 /ffprobe /usr/bin/

# Copy POToken server (bgutil-pot) from ghcr.io/jim60105/bgutil-pot:latest
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/bgutil-pot:latest /bgutil-pot /usr/bin/

# Copy POToken client plugin from ghcr.io/jim60105/bgutil-pot:latest
COPY --link --chown=$APP_UID:0 --chmod=775 --from=ghcr.io/jim60105/bgutil-pot:latest /client /etc/yt-dlp-plugins/bgutil-ytdlp-pot-provider

# yt-dlp (using musllinux build for compatibility with musl libc from Alpine)
ADD --link --chown=$APP_UID:0 --chmod=775 https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_musllinux /usr/bin/yt-dlp

ENV LD_LIBRARY_PATH="/usr/local/lib${LD_LIBRARY_PATH:+:${LD_LIBRARY_PATH}}"
ENV DENO_USE_CGROUPS=1
ENV DENO_DIR=/deno-dir/
ENV DENO_INSTALL_ROOT=/usr/local

ARG DENO_VERSION
ENV DENO_VERSION=2.5.6
COPY --link --chown=$APP_UID:0 --chmod=775 --from=docker.io/denoland/deno:distroless-2.5.6 /bin/deno /usr/bin/

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

# Use dumb-init as PID 1 to handle signals properly
ENTRYPOINT [ "dumb-init", "--", "/app/backup-dl"]
