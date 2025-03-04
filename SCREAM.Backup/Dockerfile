FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["SCREAM.Backup/SCREAM.Backup.csproj", "SCREAM.Backup/"]
RUN dotnet restore "SCREAM.Backup/SCREAM.Backup.csproj"
COPY . .
WORKDIR "/src/SCREAM.Backup"
RUN dotnet build "SCREAM.Backup.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "SCREAM.Backup.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SCREAM.Backup.dll"]
