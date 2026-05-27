namespace TerrorTweaks.Framework;

public abstract class Tweak
{
    public abstract string Name { get; }
    public abstract string Description { get; }

    public string InternalName => GetType().Name;

    public bool Enabled { get; private set; }

    public virtual void Enable() => Enabled = true;
    public virtual void Disable() => Enabled = false;

    public virtual void DrawConfig() { }
}
