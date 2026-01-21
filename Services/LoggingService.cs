using System.IO;
using Serilog;
using Serilog.Core;

namespace Boutique.Services;

public sealed class LoggingService : ILoggingService
{
    private readonly Logger _logger;
    private bool _disposed;

    public LoggingService()
    {
        LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Boutique",
            "Logs");

        Directory.CreateDirectory(LogDirectory);

        LogFilePattern = Path.Combine(LogDirectory, "Boutique-.log");

        _logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Async(configuration =>
#pragma warning disable CA1305 // File sink configuration doesn't involve locale-sensitive formatting
                configuration.File(
                    LogFilePattern,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    shared: true))
#pragma warning restore CA1305
            .CreateLogger();

        Log.Logger = _logger;
    }

    public ILogger Logger => _logger;
    public string LogDirectory { get; }
    public string LogFilePattern { get; }

    public ILogger ForContext<T>() => _logger.ForContext<T>();

    public void Flush()
    {
        if (_disposed)
        {
            return;
        }

        Log.CloseAndFlush();
        _disposed = true;
    }

    public void Dispose() => Flush();
}
