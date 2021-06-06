#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:5.0-alpine AS base
WORKDIR /app
ENV AZURE_STORAGE_CONNECTION_STRING_VTUBER="ChangeThis"
ENV CHANNELS_IN_ARRAY="[\"https://www.youtube.com/channel/UCBC7vYFNQoGPupe5NxPG4Bw\"]"
ENV MAX_DOWNLOAD="10"
RUN apk --update add --no-cache aria2 ffmpeg youtube-dl

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["backup-dl.csproj", "backup-dl/"]
RUN dotnet restore "backup-dl/backup-dl.csproj"
COPY . .
WORKDIR "/src/backup-dl"
RUN dotnet build "backup-dl.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "backup-dl.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "backup-dl.dll"]