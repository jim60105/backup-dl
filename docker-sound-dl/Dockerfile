#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:5.0 AS base
WORKDIR /app
ENV AZURE_STORAGE_CONNECTION_STRING="ChangeThis"
ENV CHANNELS_IN_ARRAY="[\"https://www.youtube.com/channel/UCuy-kZJ7HWwUU-eKv0zUZFQ\", \"https://www.youtube.com/channel/UCBC7vYFNQoGPupe5NxPG4Bw\"]"
RUN  apt-get update \
        && apt-get -y install software-properties-common \
        && apt-get update \
        && apt-get -y install python-pip aria2 ffmpeg \
        && rm -rf /var/lib/apt/lists/* \
        && pip install youtube-dl

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["docker-sound-dl/docker-sound-dl.csproj", "docker-sound-dl/"]
RUN dotnet restore "docker-sound-dl/docker-sound-dl.csproj"
COPY . .
WORKDIR "/src/docker-sound-dl"
RUN dotnet build "docker-sound-dl.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "docker-sound-dl.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "docker-sound-dl.dll"]