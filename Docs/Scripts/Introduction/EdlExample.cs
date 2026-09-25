using Solas.Components;
using Solas.ComponentUtils;
using Solas.Interfaces;

namespace Solas.Docs.Introduction;

#region Example

public class EnemyData : IData
{
    public Entity Entity { get; set; }
    public DataProperty<int> Health = new(10);
    public float Speed = 10f;
}

public class EnemyAI : ILogic, IInitializable
{
    public Entity Entity { get; set; }

    public void Initialize()
    {
        var data = Entity.AddData(new EnemyData());
        data.Health.Value = 150;
    }
}

#endregion