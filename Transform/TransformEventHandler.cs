using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Solas.World;

namespace Solas.Transform;

public static class TransformEventHandler
{
    private static readonly Dictionary<Space, TransformEventData> _handlers = [];

    public static void RegisterData(TransformData data)
    {
        if (data.Entity.IsNull) return;
        if (!_handlers.TryGetValue(data.Entity.CurrentSpace, out var handler))
            _handlers.Add(data.Entity.CurrentSpace, handler = new TransformEventData());
        data.Position.OnChange += value => handler.PositionUpdateEvent(data, value);
        data.Rotation.OnChange += value => handler.RotationUpdateEvent(data, value);
        data.Scale.OnChange += value => handler.ScaleUpdateEvent(data, value);
        handler.CreateDataEvent.Invoke(data);
    }

    public static void UnregisterData(TransformData data)
    {
        if (data.Entity.IsNull) return;
        var handler = _handlers[data.Entity.CurrentSpace];
        data.Position.OnChange -= value => handler.PositionUpdateEvent(data, value);
        data.Rotation.OnChange -= value => handler.RotationUpdateEvent(data, value);
        data.Scale.OnChange -= value => handler.ScaleUpdateEvent(data, value);
        handler.DisposeDataEvent.Invoke(data);
    }

    public static TransformEventData GetHandler(Space space) => _handlers[space];
}