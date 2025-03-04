# 😱 SCREAM

**S**ecure **C**ompress **R**estore **E**ncrypt **A**rchive **M**igrate  

> Your friendly neighborhood database superhero for MariaDB and MySQL backups!

## 🔍 What's SCREAM?

SCREAM is your all-in-one solution for protecting, compressing, and safely storing your precious database data. Think of it as a Swiss Army knife for database backups that actually cares about your peace of mind!

Instead of creating monolithic backup files that are a pain to work with, SCREAM intelligently breaks down your database into modular components:

* 📊 **Schema** - Database structure and definitions
* 💾 **Data** - The actual table contents
* ⚙️ **FunctionSp** - Functions and stored procedures
* 🔍 **Views** - Virtual tables for simplified access
* ⚡ **Triggers** - Automated responses to data events
* 🕒 **Events** - Scheduled tasks and automation

This granular approach gives you incredible flexibility when you need to restore or migrate your data.

### 🧠 How It Works

Under the hood, SCREAM uses a powerful combination of native MySQL tools and modern cloud technologies:

1. 🔍 **Smart Discovery** - SCREAM identifies all database objects that need backing up
2. 🚅 **Parallel Processing** - Multiple backup tasks run simultaneously for maximum speed
3. 📦 **Optimized Compression** - The `xz` utility compresses your data with configurable threads
4. 🔐 **Strong Encryption** - AES-256-CBC encryption with 20,000 PBKDF2 iterations
5. ☁️ **Intelligent Uploading** - Automatic switching between single and multi-part uploads
6. 📊 **Memory Efficiency** - Advanced memory recycling prevents out-of-memory errors

## ✨ Why SCREAM?

* 🧩 **Modular backups** - Restore only what you need (Schema, Data, FunctionSp, Views, Triggers, Events)
* 🔐 **Security first** - End-to-end encryption keeps your data safe
* 💾 **Space efficient** - Advanced compression saves storage costs
* ☁️ **Cloud ready** - Direct upload to your favorite S3 storage
* 🌈 **User friendly** - Clean web interface makes complex operations simple
* 🐳 **Zero friction** - Jump right in with our Docker container

## 🚀 Features

### 🌟 Core Features
* 🛡️ **Rock-solid Logical Backups**: Extract database elements by component (Schema, Data, FunctionSp, Views, Triggers, Events)
* 🔒 **Bank-grade Encryption**: Military-strength protection with `openssl enc` (AES-256-CBC with PBKDF2)
* 📦 **Super Compression**: Save space with efficient `xz` compression
* ☁️ **Cloud Archiving**: Automatic uploads to Backblaze B2 or any S3 storage
* 🔄 **Surgical Restoration**: Rebuild specific database components when needed
* 🚢 **Effortless Migration**: Move databases between environments without stress
* 🖥️ **Beautiful Interface**: Intuitive Blazor WASM UI makes management easy
* 📱 **Run Anywhere**: Lightweight Docker container works everywhere

### 🔥 Advanced Technical Features

* 🧵 **Multi-threaded Operations**: Parallel processing for each database component
* 🚄 **Smart Upload Logic**: Automatically switches between single-part and multi-part uploads
* 🧠 **Memory-efficient Processing**: Recyclable memory streams prevent out-of-memory errors
* 📊 **Detailed Progress Tracking**: Real-time reporting on backup status
* 🌡️ **Performance Metrics**: Track backup speed and processing time
* 🔄 **Comprehensive Error Handling**: Automatic cleanup of failed uploads

## 🧰 Tech Stack

* 🔷 **Backend**: .NET 8+ (fast, modern, and reliable)
* 🌐 **Frontend**: Blazor WebAssembly (smooth UI without JavaScript headaches)
* 🔧 **CLI Tools**:  
  * `openssl enc` - The gold standard in encryption
  * `xz` - The compression champion
  * `mysql` client - Rock-solid database interaction
* 📚 **Libraries**:  
  * **AWSSDK.S3** - Reliable cloud storage integration
  * **Dapper** - Lightning-fast database queries
  * **CliWrap** - Elegant command-line operations
  * **MySqlConnector** - Bulletproof database connectivity
* 🐳 **Container**: Docker (because who has time for dependency hell?)

## 📋 Before You Start

