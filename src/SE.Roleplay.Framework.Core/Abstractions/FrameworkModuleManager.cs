using System;
using System.Collections.Generic;
using System.Linq;

namespace SE.Roleplay.Framework.Core.Abstractions
{
    /// <summary>Owns module initialization and start/stop ordering for a framework host.</summary>
    public sealed class FrameworkModuleManager
    {
        private readonly IFrameworkContext _context;
        private readonly List<IFrameworkModule> _modules = new List<IFrameworkModule>();
        private readonly List<IFrameworkModule> _initialized = new List<IFrameworkModule>();
        private readonly List<IFrameworkModule> _started = new List<IFrameworkModule>();
        private bool _initializedOnce;
        private bool _startedOnce;
        private bool _stopped;

        public FrameworkModuleManager(IFrameworkContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public IReadOnlyCollection<IFrameworkModule> Modules => _modules.AsReadOnly();

        public void Register(IFrameworkModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (_initializedOnce || _stopped) throw new InvalidOperationException("Modules cannot be registered after initialization or shutdown.");
            if (string.IsNullOrWhiteSpace(module.Name)) throw new ArgumentException("Module name is required.", nameof(module));
            if (_modules.Any(existing => string.Equals(existing.Name, module.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A module named '" + module.Name + "' is already registered.");
            _modules.Add(module);
        }

        public void Initialize()
        {
            if (_initializedOnce || _stopped) throw new InvalidOperationException("Modules have already been initialized or shut down.");
            _initializedOnce = true;
            try
            {
                foreach (var module in _modules)
                {
                    // Track before calling Initialize so a partially initialized module also receives Stop.
                    _initialized.Add(module);
                    module.Initialize(_context);
                    _context.Logger.Info("Initialized module: " + module.Name);
                }
            }
            catch
            {
                StopInitializedModules(new HashSet<IFrameworkModule>());
                _stopped = true;
                throw;
            }
        }

        public void Start()
        {
            if (!_initializedOnce || _stopped) throw new InvalidOperationException("Initialize modules before starting them and do not restart after shutdown.");
            if (_startedOnce) throw new InvalidOperationException("Modules have already been started.");
            _startedOnce = true;
            try
            {
                foreach (var module in _initialized)
                {
                    module.Start();
                    _started.Add(module);
                    _context.Logger.Info("Started module: " + module.Name);
                }
            }
            catch
            {
                var stopped = StopStartedModules();
                StopInitializedModules(stopped);
                _stopped = true;
                throw;
            }
        }

        public void Stop()
        {
            if (_stopped) return;
            var stopped = StopStartedModules();
            StopInitializedModules(stopped);
            _startedOnce = false;
            _stopped = true;
        }

        private HashSet<IFrameworkModule> StopStartedModules()
        {
            var stopped = new HashSet<IFrameworkModule>();
            for (var i = _started.Count - 1; i >= 0; i--)
            {
                var module = _started[i];
                try { module.Stop(); }
                catch (Exception ex) { _context.Logger.Error("Failed to stop module: " + module.Name, ex); }
                stopped.Add(module);
            }
            _started.Clear();
            return stopped;
        }

        private void StopInitializedModules(ISet<IFrameworkModule> alreadyStopped)
        {
            // Modules that initialized but never started may still own resources.
            for (var i = _initialized.Count - 1; i >= 0; i--)
            {
                var module = _initialized[i];
                if (alreadyStopped.Contains(module)) continue;
                try { module.Stop(); }
                catch (Exception ex) { _context.Logger.Error("Failed to clean up module: " + module.Name, ex); }
            }
            _initialized.Clear();
        }
    }
}
