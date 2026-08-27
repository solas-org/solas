using Solas.Interfaces;

namespace Solas.Components;

public interface ILogic : IInjectable, IDisposable
{
    public Entity Entity { get; set; }

    void IDisposable.Dispose()
    {
    }

    void IInjectable.WriteInject(FileStream stream, Entity entity)
    {
    }

    (Guid, Guid)[] IInjectable.ReadInject(FileStream stream)
    {
        return null;
    }

    void IInjectable.Inject((Guid, Guid)[] guids)
    {
    }
}