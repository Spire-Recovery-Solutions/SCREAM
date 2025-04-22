namespace SCREAM.Service.Backup;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        var apiUrl = Environment.GetEnvironmentVariable("API_URL") ?? "http://localhost:5102";

        builder.Services.AddHttpClient("SCREAM", client =>
        {
            client.BaseAddress = new Uri(apiUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        builder.Services.AddHostedService<Worker>();
        var host = builder.Build();
        host.Run();
    }
}