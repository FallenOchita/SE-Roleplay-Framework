using System.Diagnostics;
using SE.Roleplay.Framework.Core.Abstractions;

namespace SE.Roleplay.Framework.Torch
{
    /// <summary>Small host logger that does not add a dependency on Torch's logging implementation.</summary>
    internal sealed class TorchFrameworkLogger : IFrameworkLogger
    {
        private const string Prefix = "[SE Roleplay Framework] ";

        public void Info(string message) => Trace.TraceInformation(Prefix + message);
        public void Warn(string message) => Trace.TraceWarning(Prefix + message);

        public void Error(string message, System.Exception? exception = null)
        {
            Trace.TraceError(Prefix + message);
            if (exception != null)
                Trace.TraceError(exception.ToString());
        }
    }
}
