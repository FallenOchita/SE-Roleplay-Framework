namespace SE.Roleplay.Framework.Core
{
    /// <summary>Host-neutral settings. A server adapter is responsible for loading these values.</summary>
    public sealed class FrameworkOptions
    {
        public string ServerName { get; set; } = "Space Engineers Roleplay";
        public string DataDirectory { get; set; } = "SE-Roleplay-Framework";
        public bool EnableDiagnostics { get; set; } = true;
        public int AutosaveIntervalSeconds { get; set; } = 300;
        public int MaximumCharactersPerPlayer { get; set; } = 5;
    }
}
