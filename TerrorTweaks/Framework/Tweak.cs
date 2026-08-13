namespace TerrorTweaks.Framework;

public abstract class Tweak
{
    protected Tweak()
    {
        HasConfig = GetType().GetMethod(nameof(DrawConfig))!.DeclaringType != typeof(Tweak);
    }

    public abstract string Name { get; }
    public abstract string Description { get; }

    public string InternalName => GetType().Name;

    public bool HasConfig { get; }

    public bool Enabled { get; private set; }

    public virtual void Enable() => Enabled = true;
    public virtual void Disable() => Enabled = false;

    public virtual void DrawConfig() { }
}
