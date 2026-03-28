using Serilog;

namespace Boutique.Services;

/// <summary>
/// Abstraction over Serilog providing context-specific loggers, log directory access,
/// debug level toggling, and flush support.
/// </summary>
public interface ILoggingService : IDisposable
{
  ILogger Logger { get; }
  string LogDirectory { get; }
  string LogFilePattern { get; }
  bool IsDebugEnabled { get; set; }
  ILogger ForContext<T>();
  void Flush();
}
