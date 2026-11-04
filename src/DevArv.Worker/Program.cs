using DevArv.Worker.Helpers;
using DevArv.Worker.Models;
using DevArv.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);
// Toda la configuración sale de los appsettings (ver AppSettingsConfiguration).
builder.Configuration.UseAppSettingsOnly(builder.Environment.EnvironmentName);

// Se puede instalar como servicio de Windows o de systemd.
builder.Services.AddWindowsService(o => o.ServiceName = "DevArv Worker");
builder.Services.AddSystemd();

builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection("Worker"));
builder.Services.AddHttpClient<ServerClient>();
builder.Services.AddSingleton<GitWorkspace>();
builder.Services.AddSingleton<ClaudeRunner>();
builder.Services.AddSingleton<JobRunner>();
builder.Services.AddHostedService<AgentWorkerService>();

var host = builder.Build();
host.Run();
