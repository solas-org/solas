using System.Numerics;
using Solas.Attributes;
using Solas.Components;
using Solas.Interfaces;
using Solas.Render.Components;
using Solas.Transform;
using Solas.Transform.MathExtensions;

namespace Solas.Render.Logics;

public class MeshRenderLogic : ILogic, IInitializable
{
    public Entity Entity { get; set; }
    
    [Inject]
    public Mesh? Mesh
    {
        get;
        set
        {
            field = value;
            RenderLogicEventHandler.OnMeshUpdate(this, field);
        }
    }

    [Inject]
    public Material? Material
    {
        get;
        set
        {
            field = value;
            RenderLogicEventHandler.OnMaterialUpdate(this, field);
        }
    }

    private TransformData? _transformData;

    public void Initialize()
    {
        _transformData = Entity.GetData<TransformData>() ?? Entity.AddData(new TransformData());
        RenderLogicEventHandler.Register(this);
    }

    public Matrix4x4 GetModelMatrix()
    {
        var translationMat = Matrix4x4.CreateTranslation(_transformData!.Position.Value);
        var rotationMat = Matrix4x4.CreateFromQuaternion(_transformData.Rotation.Value.ToQuaternion());
        var scaleMat = Matrix4x4.CreateScale(_transformData.Scale.Value);

        return scaleMat * rotationMat * translationMat;
    }

    public void Dispose()
    {
        RenderLogicEventHandler.Unregister(this);
    }
}