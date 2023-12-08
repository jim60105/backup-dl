#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

### Base image for yt-dlp
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS base
WORKDIR /app
RUN apk add --no-cache aria2 ffmpeg python3 &&\
    apk add --no-cache --virtual build-deps musl-dev gcc g++ python3-dev py3-pip &&\
    python3 -m venv /venv && \
    source /venv/bin/activate && \
    pip install --no-cache-dir yt-dlp && \
    pip uninstall -y setuptools pip && \
    apk del build-deps

ENV AZURE_STORAGE_CONNECTION_STRING_VTUBER="ChangeThis"
ENV CHANNELS_IN_ARRAY="[\"https://www.youtube.com/channel/UCBC7vYFNQoGPupe5NxPG4Bw\"]"
ENV MAX_DOWNLOAD="10"
ENV PATH="/venv/bin:$PATH"

### Build .NET
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["backup-dl.csproj", "."]
RUN dotnet restore "backup-dl.csproj"

COPY . .
RUN dotnet build "backup-dl.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
COPY . .
ARG BUILD_CONFIGURATION=Release
ARG TARGETPLATFORM
RUN dotnet publish "backup-dl.csproj" -c $BUILD_CONFIGURATION -o /app/publish

### Final image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

RUN chown -R 1001:1001 /app
USER 1001
ENTRYPOINT ["dotnet", "backup-dl.dll"]