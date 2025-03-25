using Microsoft.EntityFrameworkCore;
using SCREAM.Data;

namespace SCREAM.Service.Backup;

public class Program
{

    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
         builder.Services.AddHttpClient("SCREAM", client =>
            {
                client.BaseAddress = new Uri("http://localhost:5102");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });

        builder.Services.AddHostedService<Worker>();
        var host = builder.Build();
        host.Run();
    }
}