namespace SCREAM.Web.Models.Database
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
