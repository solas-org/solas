namespace Solas.ComponentUtils;

public class DataProperty<T>(T @default = default) : ReactiveProperty<T>(@default)
{
    private readonly List<DataModifier<T>> _modifiers = [];

    public TModifier AddModifier<TModifier>() where TModifier : DataModifier<T>, new()
    {
        var newModifier = _modifiers.FirstOrDefault(m => m is TModifier) as TModifier;
        if (newModifier == null)
        {
            newModifier = new TModifier { Property = this };
            _modifiers.Add(newModifier);
        }

        return newModifier;
    }

    public void RemoveModifier<TModifier>() where TModifier : DataModifier<T>
    {
        _modifiers.RemoveAll(m => m is TModifier);
    }

    public TModifier GetModifier<TModifier>() where TModifier : DataModifier<T>
    {
        return _modifiers.Find(m => m is TModifier) as TModifier;
    }
}