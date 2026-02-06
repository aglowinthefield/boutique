using Serilog;

namespace Boutique.Services;

public interface ILoggingService : IDisposable
{
    ILogger Logger { get; }
    string LogDirectory { get; }
    string LogFilePattern { get; }
    bool IsDebugEnabled { get; set; }
    ILogger ForContext<T>();
    void Flush();
}
