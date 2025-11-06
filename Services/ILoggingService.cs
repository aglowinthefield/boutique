using Serilog;

namespace Boutique.Services;

public interface ILoggingService : IDisposable
{
    ILogger Logger { get; }
    string LogDirectory { get; }
    string LogFilePattern { get; }
    ILogger ForContext<T>();
    void Flush();
}