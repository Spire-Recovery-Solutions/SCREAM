# Use non-chiseled base image to get shell access
FROM mcr.microsoft.com/dotnet/aspnet:8.0-jammy AS base
WORKDIR /app
EXPOSE 8080

# Install curl for healthchecks
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["SCREAM.Service.Api/SCREAM.Service.Api.csproj", "SCREAM.Service.Api/"]
RUN dotnet restore "SCREAM.Service.Api/SCREAM.Service.Api.csproj"
COPY . ./
WORKDIR "/src/SCREAM.Service.Api"
RUN dotnet build "SCREAM.Service.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "SCREAM.Service.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

# Healthcheck with proper timeout and retries
HEALTHCHECK --interval=10s --timeout=5s --start-period=15s --retries=3 \
    CMD curl --fail http://localhost:8080 || exit 1

ENTRYPOINT ["dotnet", "SCREAM.Service.Api.dll"]