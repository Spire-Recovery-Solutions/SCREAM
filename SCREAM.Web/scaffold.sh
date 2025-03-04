#!/bin/bash

# SCREAM - Blazor WebAssembly Project Scaffolding Script
# This script creates the necessary folder and file structure for the SCREAM project

# Base directories
mkdir -p Pages/Backup
mkdir -p Pages/Restore
mkdir -p Pages/Migration
mkdir -p Pages/DatabaseManagement/Connections
mkdir -p Pages/DatabaseManagement/Databases
mkdir -p Pages/Components/{Schema,Data,FunctionSp,Views,Triggers,Events}
mkdir -p Pages/Storage/{S3,BackblazeB2,Local}
mkdir -p Pages/Settings/{General,Encryption,Compression,Performance}
mkdir -p Pages/Logs
mkdir -p Pages/About

mkdir -p Shared/Components
mkdir -p Services
mkdir -p Models
mkdir -p Data
mkdir -p Utilities

# Create Dashboard page
cat > Pages/Index.razor << 'EOF'
@page "/"

<PageTitle>Dashboard - SCREAM</PageTitle>

<MudText Typo="Typo.h3" Class="mb-4">Dashboard</MudText>

<MudGrid>
    <MudItem xs="12" sm="6" md="3">
        <MudPaper Class="pa-4" Elevation="3">
            <MudText Typo="Typo.h5">Databases</MudText>
            <MudText Typo="Typo.h3">0</MudText>
        </MudPaper>
    </MudItem>
    <MudItem xs="12" sm="6" md="3">
        <MudPaper Class="pa-4" Elevation="3">
            <MudText Typo="Typo.h5">Backups</MudText>
            <MudText Typo="Typo.h3">0</MudText>
        </MudPaper>
    </MudItem>
    <MudItem xs="12" sm="6" md="3">
        <MudPaper Class="pa-4" Elevation="3">
            <MudText Typo="Typo.h5">Storage Used</MudText>
            <MudText Typo="Typo.h3">0 GB</MudText>
        </MudPaper>
    </MudItem>
    <MudItem xs="12" sm="6" md="3">
        <MudPaper Class="pa-4" Elevation="3">
            <MudText Typo="Typo.h5">Active Jobs</MudText>
            <MudText Typo="Typo.h3">0</MudText>
        </MudPaper>
    </MudItem>
</MudGrid>

<MudText Typo="Typo.h5" Class="mt-8 mb-4">Recent Activity</MudText>
<MudPaper Class="pa-4">
    <MudText>No recent activity</MudText>
</MudPaper>
EOF

# Create Backup page
cat > Pages/Backup/Index.razor << 'EOF'
@page "/backup"

<PageTitle>Backup - SCREAM</PageTitle>

<MudText Typo="Typo.h3" Class="mb-4">Database Backup</MudText>

