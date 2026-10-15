using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using RaceReplay.Cli;

// The host provides DI, configuration and logging; it isn't run, because each command is one unit of work.
HostApplicationBuilder builder = Host.CreateApplicationBuilder();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddRaceReplayCli();
using IHost host = builder.Build();

return await CliCommands.RunAsync(host.Services, args);