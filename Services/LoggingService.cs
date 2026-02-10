using System.IO;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Boutique.Services;

public sealed class LoggingService : ILoggingService
{
  private readonly LoggingLevelSwitch _levelSwitch;
  private readonly Logger             _logger;
  private          bool               _disposed;

  public LoggingService()
  {
    LogDirectory = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "Boutique",
      "Logs");

    Directory.CreateDirectory(LogDirectory);

    LogFilePattern = Path.Combine(LogDirectory, "Boutique-.log");

    _levelSwitch = new LoggingLevelSwitch();

    _logger = new LoggerConfiguration()
              .MinimumLevel.ControlledBy(_levelSwitch)
              .Enrich.FromLogContext()
              .WriteTo.Async(configuration =>
#pragma warning disable CA1305 // File sink configuration doesn't involve locale-sensitive formatting
                               configuration.File(
                                 LogFilePattern,
                                 shared: true,
                                 rollingInterval: RollingInterval.Day,
                                 retainedFileCountLimit: 14))
#pragma warning restore CA1305
              .CreateLogger();

    Log.Logger = _logger;
  }

  public ILogger Logger => _logger;
  public string LogDirectory { get; }
  public string LogFilePattern { get; }

  public bool IsDebugEnabled
  {
    get => _levelSwitch.MinimumLevel == LogEventLevel.Debug;
    set => _levelSwitch.MinimumLevel = value ? LogEventLevel.Debug : LogEventLevel.Information;
  }

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