<MudPaper Class="pa-4 mb-4">
    <MudForm>
        <MudSelect T="string" Label="Database Connection" AnchorOrigin="Origin.BottomCenter">
            <MudSelectItem Value="@("default")">Default Connection</MudSelectItem>
        </MudSelect>
        
        <MudSelect T="string" Label="Database Name" AnchorOrigin="Origin.BottomCenter" Class="mt-3">
            <MudSelectItem Value="@("sample")">sample_db</MudSelectItem>
        </MudSelect>
        
        <MudText Typo="Typo.h6" Class="mt-4 mb-2">Components to Backup</MudText>
        <MudGrid>
            <MudItem xs="12" sm="6" md="4">
                <MudCheckBox T="bool" @bind-Checked="backupSchema" Label="Schema" Color="Color.Primary" />
            </MudItem>
            <MudItem xs="12" sm="6" md="4">
                <MudCheckBox T="bool" @bind-Checked="backupData" Label="Data" Color="Color.Primary" />
            </MudItem>
            <MudItem xs="12" sm="6" md="4">
                <MudCheckBox T="bool" @bind-Checked="backupFunctionSp" Label="Functions & SPs" Color="Color.Primary" />
            </MudItem>
            <MudItem xs="12" sm="6" md="4">
                <MudCheckBox T="bool" @bind-Checked="backupViews" Label="Views" Color="Color.Primary" />
            </MudItem>
            <MudItem xs="12" sm="6" md="4">
                <MudCheckBox T="bool" @bind-Checked="backupTriggers" Label="Triggers" Color="Color.Primary" />
            </MudItem>
            <MudItem xs="12" sm="6" md="4">
                <MudCheckBox T="bool" @bind-Checked="backupEvents" Label="Events" Color="Color.Primary" />
            </MudItem>
        </MudGrid>
        
        <MudText Typo="Typo.h6" Class="mt-4 mb-2">Backup Options</MudText>
        <MudGrid>
            <MudItem xs="12" sm="6">
                <MudNumericField @bind-Value="compressionLevel" Label="Compression Level" Min="1" Max="9" />
            </MudItem>
            <MudItem xs="12" sm="6">
                <MudNumericField @bind-Value="threads" Label="Thread Count" Min="1" Max="16" />
            </MudItem>
        </MudGrid>
        
        <MudSelect T="string" Label="Storage Destination" AnchorOrigin="Origin.BottomCenter" Class="mt-3">
            <MudSelectItem Value="@("s3")">S3 Storage</MudSelectItem>
            <MudSelectItem Value="@("b2")">Backblaze B2</MudSelectItem>
            <MudSelectItem Value="@("local")">Local Storage</MudSelectItem>
        </MudSelect>
        
        <MudButton Variant="Variant.Filled" Color="Color.Primary" Class="mt-4">Start Backup</MudButton>
    </MudForm>
</MudPaper>

@code {
    private bool backupSchema = true;
    private bool backupData = true;
    private bool backupFunctionSp = true;
    private bool backupViews = true;
    private bool backupTriggers = true;
    private bool backupEvents = true;
    private int compressionLevel = 3;
    private int threads = 4;
}
EOF

# Create Restore page
cat > Pages/Restore/Index.razor << 'EOF'
@page "/restore"

<PageTitle>Restore - SCREAM</PageTitle>

<MudText Typo="Typo.h3" Class="mb-4">Database Restore</MudText>

<MudPaper Class="pa-4 mb-4">
    <MudForm>
        <MudSelect T="string" Label="Storage Source" AnchorOrigin="Origin.BottomCenter">
            <MudSelectItem Value="@("s3")">S3 Storage</MudSelectItem>
            <MudSelectItem Value="@("b2")">Backblaze B2</MudSelectItem>
            <MudSelectItem Value="@("local")">Local Storage</MudSelectItem>
        </MudSelect>
        
        <MudSelect T="string" Label="Backup Set" AnchorOrigin="Origin.BottomCenter" Class="mt-3">
            <MudSelectItem Value="@("backup1")">sample_db_2025-03-04_120000</MudSelectItem>
        </MudSelect>
        
        <MudText Typo="Typo.h6" Class="mt-4 mb-2">Components to Restore</MudText>
        <MudGrid>
            <MudItem xs="12" sm="6" md="4">
                <MudCheckBox T="bool" @bind-Checked="restoreSchema" Label="Schema" Color="Color.Primary" />
            </MudItem>
            <MudItem xs="12" sm="6" md="4">
                <MudCheckBox T="bool" @bind-Checked="restoreData" Label="Data" Color="Color.Primary" />
            </MudItem>
            <MudItem xs="12" sm="6" md="4">
                <MudCheckBox T="bool" @bind-Checked="restoreFunctionSp" Label="Functions & SPs" Color="Color.Primary" />
            </MudItem>
            <MudItem xs="12" sm="6" md="4">
                <MudCheckBox T="bool" @bind-Checked="restoreViews" Label="Views" Color="Color.Primary" />
            </MudItem>
            <MudItem xs="12" sm="6" md="4">
                <MudCheckBox T="bool" @bind-Checked="restoreTriggers" Label="Triggers" Color="Color.Primary" />
            </MudItem>
            <MudItem xs="12" sm="6" md="4">
                <MudCheckBox T="bool" @bind-Checked="restoreEvents" Label="Events" Color="Color.Primary" />
            </MudItem>
        </MudGrid>
        
        <MudSelect T="string" Label="Destination Database" AnchorOrigin="Origin.BottomCenter" Class="mt-3">
            <MudSelectItem Value="@("default")">Default Connection</MudSelectItem>
        </MudSelect>
        
        <MudTextField @bind-Value="databaseName" Label="Database Name" Class="mt-3" />
        
        <MudButton Variant="Variant.Filled" Color="Color.Primary" Class="mt-4">Start Restore</MudButton>
    </MudForm>
