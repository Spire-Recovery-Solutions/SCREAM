using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using SCREAM.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddHttpClient("SCREAM", client =>
{
    client.BaseAddress = new Uri("http://localhost:8000");//change this to 5102 when running locally
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});
builder.Services.AddMudServices();

await builder.Build().RunAsync();
