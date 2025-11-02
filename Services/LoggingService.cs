using System;
using System.IO;
using Serilog;
using Serilog.Core;

namespace RequiemGlamPatcher.Services;

public sealed class LoggingService : ILoggingService
{
    private readonly Logger _logger;
    private bool _disposed;

    public LoggingService()
    {
        LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RequiemGlamPatcher",
            "Logs");

        Directory.CreateDirectory(LogDirectory);

        LogFilePattern = Path.Combine(LogDirectory, "RequiemGlamPatcher-.log");

        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Async(configuration =>
                configuration.File(
                    LogFilePattern,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    shared: true))
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
            return;

        Log.CloseAndFlush();
        _disposed = true;
    }

    public void Dispose()
    {
        Flush();
    }
}