</MudPaper>

@code {
    private bool restoreSchema = true;
    private bool restoreData = true;
    private bool restoreFunctionSp = true;
    private bool restoreViews = true;
    private bool restoreTriggers = true;
    private bool restoreEvents = true;
    private string databaseName = "";
}
EOF

# Create Migration page
cat > Pages/Migration/Index.razor << 'EOF'
@page "/migration"

<PageTitle>Migration - SCREAM</PageTitle>

<MudText Typo="Typo.h3" Class="mb-4">Database Migration</MudText>

<MudPaper Class="pa-4 mb-4">
    <MudText Typo="Typo.h5" Class="mb-3">Source Database</MudText>
    <MudForm>
        <MudSelect T="string" Label="Source Connection" AnchorOrigin="Origin.BottomCenter">
            <MudSelectItem Value="@("default")">Default Connection</MudSelectItem>
        </MudSelect>
        
        <MudSelect T="string" Label="Source Database" AnchorOrigin="Origin.BottomCenter" Class="mt-3">
            <MudSelectItem Value="@("sample")">sample_db</MudSelectItem>
        </MudSelect>
    </MudForm>
</MudPaper>

<MudPaper Class="pa-4 mb-4">
    <MudText Typo="Typo.h5" Class="mb-3">Destination Database</MudText>
    <MudForm>
        <MudSelect T="string" Label="Destination Connection" AnchorOrigin="Origin.BottomCenter">
            <MudSelectItem Value="@("default")">Default Connection</MudSelectItem>
        </MudSelect>
        
        <MudTextField @bind-Value="destinationDatabase" Label="Destination Database" Class="mt-3" />
    </MudForm>
</MudPaper>

<MudPaper Class="pa-4 mb-4">
    <MudText Typo="Typo.h5" Class="mb-3">Migration Components</MudText>
    <MudGrid>
        <MudItem xs="12" sm="6" md="4">
            <MudCheckBox T="bool" @bind-Checked="migrateSchema" Label="Schema" Color="Color.Primary" />
        </MudItem>
        <MudItem xs="12" sm="6" md="4">
            <MudCheckBox T="bool" @bind-Checked="migrateData" Label="Data" Color="Color.Primary" />
        </MudItem>
        <MudItem xs="12" sm="6" md="4">
            <MudCheckBox T="bool" @bind-Checked="migrateFunctionSp" Label="Functions & SPs" Color="Color.Primary" />
        </MudItem>
        <MudItem xs="12" sm="6" md="4">
            <MudCheckBox T="bool" @bind-Checked="migrateViews" Label="Views" Color="Color.Primary" />
        </MudItem>
        <MudItem xs="12" sm="6" md="4">
            <MudCheckBox T="bool" @bind-Checked="migrateTriggers" Label="Triggers" Color="Color.Primary" />
        </MudItem>
        <MudItem xs="12" sm="6" md="4">
            <MudCheckBox T="bool" @bind-Checked="migrateEvents" Label="Events" Color="Color.Primary" />
        </MudItem>
    </MudGrid>
    
    <MudButton Variant="Variant.Filled" Color="Color.Primary" Class="mt-4">Start Migration</MudButton>
