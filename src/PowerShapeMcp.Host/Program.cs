using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PowerShapeMcp;
using HostOptions = PowerShapeMcp.HostOptions;

var options = HostOptions.Parse(args);
using var session = new BridgeSession(options);
var builder = Host.CreateApplicationBuilder();
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(session);
builder.Services.AddMcpServer(server => server.ServerInfo = new()
    { Name = "powershape-bridge", Version = HostOptions.Version, Title = "PowerShape Bridge MCP" })
    .WithStdioServerTransport()
    .WithTools(ToolCatalog.Create(session));
await builder.Build().RunAsync();
