using System.Numerics;

namespace Solas.Transform;

public class TransformEventData()
{
    public Action<TransformData> CreateDataEvent = delegate { };
    public Action<TransformData> DisposeDataEvent = delegate { };
    public Action<TransformData, Vector3> PositionUpdateEvent = delegate { };
    public Action<TransformData, Vector3> RotationUpdateEvent = delegate { };
    public Action<TransformData, Vector3> ScaleUpdateEvent = delegate { };
}