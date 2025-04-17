# Use regular (non-chiseled) .NET 8.0 with Ubuntu 22.04 (jammy) runtime for base stage
FROM mcr.microsoft.com/dotnet/runtime:8.0-jammy AS base
WORKDIR /app
EXPOSE 5000
# Install MySQL Client and 7-Zip for Ubuntu 22.04 (jammy)
RUN apt-get update && apt-get install -y wget lsb-release xz-utils curl p7zip-full && \
    wget -q https://downloads.mysql.com/archives/get/p/23/file/mysql-common_8.0.40-1ubuntu22.04_amd64.deb && \
    wget -q https://downloads.mysql.com/archives/get/p/23/file/mysql-community-client-core_8.0.40-1ubuntu22.04_amd64.deb && \
    wget -q https://downloads.mysql.com/archives/get/p/23/file/mysql-community-client-plugins_8.0.40-1ubuntu22.04_amd64.deb && \
    wget -q https://downloads.mysql.com/archives/get/p/23/file/mysql-community-client_8.0.40-1ubuntu22.04_amd64.deb && \
    dpkg -i mysql-common_8.0.40-1ubuntu22.04_amd64.deb \
            mysql-community-client-plugins_8.0.40-1ubuntu22.04_amd64.deb \
            mysql-community-client-core_8.0.40-1ubuntu22.04_amd64.deb \
            mysql-community-client_8.0.40-1ubuntu22.04_amd64.deb || true && \
    apt-get install -f -y && \
    rm -f *.deb && \
    apt-get clean

# Build stage: Use the full SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Copy all project files into the container
COPY . .
# Change to the service directory and restore dependencies
WORKDIR /src/SCREAM.Service.Restore
RUN dotnet restore
# Build the service
RUN dotnet build "./SCREAM.Service.Restore.csproj" -c $BUILD_CONFIGURATION -o /app/build
# Publish stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./SCREAM.Service.Restore.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
# Final runtime image (using the non-chiseled runtime image)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# Set environment variables
ENV ASPNETCORE_URLS=http://+:5000
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV DOTNET_EnableDiagnostics=0
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
ENTRYPOINT ["dotnet", "SCREAM.Service.Restore.dll"]