</MudPaper>

@code {
    private bool migrateSchema = true;
    private bool migrateData = true;
    private bool migrateFunctionSp = true;
    private bool migrateViews = true;
    private bool migrateTriggers = true;
    private bool migrateEvents = true;
    private string destinationDatabase = "";
}
EOF

# Create About page
cat > Pages/About/Index.razor << 'EOF'
@page "/about"

<PageTitle>About - SCREAM</PageTitle>

<MudText Typo="Typo.h3" Class="mb-4">About SCREAM</MudText>

<MudPaper Class="pa-4 mb-4">
    <MudText Typo="Typo.h4" Class="mb-2 d-flex align-center">
        <MudIcon Icon="@Icons.Material.Filled.Celebration" Class="mr-2" /> SCREAM
    </MudText>
    <MudText Typo="Typo.subtitle1" Class="mb-4">
        <strong>S</strong>ecure <strong>C</strong>ompress <strong>R</strong>estore <strong>E</strong>ncrypt <strong>A</strong>rchive <strong>M</strong>igrate
    </MudText>
    
    <MudText>Your friendly neighborhood database superhero for MariaDB and MySQL backups!</MudText>
    
    <MudText Typo="Typo.h5" Class="mt-4 mb-2">What's SCREAM?</MudText>
    <MudText>
        SCREAM is your all-in-one solution for protecting, compressing, and safely storing your precious database data. 
        Think of it as a Swiss Army knife for database backups that actually cares about your peace of mind!
    </MudText>
    
    <MudText Class="mt-2">
        Instead of creating monolithic backup files that are a pain to work with, SCREAM intelligently breaks down 
        your database into modular components.
    </MudText>
    
    <MudText Typo="Typo.h5" Class="mt-4 mb-2">Tech Stack</MudText>
    <MudList>
        <MudListItem Icon="@Icons.Material.Filled.Code">Backend: .NET 8+ (fast, modern, and reliable)</MudListItem>
        <MudListItem Icon="@Icons.Material.Filled.Web">Frontend: Blazor WebAssembly (smooth UI without JavaScript headaches)</MudListItem>
        <MudListItem Icon="@Icons.Material.Filled.Terminal">CLI Tools: openssl enc, xz, mysql client</MudListItem>
    </MudList>
    
    <MudDivider Class="my-4" />
    
    <MudText Align="Align.Center">Made with ❤️ by database enthusiasts who've lost data one too many times.</MudText>
    <MudText Align="Align.Center" Typo="Typo.caption">Version 1.0.0 | © 2025</MudText>
</MudPaper>
EOF

# Create Main Layout update
cat > Shared/MainLayout.razor << 'EOF'
@inherits LayoutComponentBase

<MudThemeProvider IsDarkMode="true"/>
<MudPopoverProvider/>
<MudDialogProvider/>
<MudSnackbarProvider/>

<MudLayout>
    <MudAppBar Elevation="1" Class="px-2">
        <MudIconButton Icon="@Icons.Material.Filled.Menu" Color="Color.Inherit" Edge="Edge.Start" OnClick="@((e) => DrawerToggle())" />
        <MudText Typo="Typo.h5" Class="ml-2">SCREAM</MudText>
        <MudSpacer />
        <MudIconButton Icon="@Icons.Material.Filled.Notifications" Color="Color.Inherit" Edge="Edge.End" />
        <MudIconButton Icon="@Icons.Material.Filled.Settings" Color="Color.Inherit" Edge="Edge.End" />
    </MudAppBar>
    <MudDrawer @bind-Open="@_drawerOpen" ClipMode="DrawerClipMode.Always" Elevation="2">
        <NavMenu/>
    </MudDrawer>
    <MudMainContent>
        <MudContainer>
            <div class="content pa-4">
                @Body
            </div>
        </MudContainer>
    </MudMainContent>
