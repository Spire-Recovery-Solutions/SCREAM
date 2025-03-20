using Dapper;
using MySqlConnector;
using SCREAM.Data.Entities;
using SCREAM.Data.Entities.Backup.BackupItems;
using SCREAM.Data.Entities.Database;

namespace SCREAM.Business
{
    /// <summary>
    /// Generator for database objects metadata to be used with mysqldump via CliWrap.
    /// </summary>
    public class BackupItemGenerator
    {
        /// <summary>
        /// Gets all database objects that can be backed up using mysqldump via CliWrap.
        /// </summary>
        public async Task<List<BackupItem>> GetBackupItems(DatabaseConnection dbConnection)
        {
            await using var connection = new MySqlConnection(dbConnection.ConnectionString);

            var backupItems = new List<BackupItem>();

            // Query to get tables and views.
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

            // Query to get triggers.
            string triggersQuery = @"
                SELECT 
                    TRIGGER_SCHEMA as `Schema`,
                    TRIGGER_NAME as `Name`,
                    'TRIGGER' as `TypeValue`
                FROM 
                    information_schema.TRIGGERS
                WHERE 
                    TRIGGER_SCHEMA NOT IN ('mysql', 'performance_schema', 'information_schema', 'sys')";

            // Query to get events.
            string eventsQuery = @"
                SELECT 
                    EVENT_SCHEMA as `Schema`,
                    EVENT_NAME as `Name`,
                    'EVENT' as `TypeValue`
                FROM 
                    information_schema.EVENTS
                WHERE 
                    EVENT_SCHEMA NOT IN ('mysql', 'performance_schema', 'information_schema', 'sys')";

            // Query to get routines (functions and procedures).
            string routinesQuery = @"
                SELECT 
                    ROUTINE_SCHEMA as `Schema`,
                    ROUTINE_NAME as `Name`,
                    ROUTINE_TYPE as `TypeValue`
                FROM 
                    information_schema.ROUTINES
                WHERE 
                    ROUTINE_SCHEMA NOT IN ('mysql', 'performance_schema', 'information_schema', 'sys')";

            // Execute all queries to get metadata.
            var tables = connection.Query<BackupItemMetadata>(tablesQuery).ToList();
            var triggers = connection.Query<BackupItemMetadata>(triggersQuery).ToList();
            var events = connection.Query<BackupItemMetadata>(eventsQuery).ToList();
            var routines = connection.Query<BackupItemMetadata>(routinesQuery).ToList();

            // Process tables (base tables and views).
            foreach (var item in tables.Where(t => t.TypeValue == "BASE TABLE"))
            {
                // Table structure
                backupItems.Add(new BackupItem
                {
                    DatabaseItem = new DatabaseTableStructureItems
                    {
                        Schema = item.Schema,
                        Name = item.Name,
                        Engine = item.Engine
                    }
                });

                // Table data
                backupItems.Add(new BackupItem
                {
                    DatabaseItem = new DatabaseTableDataItems
                    {
                        Schema = item.Schema,
                        Name = item.Name,
                        RowCount = item.TableRows
                    }
                });
            }

            // Process views.
            foreach (var item in tables.Where(t => t.TypeValue == "VIEW"))
        {
            backupItems.Add(new BackupItem {
                DatabaseItem = new DatabaseViewItems {
                    Schema = item.Schema,
                    Name = item.Name
                }
            });
        }

            // Process triggers.
            foreach (var item in triggers)
        {
            backupItems.Add(new BackupItem {
                DatabaseItem = new DatabaseTriggerItems {
                    Schema = item.Schema,
                    Name = item.Name
                }
            });
        }
            // Process events.
            foreach (var item in events)
        {
                backupItems.Add(new BackupItem {
                DatabaseItem = new DatabaseEventItems {
                    Schema = item.Schema,
                    Name = item.Name
                }
            });
        }

        // Process routines
        foreach (var item in routines)
        {
            backupItems.Add(new BackupItem {
                DatabaseItem = new DatabaseFunctionProcedureItems {
                    Schema = item.Schema,
                    Name = item.Name
                }
            });
        }


            return backupItems;
        }
    }

    /// <summary>
    /// Metadata for database objects retrieved from information_schema.
    /// </summary>
    public class BackupItemMetadata
    {
        public string Schema { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TypeValue { get; set; } = string.Empty;
        public string Engine { get; set; } = string.Empty;
        public long TableRows { get; set; }
    }
}
