FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["SCREAM.Service.Backup/SCREAM.Service.Backup.csproj", "SCREAM.Service.Backup/"]
RUN dotnet restore "SCREAM.Service.Backup/SCREAM.Service.Backup.csproj"
COPY . .
WORKDIR "/src/SCREAM.Service.Backup"
RUN dotnet build "SCREAM.Service.Backup.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "SCREAM.Service.Backup.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SCREAM.Service.Backup.dll"]
