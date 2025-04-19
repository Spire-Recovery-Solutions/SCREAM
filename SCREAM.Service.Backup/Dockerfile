# Use runtime image for base
FROM mcr.microsoft.com/dotnet/runtime:8.0-jammy AS base
WORKDIR /app

# Use SDK image for build
FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
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

# Configure environment variables 
ENV DOTNET_RUNNING_IN_CONTAINER=true 
ENV DOTNET_EnableDiagnostics=0 
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

ENTRYPOINT ["dotnet", "SCREAM.Service.Backup.dll"]