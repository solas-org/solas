using Solas.Components;
using Solas.Containers;
using Solas.Enums;
using Solas.Interfaces;
using Solas.World;

namespace Solas.Systems;

internal class InitializeSystem(Space space)
{
    internal InitializationPool Pool;

    internal IEnumerable<Task> InitializeDependencies()
    {
        var entitiesList = EngineContext.EntityPool.GetEntitiesIn(space);
        var entities = entitiesList is List<Entity> l ? l.ToArray() : entitiesList.ToArray();

        var guidsCount = Pool.OrderedEntitiesIds.Length;
        var orderedEntities = new Entity[guidsCount];

        if (Pool.OrderType == InitializationOrder.Custom)
        {
            var entitiesCopy = (Entity[])entities.Clone();
            for (var i = 0; i < guidsCount; i++)
            {
                var targetGuid = Pool.OrderedEntitiesIds[i];
                for (int j = 0; j < entitiesCopy.Length; j++)
                {
                    if (entitiesCopy[j].Id == targetGuid)
                    {
                        entities[i] = entitiesCopy[j];
                        break;
                    }
                }
            }
        }
        else if (Pool.OrderType != InitializationOrder.Random)
        {
            var result = new Entity[entities.Length];
            var count = 0;

            for (var i = 0; i < entities.Length; i++)
            {
                for (var j = 0; j < guidsCount; j++)
                {
                    if (entities[i].Id == Pool.OrderedEntitiesIds[j])
                    {
                        orderedEntities[j] = entities[i];
                        entities[i] = Entity.Null;
                        break;
                    }
                }

                if (entities[i] != Entity.Null && Pool.OrderType == InitializationOrder.Suffixal)
                {
                    result[count++] = entities[i];
                }
            }

            for (var j = 0; j < guidsCount; j++)
            {
                result[count++] = orderedEntities[j];
            }

            if (Pool.OrderType == InitializationOrder.Prefixal)
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (entities[i] != Entity.Null)
                    {
                        result[count++] = entities[i];
                    }
                }
            }

            entities = result;
        }

        var allTasks = new List<Task>();
        for (int i = 0; i < entities.Length; i++)
        {
            if (entities[i] == Entity.Null) continue;
            var logics = entities[i].Logics;
            for (int j = 0; j < logics.Length; j++)
            {
                allTasks.Add(InitializeLogic(logics[j]));
            }
        }

        return allTasks;
    }

    private async Task InitializeLogic(ILogic logic)
    {
        if (logic is IInitializable init)
        {
            await Task.Run(init.Initialize);
        }
    }
}