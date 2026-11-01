namespace SE.Roleplay.Framework.Directory.Models;

public sealed record ServerAdmissionDecision(
    string ServerId,
    bool AllowlistEnabled,
    bool IsAllowed,
    bool ShouldAdmit);

public sealed class UpdateServerAdmissionSettingsRequest
{
    public bool AllowlistEnabled { get; set; }
}
