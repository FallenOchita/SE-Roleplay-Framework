namespace SE.Roleplay.Framework.Core.Abstractions
{
    /// <summary>A lifecycle unit hosted by the framework.</summary>
    public interface IFrameworkModule
    {
        string Name { get; }
        void Initialize(IFrameworkContext context);
        void Start();
        void Stop();
    }
}