You'll need:
* 🐳 Docker installed on your system  
* 🗄️ Access to a MariaDB or MySQL database
* ☁️ S3-compatible storage account credentials
* 🔷 .NET 8+ SDK (only if you're developing outside Docker)

### 🛠️ System Requirements

SCREAM is designed to be efficient with your resources, but for best performance:

* 🧠 **Memory**: 2GB minimum, 4GB+ recommended for large databases
* 💽 **CPU**: Multi-core processor (SCREAM scales with available cores!)
* 🔄 **Network**: Good upload bandwidth for cloud storage transfer

## 🚀 Quick Start (I'm in a hurry!)

```bash
# Pull our Docker image
docker pull ghcr.io/username/scream:latest

# Run it (will use demo mode with sample settings)
docker run -p 8080:80 --name scream-quick ghcr.io/username/scream:latest

# Visit http://localhost:8080 and you're in!
```

## 📥 Full Installation

### 🐳 The Docker Way (Recommended)

1. Clone the repo:
   ```bash
   git clone https://github.com/username/scream.git
   cd scream
   ```

2. Build and launch:
   ```bash
   docker build -t scream:latest .
   docker run -p 8080:80 -v $(pwd)/config:/app/config scream:latest
   ```

3. Visit http://localhost:8080 and start SCREAMing! 😱

### 🔨 The DIY Way

Want to build from source? We've got you covered:

1. Install the prerequisites:
   * .NET 8+ SDK
   * openssl, xz, and mysql client tools

2. Clone and prep:
   ```bash
   git clone https://github.com/username/scream.git
   cd scream
   dotnet restore
   ```

3. Launch:
   ```bash
   dotnet run --project Scream.Web
   ```

4. Visit http://localhost:5000 and enjoy!

## ⚙️ Configuration

Create a `config/appsettings.json` file with your settings:

```json
{
  "MySqlBackup": {
    "HostName": "your-db-server",
    "UserName": "your-user",
    "Password": "your-password", 
    "EncryptionKey": "your-very-secure-encryption-key",
    "BucketName": "your-backup-bucket",
    "BackupFolder": "my-backups",
    "MaxPacketSize": "1073741824",
    "Threads": "4",
    "BackblazeB2": {
      "ServiceURL": "s3.us-west-000.backblazeb2.com",
      "AccessKey": "your-b2-access-key",
      "SecretKey": "your-b2-secret-key"
    }
  }
}
```

Pro tip: All settings can be configured through environment variables too!

```bash
export MYSQL_BACKUP_HOSTNAME="your-db-server"
export MYSQL_BACKUP_USERNAME="your-user"
export MYSQL_BACKUP_PASSWORD="your-password"
export MYSQL_BACKUP_ENCRYPTION_KEY="your-very-secure-encryption-key"
export MYSQL_BACKUP_BUCKET_NAME="your-backup-bucket"
export MYSQL_BACKUP_FOLDER="my-backups"
export MYSQL_BACKUP_MAX_PACKET_SIZE="1073741824"
export MYSQL_BACKUP_THREADS="4"
export MYSQL_BACKUP_B2_SERVICE_URL="s3.us-west-000.backblazeb2.com"
export MYSQL_BACKUP_B2_ACCESS_KEY="your-b2-access-key" 
export MYSQL_BACKUP_B2_SECRET_KEY="your-b2-secret-key"
```

## 📚 Using SCREAM

### 💾 Backing Up Your Data

1. Open the web UI and go to the "Backup" tab
2. Select your database connection (or enter a new one)
3. Choose what to include (Schema, Data, FunctionSp, Views, Triggers, Events)
4. Set encryption options (we recommend AES-256-CBC)
5. Pick your S3 bucket details
6. Click "Start Backup" and watch the magic happen!

#### 🧙‍♂️ What Happens Behind the Scenes

When you click that "Start Backup" button:

1. SCREAM queries your database to discover all objects needing backup
2. Each component is processed in parallel (using the thread count you configured)
3. For each component:
   - The appropriate `mysqldump` command is executed with optimized flags
   - Output is piped directly to `xz` for compression
   - Compressed data is piped to `openssl` for encryption
   - Encrypted data is streamed directly to S3 storage
4. Large backups are automatically split into multi-part uploads
5. Detailed logs track the progress of each step

### 🔄 Restoring Your Data

1. Navigate to the "Restore" tab
2. Browse your S3 buckets or upload a local backup
3. Select which components to restore
4. Choose destination database
5. Hit "Start Restore" and grab a coffee while SCREAM works

### 🚚 Migrating Between Environments

1. Go to the "Migration" tab
2. Set up your source and destination connections
3. Select components to migrate
4. Click "Start Migration" and relax

## 🔧 Troubleshooting

### Common Issues

* **Can't connect to database?** Double-check your connection string and ensure network access
* **S3 uploads failing?** Verify your credentials and bucket permissions
* **Slow backups?** Try adjusting:
  * The number of threads (`MYSQL_BACKUP_THREADS`)
  * The compression level in the xz command (currently set to `-3`)
  * The maximum packet size (`MYSQL_BACKUP_MAX_PACKET_SIZE`)
* **Memory errors?** Adjust the RecyclableMemoryStreamManager settings or decrease thread count
* **Timeouts on large tables?** Increase the connection timeout in your MySQL settings

### 📊 Performance Tuning

* **CPU-bound?** Increase the thread count to match your available cores
* **Network-bound?** Adjust compression level (higher = smaller files but slower processing)
* **Memory-bound?** Adjust the MaximumBufferSize and pool settings

Need more help? Open an issue on GitHub or check our [Wiki](https://github.com/username/scream/wiki)!

## 🤝 Contributing

We'd love your help making SCREAM even better! Here's how:

1. Fork the repository
2. Create your feature branch (`git checkout -b amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin amazing-feature`)
5. Open a Pull Request

Even small improvements are welcome!

## 📜 License

SCREAM is licensed under the MIT License © 2025 [Your Name/Organization].

This project stands on the shoulders of giants with these dependencies:
* .NET 8+ & Blazor WASM: MIT License
* AWSSDK.S3: Apache License 2.0
* Dapper: Apache License 2.0
* CliWrap: MIT License
* MySqlConnector: MIT License
* openssl enc: OpenSSL License
* xz: Public Domain/LGPL
* mysql client: GPL v2

---

Made with ❤️ by database enthusiasts who've lost data one too many times.

**Remember:** The best backup is the one you actually have when you need it!
