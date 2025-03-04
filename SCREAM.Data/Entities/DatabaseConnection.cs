using SCREAM.Web.Enums;

namespace SCREAM.Data.Entities
{
   public class DatabaseConnection : ScreamDbBaseEntity
    {
        public long Id { get; set; }
        public required string HostName { get; set; } = string.Empty;
        public required int Port { get; set; } = 3306;
        public required string UserName { get; set; } = string.Empty;
        public required string Password { get; set; } = string.Empty;
        public required DatabaseType Type { get; set; }

        public string ConnectionString 
        { 
            get 
            {
                return $"Server={HostName};Port={Port};User ID={UserName};Password={Password};";
            } 
        }
    }
}
