using System;
using System.Data;
using System.Linq;
using System.Collections.Generic;
using MySqlConnector;
using Dapper;
using SCREAM.Data.Entities;
using CliWrap;

namespace SCREAM.Business;

/// <summary>
/// Generator for database objects metadata to be used with mysqldump via CliWrap
/// </summary>
public class BackupItemGenerator
{
    /// <summary>
    /// Gets all database objects that can be backed up using mysqldump via CliWrap
    /// </summary>
    public List<BackupItem> GetBackupItems(MySqlConnection connection)
    {
        var backupItems = new List<BackupItem>();
        
        // Query to get tables and views
        string tablesQuery = @"
        SELECT 
            TABLE_SCHEMA as `Schema`,
            TABLE_NAME as `Name`,
            TABLE_TYPE as `TypeValue`,
            ENGINE as `Engine`,
            COALESCE(TABLE_ROWS, 0) as `TableRows`
        FROM 
            information_schema.TABLES
        WHERE 
            TABLE_SCHEMA NOT IN ('mysql', 'performance_schema', 'information_schema', 'sys')";
        
        // Query to get triggers
        string triggersQuery = @"
        SELECT 
            TRIGGER_SCHEMA as `Schema`,
            TRIGGER_NAME as `Name`,
            'TRIGGER' as `TypeValue`
        FROM 
            information_schema.TRIGGERS
        WHERE 
            TRIGGER_SCHEMA NOT IN ('mysql', 'performance_schema', 'information_schema', 'sys')";
        
        // Query to get events
        string eventsQuery = @"
        SELECT 
            EVENT_SCHEMA as `Schema`,
            EVENT_NAME as `Name`,
            'EVENT' as `TypeValue`
        FROM 
            information_schema.EVENTS
        WHERE 
            EVENT_SCHEMA NOT IN ('mysql', 'performance_schema', 'information_schema', 'sys')";
        
        // Query to get routines (functions and procedures)
        string routinesQuery = @"
        SELECT 
            ROUTINE_SCHEMA as `Schema`,
            ROUTINE_NAME as `Name`,
            ROUTINE_TYPE as `TypeValue`
        FROM 
            information_schema.ROUTINES
        WHERE 
            ROUTINE_SCHEMA NOT IN ('mysql', 'performance_schema', 'information_schema', 'sys')";
        
        // Execute all queries to get metadata
        var tables = connection.Query<BackupItemMetadata>(tablesQuery).ToList();
        var triggers = connection.Query<BackupItemMetadata>(triggersQuery).ToList();
        var events = connection.Query<BackupItemMetadata>(eventsQuery).ToList();
        var routines = connection.Query<BackupItemMetadata>(routinesQuery).ToList();
        
        // Process tables
        foreach (var item in tables.Where(t => t.TypeValue == "BASE TABLE"))
        {
            // Add table structure item
            backupItems.Add(new TableStructureItem { 
                Schema = item.Schema, 
                Name = item.Name,
                Engine = item.Engine
            });
            
            // Add table data item
            backupItems.Add(new TableDataItem { 
                Schema = item.Schema, 
                Name = item.Name,
                RowCount = item.TableRows
            });
        }
        
        // Process views
        foreach (var item in tables.Where(t => t.TypeValue == "VIEW"))
        {
            backupItems.Add(new ViewItem { 
                Schema = item.Schema, 
                Name = item.Name
            });
        }
        
        // Process triggers - group by schema
        var triggerSchemas = triggers.Select(t => t.Schema).Distinct();
        foreach (var schema in triggerSchemas)
        {
            backupItems.Add(new TriggerItem { 
                Schema = schema
            });
        }
        
        // Process events - group by schema
        var eventSchemas = events.Select(e => e.Schema).Distinct();
        foreach (var schema in eventSchemas)
        {
            backupItems.Add(new EventItem { 
                Schema = schema
            });
        }
        
        // Process routines (functions and procedures) - group by schema
        var routineSchemas = routines.Select(r => r.Schema).Distinct();
        foreach (var schema in routineSchemas)
        {
            backupItems.Add(new FunctionProcedureItem { 
                Schema = schema
            });
        }
        
        return backupItems;
    }
}

/// <summary>
/// Metadata for database objects retrieved from information_schema
/// </summary>
public class BackupItemMetadata
{
    public string Schema { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TypeValue { get; set; } = string.Empty;
    public string Engine { get; set; } = string.Empty;
    public long TableRows { get; set; }
}