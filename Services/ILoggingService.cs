using System;
using Serilog;

namespace RequiemGlamPatcher.Services;

public interface ILoggingService : IDisposable
{
    ILogger Logger { get; }
    ILogger ForContext<T>();
    string LogDirectory { get; }
    string LogFilePattern { get; }
    void Flush();
}
