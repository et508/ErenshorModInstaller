using System;

namespace ErenshorModInstaller.Wpf.Services.Abstractions
{
    public interface IStatusSink
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
        void Clear();
    }
}