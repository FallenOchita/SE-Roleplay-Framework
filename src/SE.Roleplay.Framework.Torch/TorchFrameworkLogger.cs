using NLog;
using SE.Roleplay.Framework.Core.Abstractions;

namespace SE.Roleplay.Framework.Torch
{
    /// <summary>Writes framework messages through Torch's configured NLog pipeline.</summary>
    internal sealed class TorchFrameworkLogger : IFrameworkLogger
    {
        private static readonly Logger Log = LogManager.GetLogger("SE.Roleplay.Framework");

        public void Info(string message) => Log.Info(message);
        public void Warn(string message) => Log.Warn(message);
        public void Error(string message, System.Exception? exception = null)
        {
            if (exception == null)
                Log.Error(message);
            else
                Log.Error(exception, message);
        }
    }
}
