using CommandLine;
using MqttDeviceSimulator;
using Serilog;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{DeviceId}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    await Parser.Default.ParseArguments<SimulatorOptions>(args)
        .WithParsedAsync(async options =>
        {
            var simulator = new DeviceSimulator(options);
            await simulator.RunAsync();
        });
}
catch (Exception ex)
{
    Log.Fatal(ex, "Simulator crashed");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;
