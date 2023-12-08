#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:7.0-alpine AS base
WORKDIR /app
ENV AZURE_STORAGE_CONNECTION_STRING_VTUBER="ChangeThis"
ENV CHANNELS_IN_ARRAY="[\"https://www.youtube.com/channel/UCBC7vYFNQoGPupe5NxPG4Bw\"]"
ENV MAX_DOWNLOAD="10"
RUN apk add --no-cache --virtual build-deps musl-dev gcc g++ python3-dev &&\
    apk add --no-cache aria2 ffmpeg py3-pip &&\
    pip install --no-cache-dir --upgrade yt-dlp &&\
    apk del build-deps

FROM mcr.microsoft.com/dotnet/sdk:7.0-alpine AS build
WORKDIR /src
COPY ["backup-dl.csproj", "."]
RUN dotnet restore "backup-dl.csproj"
COPY . .
RUN dotnet build "backup-dl.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "backup-dl.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

RUN addgroup -g 1000 docker && \
    adduser -u 1000 -G docker -h /home/docker -s /bin/sh -D docker \
    && chown -R 1000:1000 .
USER docker

ENTRYPOINT ["dotnet", "backup-dl.dll"]