</MudLayout>

@code {
    bool _drawerOpen = true;

    void DrawerToggle()
    {
        _drawerOpen = !_drawerOpen;
    }
}
EOF

# Create NavMenu (updated)
cat > Shared/NavMenu.razor << 'EOF'
<MudNavMenu>
    <MudNavLink Href="/" Match="NavLinkMatch.All" Icon="@Icons.Material.Filled.Dashboard">Dashboard</MudNavLink>
    
    <MudNavLink Href="/backup" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Filled.Backup">Backup</MudNavLink>
    
    <MudNavLink Href="/restore" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Filled.Restore">Restore</MudNavLink>
    
    <MudNavLink Href="/migration" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Filled.SwapHoriz">Migration</MudNavLink>
    
    <MudNavGroup Title="Database Management" Expanded="true" Icon="@Icons.Material.Filled.Storage">
        <MudNavLink Href="/connections" Match="NavLinkMatch.Prefix">Connections</MudNavLink>
        <MudNavLink Href="/databases" Match="NavLinkMatch.Prefix">Databases</MudNavLink>
    </MudNavGroup>
    
    <MudNavGroup Title="Components" Expanded="false" Icon="@Icons.Material.Filled.Extension">
        <MudNavLink Href="/components/schema" Match="NavLinkMatch.Prefix">Schema</MudNavLink>
        <MudNavLink Href="/components/data" Match="NavLinkMatch.Prefix">Data</MudNavLink>
        <MudNavLink Href="/components/functionsp" Match="NavLinkMatch.Prefix">Functions & SP</MudNavLink>
        <MudNavLink Href="/components/views" Match="NavLinkMatch.Prefix">Views</MudNavLink>
        <MudNavLink Href="/components/triggers" Match="NavLinkMatch.Prefix">Triggers</MudNavLink>
        <MudNavLink Href="/components/events" Match="NavLinkMatch.Prefix">Events</MudNavLink>
    </MudNavGroup>
    
    <MudNavGroup Title="Storage" Expanded="false" Icon="@Icons.Material.Filled.CloudQueue">
        <MudNavLink Href="/storage/s3" Match="NavLinkMatch.Prefix">S3 Storage</MudNavLink>
        <MudNavLink Href="/storage/backblaze" Match="NavLinkMatch.Prefix">Backblaze B2</MudNavLink>
        <MudNavLink Href="/storage/local" Match="NavLinkMatch.Prefix">Local Storage</MudNavLink>
    </MudNavGroup>
    
    <MudNavGroup Title="Settings" Expanded="false" Icon="@Icons.Material.Filled.Settings">
        <MudNavLink Href="/settings/general" Match="NavLinkMatch.Prefix">General</MudNavLink>
        <MudNavLink Href="/settings/encryption" Match="NavLinkMatch.Prefix">Encryption</MudNavLink>
        <MudNavLink Href="/settings/compression" Match="NavLinkMatch.Prefix">Compression</MudNavLink>
        <MudNavLink Href="/settings/performance" Match="NavLinkMatch.Prefix">Performance</MudNavLink>
    </MudNavGroup>
    
    <MudNavLink Href="/logs" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Filled.Assignment">Logs</MudNavLink>
    
    <MudNavLink Href="/about" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Filled.Info">About</MudNavLink>
</MudNavMenu>
EOF

# Create model classes
mkdir -p Models/{Database,Backup,Settings}

# Database Connection Model
cat > Models/Database/DatabaseConnection.cs << 'EOF'
namespace SCREAM.Models.Database
{
    public class DatabaseConnection
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string HostName { get; set; } = string.Empty;
        public int Port { get; set; } = 3306;
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool UseSsl { get; set; } = false;
        public string ConnectionString 
        { 
            get 
            {
                return $"Server={HostName};Port={Port};User ID={UserName};Password={Password};{(UseSsl ? "SslMode=Required;" : "")}";
            } 
        }
    }
}
EOF

