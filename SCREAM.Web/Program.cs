using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using SCREAM.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddHttpClient("SCREAM", client =>
{
    client.BaseAddress = new Uri("https://localhost:7202");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});
builder.Services.AddMudServices();

await builder.Build().RunAsync();
