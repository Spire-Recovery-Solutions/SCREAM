# SCREAM

**S**ecure **C**ompress **R**estore **E**ncrypt **A**rchive **M**igrate  

A powerful utility for logically backing up, compressing, encrypting, and uploading MariaDB and MySQL databases to S3-compatible storage (e.g., Backblaze), with full support for granular restoration and migration.

## Overview

SCREAM breaks down database backups into modular components—schemas, tables, data, functions, views, and more—ensuring flexibility and reliability. Built with modern tools and wrapped in a Blazor WebAssembly UI, it runs seamlessly in a Docker container for easy deployment.

## Features

- **Logical Backups**: Extracts MariaDB/MySQL database components (schemas, tables, data, functions, views) for precise control.
- **Compression**: Uses `xz` to shrink backup files efficiently.
- **Encryption**: Secures backups with `openssl enc` for end-to-end protection.
- **S3 Upload**: Archives backups to Backblaze or any S3-compatible storage using AWSSDK.S3.
- **Restoration & Migration**: Rebuilds databases from granular components with ease.
- **Blazor WASM UI**: Provides an intuitive web interface to manage backup and restore operations.
- **Dockerized**: Runs in a lightweight Docker container for portability and consistency.

## Tech Stack

- **Backend**: .NET 8+  
- **Frontend**: Blazor WebAssembly  
- **CLI Utilities**:  
  - `openssl enc` (encryption)  
  - `xz` (compression)  
  - `mysql` (client v8.0.40, database interaction)  
- **Libraries**:  
  - **AWSSDK.S3**: S3-compatible storage integration  
  - **Dapper**: Lightweight ORM for database queries  
  - **CliWrap**: CLI tool invocation in .NET  
  - **MySqlConnector**: High-performance MySQL driver  
- **Container**: Docker  

## Prerequisites

- Docker installed on your system  
- .NET 8+ SDK (for local development outside Docker)  
- Access to a MariaDB or MySQL instance  
- S3-compatible storage account (e.g., Backblaze B2) with credentials  

## Getting Started

### Running with Docker

1. Clone the repository:
   ```bash
   git clone https://github.com/username/scream.git
   cd scream
   ```

2. Build and run the Docker container:
   ```bash
   docker build -t scream:latest .
   docker run -p 8080:80 scream:latest
   ```

3. Access the Blazor UI at http://localhost:8080

## Configuration

Update `appsettings.json` with your database connection string and S3 credentials:

```json
{
  "Database": {
    "ConnectionString": "Server=your-db;Database=your-schema;Uid=your-user;Pwd=your-password;"
  },
  "S3": {
    "AccessKey": "your-access-key",
    "SecretKey": "your-secret-key",
    "BucketName": "your-bucket",
    "Endpoint": "s3.us-west-000.backblazeb2.com"
  }
}
```

## Usage

### Backup
1. Open the Blazor UI
2. Configure your database and S3 settings
3. Select components to back up (e.g., schemas, tables, data)
4. SCREAM will compress, encrypt, and upload the backup to S3

### Restore
1. Load a backup from S3 via the UI
2. Choose components to restore or migrate
3. SCREAM decrypts, decompresses, and applies the data to your database

## Development

To build and run locally without Docker:

1. Install .NET 8+ SDK
2. Install CLI tools: openssl, xz, and mysql (v8.0.40)
3. Restore dependencies:
   ```bash
   dotnet restore
   ```
4. Run the app:
   ```bash
   dotnet run --project Scream.Web
   ```

## Contributing

Contributions are welcome! Feel free to open issues or submit pull requests for bug fixes, features, or improvements.

## License

SCREAM is licensed under the MIT License © 2025 [Your Name/Organization].

This project incorporates third-party dependencies with their respective licenses:

- .NET 8+ & Blazor WASM: MIT License
- AWSSDK.S3: Apache License 2.0
- Dapper: Apache License 2.0
- CliWrap: MIT License
- MySqlConnector: MIT License
- openssl enc: OpenSSL License (Apache-style)
- xz: Public Domain (or LGPL in some distributions)
- mysql (client v8.0.40): GNU General Public License v2 (GPLv2) with commercial options

Users must comply with the licenses of these dependencies when using, modifying, or distributing SCREAM. See individual dependency documentation for full license terms.