# Backup Configuration Model
cat > Models/Backup/BackupConfiguration.cs << 'EOF'
namespace SCREAM.Models.Backup
{
    public class BackupConfiguration
    {
        public string DatabaseConnectionId { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = string.Empty;
        public bool BackupSchema { get; set; } = true;
        public bool BackupData { get; set; } = true;
        public bool BackupFunctionSp { get; set; } = true;
        public bool BackupViews { get; set; } = true;
        public bool BackupTriggers { get; set; } = true;
        public bool BackupEvents { get; set; } = true;
        public int CompressionLevel { get; set; } = 3;
        public int ThreadCount { get; set; } = 4;
        public string StorageDestination { get; set; } = "s3";
        public string EncryptionKey { get; set; } = string.Empty;
        public string BackupFolder { get; set; } = string.Empty;
    }
}
EOF

# S3 Storage Settings Model
cat > Models/Settings/S3StorageSettings.cs << 'EOF'
namespace SCREAM.Models.Settings
{
    public class S3StorageSettings
    {
        public string ServiceUrl { get; set; } = string.Empty;
        public string BucketName { get; set; } = string.Empty;
        public string AccessKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
    }
}
EOF

# Create Services
cat > Services/DatabaseService.cs << 'EOF'
using SCREAM.Models.Database;

namespace SCREAM.Services
{
    public interface IDatabaseService
    {
        Task<List<string>> GetDatabasesAsync(DatabaseConnection connection);
    }

    public class DatabaseService : IDatabaseService
    {
        public async Task<List<string>> GetDatabasesAsync(DatabaseConnection connection)
        {
            // This would use MySqlConnector to actually query databases
            // For now, we return a mock list
            await Task.Delay(100); // Simulate network delay
            return new List<string> { "mysql", "information_schema", "sample_db" };
        }
    }
}
EOF

cat > Services/BackupService.cs << 'EOF'
using SCREAM.Models.Backup;

namespace SCREAM.Services
{
    public interface IBackupService
    {
        Task<string> StartBackupAsync(BackupConfiguration config);
        Task<List<string>> GetBackupComponentsAsync(string backupId);
    }

    public class BackupService : IBackupService
    {
        public async Task<string> StartBackupAsync(BackupConfiguration config)
        {
            // This would implement the actual backup logic
            await Task.Delay(1000); // Simulate work
            return Guid.NewGuid().ToString();
        }

        public async Task<List<string>> GetBackupComponentsAsync(string backupId)
        {
            // This would check what components are available in a backup
            await Task.Delay(100);
            return new List<string> { "schema", "data", "functionsp", "views", "triggers", "events" };
        }
    }
}
EOF

# Create placeholders for the remaining sections
for dir in Pages/DatabaseManagement/Connections Pages/DatabaseManagement/Databases Pages/Components/{Schema,Data,FunctionSp,Views,Triggers,Events} Pages/Storage/{S3,BackblazeB2,Local} Pages/Settings/{General,Encryption,Compression,Performance} Pages/Logs; do
    # Extract the section name from path
    section=$(basename "$dir")
    
    # Create Index.razor file with basic content
    cat > "$dir/Index.razor" << EOF
@page "/${dir#Pages/}"

<PageTitle>${section} - SCREAM</PageTitle>

<MudText Typo="Typo.h3" Class="mb-4">${section}</MudText>

<MudPaper Class="pa-4">
    <MudText>This is the ${section} page.</MudText>
</MudPaper>
EOF
done

# Make the script executable
chmod +x scaffold.sh

echo "SCREAM project structure has been successfully scaffolded!"
echo "The script created all necessary folders and files for the Blazor WebAssembly project."
