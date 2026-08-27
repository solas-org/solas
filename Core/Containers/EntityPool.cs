using System.Runtime.CompilerServices;
using Solas.Components;
using Solas.ComponentUtils;
using Solas.Interfaces;
using Solas.World;

namespace Solas.Containers;

internal class EntityPool
{
    private int _capacity = 1024;
    private uint _globalInternalIdCounter = 1;

    private ushort[] _versions = new ushort[1024];
    private Guid[] _guids = new Guid[1024];
    private Space[] _spaces = new Space[1024];
    private EntityMetaData[] _metaData = new EntityMetaData[1024];
    private bool[] _isEnableds = new bool[1024];
    private ReactiveProperty<bool>[] _reactiveProperties = new ReactiveProperty<bool>[1024];
    private uint[][] _bitmasks = new uint[1024][];

    private readonly Dictionary<Space, List<Entity>> _entitiesInSpaces = new();
    private readonly Stack<uint> _freeInternalIds = new();

    internal List<IUpdateRunner> UpdateRunners { get; } = [];
    internal List<IUpdateRunner> FixedUpdateRunners { get; } = [];
    internal List<IUpdateRunner> LateUpdateRunners { get; } = [];

    #region Space Registration

    internal void RegisterSpace(Space space)
    {
        if (space == null)
            throw new ArgumentNullException(nameof(space), "Cannot register a null Space in EntityPool.");

        _entitiesInSpaces.TryAdd(space, []);
    }

    internal void UnregisterSpace(Space space)
    {
        if (space == null)
            throw new ArgumentNullException(nameof(space), "Cannot unregister a null Space.");

        if (_entitiesInSpaces.TryGetValue(space, out var entities))
        {
            var array = entities.ToArray();
            for (int i = 0; i < array.Length; i++)
            {
                UnregisterEntity(array[i]);
            }

            _entitiesInSpaces.Remove(space);
        }
    }

    #endregion

    #region Entity Registration & LifeCycle

    internal (uint InternalId, ushort Version) RegisterEntity(Guid guid, Space space, EntityMetaData metaData)
    {
        space ??= WorldContext.GlobalSpace;
        RegisterSpace(space);

        uint id;
        ushort version;

        if (_freeInternalIds.Count > 0)
        {
            id = _freeInternalIds.Pop();
            version = _versions[id];
        }
        else
        {
            id = _globalInternalIdCounter++;
            EnsureCapacity(id);
            version = 1;
            _versions[id] = version;
        }

        guid = guid == Guid.Empty ? Guid.NewGuid() : guid;
        metaData = metaData.Equals(default) ? EntityMetaData.CreateDefault() : metaData;

        _guids[id] = guid;
        _spaces[id] = space;
        _metaData[id] = metaData;
        _isEnableds[id] = true;
        _reactiveProperties[id] = null;

        var totalChunks = ComponentRegistry.Count / 32 + 1;
        _bitmasks[id] = new uint[totalChunks];

        return (id, version);
    }

    internal void LinkEntityToSpace(Entity entity)
    {
        EnsureEntityAlive(entity);

        if (_entitiesInSpaces.TryGetValue(entity.CurrentSpace, out var list))
        {
            list.Add(entity);
        }
        else
        {
            throw new InvalidOperationException($"Space '{entity.CurrentSpace}' is not registered for Entity (ID: {entity.InternalId}).");
        }
    }

    internal void UnregisterEntity(Entity entity)
    {
        uint id = entity.InternalId;
        if (!IsAlive(entity)) return;

        if (_componentPoolsInSpaces.TryGetValue(entity.CurrentSpace, out var pools))
        {
            foreach (var pool in pools.Values)
            {
                pool.Remove(entity);
            }
        }

        _bitmasks[id] = [];

        if (_spaces[id] != null && _entitiesInSpaces.TryGetValue(_spaces[id], out var list))
        {
            FastRemove(list, entity);
        }

        _spaces[id] = null;
        _versions[id]++;
        _freeInternalIds.Push(id);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool IsAlive(Entity entity)
    {
        uint id = entity.InternalId;
        return id < _globalInternalIdCounter && _spaces[id] != null && _versions[id] == entity.Version;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureEntityAlive(Entity entity)
    {
        if (!IsAlive(entity))
        {
            throw new InvalidOperationException($"Entity (ID: {entity.InternalId}, Version: {entity.Version}) is dead or does not exist.");
        }
    }

    #endregion

    #region Properties Getters/Setters

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Guid GetGuid(Entity e)
    {
        EnsureEntityAlive(e);
        return _guids[e.InternalId];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Space GetSpace(Entity e)
    {
        EnsureEntityAlive(e);
        return _spaces[e.InternalId];
    }

    internal void SetSpace(Entity e, Space newSpace)
    {
        EnsureEntityAlive(e);
        if (newSpace == null)
            throw new ArgumentNullException(nameof(newSpace), $"Cannot assign null Space to Entity (ID: {e.InternalId}).");

        uint id = e.InternalId;
        var oldSpace = _spaces[id];
        if (oldSpace == newSpace) return;

        if (oldSpace != null && _entitiesInSpaces.TryGetValue(oldSpace, out var oldList))
        {
            FastRemove(oldList, e);
        }

        _spaces[id] = newSpace;
        RegisterSpace(newSpace);
        _entitiesInSpaces[newSpace].Add(e);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal EntityMetaData GetMetaData(Entity e)
    {
        EnsureEntityAlive(e);
        return _metaData[e.InternalId];
    }

    internal void SetMetaData(Entity e, EntityMetaData meta)
    {
        EnsureEntityAlive(e);
        _metaData[e.InternalId] = meta;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ReactiveProperty<bool> GetIsEnabled(Entity e)
    {
        EnsureEntityAlive(e);
        uint id = e.InternalId;
        ref var prop = ref _reactiveProperties[id];

        if (prop == null)
        {
            prop = new ReactiveProperty<bool> { Value = _isEnableds[id] };
            prop.OnChange += val => _isEnableds[id] = val;
        }

        return prop;
    }

    private IData[] _dataBuffer = new IData[64];
    private ILogic[] _logicBuffer = new ILogic[64];

    internal ReadOnlySpan<IData> GetDataSpan(Entity e)
    {
        EnsureEntityAlive(e);
        if (!_componentPoolsInSpaces.TryGetValue(e.CurrentSpace, out var pools))
            return ReadOnlySpan<IData>.Empty;

        int count = 0;
        foreach (var pool in pools.Values)
        {
            if (pool.GetComponentFor(e) is IData data)
            {
                if (count >= _dataBuffer.Length)
                {
                    Array.Resize(ref _dataBuffer, _dataBuffer.Length * 2);
                }
                _dataBuffer[count++] = data;
            }
        }

        return new ReadOnlySpan<IData>(_dataBuffer, 0, count);
    }

    internal ReadOnlySpan<ILogic> GetLogicSpan(Entity e)
    {
        EnsureEntityAlive(e);
        if (!_componentPoolsInSpaces.TryGetValue(e.CurrentSpace, out var pools))
            return ReadOnlySpan<ILogic>.Empty;

        int count = 0;
        foreach (var pool in pools.Values)
        {
            if (pool.GetComponentFor(e) is ILogic logic)
            {
                if (count >= _logicBuffer.Length)
                {
                    Array.Resize(ref _logicBuffer, _logicBuffer.Length * 2);
                }
                _logicBuffer[count++] = logic;
            }
        }

        return new ReadOnlySpan<ILogic>(_logicBuffer, 0, count);
    }

    #endregion

    #region Data Methods

    internal void AddData<T>(Entity e, T data) where T : IData
    {
        EnsureEntityAlive(e);
        if (data == null)
            throw new ArgumentNullException(nameof(data), $"Cannot attach null Data of type '{typeof(T).Name}' to Entity (ID: {e.InternalId}).");

        AddReferences(data, e);
        SetBit(e, typeof(T), true);
    }

    internal void RemoveData<T>(Entity e, T data) where T : IData
    {
        EnsureEntityAlive(e);
        RemoveReferences(data, e);
        SetBit(e, typeof(T), false);
    }

    internal T GetData<T>(Entity e) where T : IData
    {
        EnsureEntityAlive(e);
        if (_componentPoolsInSpaces.TryGetValue(e.CurrentSpace, out var pools) &&
            pools.TryGetValue(typeof(T), out var pool))
        {
            return ((ComponentPool<T>)pool).Get(e);
        }

        throw new KeyNotFoundException($"There is no Data of type '{typeof(T).Name}' attached to Entity (ID: {e.InternalId}) in Space '{e.CurrentSpace}'.");
    }

    #endregion

    #region Logic Methods

    internal void AddLogic<T>(Entity e, T logic) where T : ILogic
    {
        EnsureEntityAlive(e);
        if (logic == null)
            throw new ArgumentNullException(nameof(logic), $"Cannot attach null Logic of type '{typeof(T).Name}' to Entity (ID: {e.InternalId}).");

        AddReferences(logic, e);
        SetBit(e, typeof(T), true);
    }

    internal void RemoveLogic<T>(Entity e, T logic) where T : ILogic
    {
        EnsureEntityAlive(e);
        RemoveReferences(logic, e);
        SetBit(e, typeof(T), false);
    }

    internal T GetLogic<T>(Entity e) where T : ILogic
    {
        EnsureEntityAlive(e);
        if (_componentPoolsInSpaces.TryGetValue(e.CurrentSpace, out var pools) &&
            pools.TryGetValue(typeof(T), out var pool))
        {
            return ((ComponentPool<T>)pool).Get(e);
        }

        throw new KeyNotFoundException($"There is no Logic of type '{typeof(T).Name}' attached to Entity (ID: {e.InternalId}) in Space '{e.CurrentSpace}'.");
    }

    #endregion

    #region Bitmask Management

    private void SetBit(Entity entity, Type componentType, bool value)
    {
        uint id = entity.InternalId;
        var compId = ComponentRegistry.GetId(componentType);
        var chunkIndex = compId / 32;
        var bitIndex = compId % 32;

        ref var mask = ref _bitmasks[id];
        if (chunkIndex >= mask.Length)
        {
            Array.Resize(ref mask, Math.Max(chunkIndex + 1, ComponentRegistry.Count / 32 + 1));
        }

        if (value)
        {
            mask[chunkIndex] |= 1u << bitIndex;
        }
        else
        {
            mask[chunkIndex] &= ~(1u << bitIndex);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal uint[] GetBitmask(uint internalId) => _bitmasks[internalId] ?? [];

    #endregion

    #region Component Pools

    private readonly Dictionary<Space, Dictionary<Type, IComponentPool>> _componentPoolsInSpaces = [];

    private ComponentPool<T> RegisterPool<T>(Space space)
    {
        if (space == null)
            throw new ArgumentNullException(nameof(space), $"Cannot register component pool of type '{typeof(T).Name}' for a null Space.");

        var type = typeof(T);
        if (!_componentPoolsInSpaces.TryGetValue(space, out var pools))
        {
            pools = new Dictionary<Type, IComponentPool>();
            _componentPoolsInSpaces[space] = pools;
        }

        if (pools.TryGetValue(type, out var componentPool))
            return (ComponentPool<T>)componentPool;

        var pool = new ComponentPool<T>();
        pools[type] = pool;
        return pool;
    }

    private void AddReferences<T>(T component, Entity entity)
    {
        var rawPool = RegisterPool<T>(entity.CurrentSpace);
        rawPool.Add(component, entity);
    }

    private void RemoveReferences<T>(T _, Entity entity)
    {
        var type = typeof(T);
        if (_componentPoolsInSpaces.TryGetValue(entity.CurrentSpace, out var pools) &&
            pools.TryGetValue(type, out var pool))
        {
            pool.Remove(entity);
        }
    }

    #endregion

    #region Search

    internal IEnumerable<Entity> GetEntitiesIn(Space space)
    {
        if (space == null)
            throw new ArgumentNullException(nameof(space), "Cannot get entities from a null Space.");

        return _entitiesInSpaces.TryGetValue(space, out var list) ? list : Array.Empty<Entity>();
    }

    internal IEnumerable<Entity> GetEntitiesIn(SpaceFolder spaceFolder)
    {
        if (spaceFolder == null)
            throw new ArgumentNullException(nameof(spaceFolder), "Cannot get entities from a null SpaceFolder.");

        if (!_entitiesInSpaces.TryGetValue(spaceFolder.Space, out var entities)) yield break;

        var ids = spaceFolder.EntityIds;
        for (int i = 0; i < entities.Count; i++)
        {
            if (ids.Contains(entities[i].Id))
                yield return entities[i];
        }
    }

    internal IEnumerable<Entity> GetEntitiesInAvailable(Space space)
    {
        if (space == null)
            throw new ArgumentNullException(nameof(space), "Cannot get available entities from a null Space.");

        var availableSpaces = SpaceTree.GetAllAvailableSpacesFor(space);
        for (int i = 0; i < availableSpaces.Count; i++)
        {
            if (_entitiesInSpaces.TryGetValue(availableSpaces[i], out var list))
            {
                for (int j = 0; j < list.Count; j++)
                    yield return list[j];
            }
        }
    }

    internal IEnumerable<Entity> GetEntitiesWith(Space space, params Type[] types)
    {
        if (space == null)
            throw new ArgumentNullException(nameof(space), "Cannot filter entities in a null Space.");

        if (types == null || types.Length == 0) yield break;
        var filter = BuildFilter(types);
        foreach (var entity in GetEntitiesWithFilter(space, filter))
        {
            yield return entity;
        }
    }

    internal IEnumerable<Entity> GetEntitiesInAvailableWith(Space space, params Type[] types)
    {
        if (space == null)
            throw new ArgumentNullException(nameof(space), "Cannot filter available entities in a null Space.");

        if (types == null || types.Length == 0) yield break;
        var filter = BuildFilter(types);
        var availableSpaces = SpaceTree.GetAllAvailableSpacesFor(space);

        for (int i = 0; i < availableSpaces.Count; i++)
        {
            foreach (var entity in GetEntitiesWithFilter(availableSpaces[i], filter))
            {
                yield return entity;
            }
        }
    }
    
    private IEnumerable<Entity> GetEntitiesWithFilter(Space space, uint[] filter)
    {
        if (!_entitiesInSpaces.TryGetValue(space, out var entities)) yield break;

        for (int i = 0; i < entities.Count; i++)
        {
            var mask = GetBitmask(entities[i].InternalId);
            if (IsMatch(mask, filter))
                yield return entities[i];
        }
    }

    private static bool IsMatch(uint[] entityMask, uint[] filter)
    {
        for (var i = 0; i < filter.Length; i++)
        {
            var entityChunk = i < entityMask.Length ? entityMask[i] : 0u;
            if ((entityChunk & filter[i]) != filter[i]) return false;
        }
        return true;
    }
    
    private static uint[] BuildFilter(Type[] types)
    {
        var totalChunks = ComponentRegistry.Count / 32 + 1;
        var filter = new uint[totalChunks];
        for (int i = 0; i < types.Length; i++)
        {
            var id = ComponentRegistry.GetId(types[i]);
            filter[id / 32] |= 1u << (id % 32);
        }
        return filter;
    }

    internal IEnumerable<Entity> GetEntitiesByType<T>(Space space)
    {
        if (space == null)
            throw new ArgumentNullException(nameof(space), "Cannot get entities by type from a null Space.");

        var type = typeof(T);
        if (_componentPoolsInSpaces.TryGetValue(space, out var pools) && pools.TryGetValue(type, out var value))
        {
            var pool = (ComponentPool<T>)value;
            return pool.Entities;
        }

        return [];
    }

    internal IEnumerable<Entity> GetEntitiesByTypeInAvailable<T>(Space space)
    {
        if (space == null)
            throw new ArgumentNullException(nameof(space), "Cannot get available entities by type from a null Space.");

        var availableSpaces = SpaceTree.GetAllAvailableSpacesFor(space);
        for (int i = 0; i < availableSpaces.Count; i++)
        {
            foreach (var entity in GetEntitiesByType<T>(availableSpaces[i]))
            {
                yield return entity;
            }
        }
    }

    internal IEnumerable<T> GetComponentsByType<T>(Space space)
    {
        if (space == null)
            throw new ArgumentNullException(nameof(space), "Cannot get components by type from a null Space.");

        var type = typeof(T);
        if (_componentPoolsInSpaces.TryGetValue(space, out var pools) && pools.TryGetValue(type, out var value))
        {
            var pool = (ComponentPool<T>)value;
            return pool.Components;
        }

        return [];
    }

    internal IEnumerable<T> GetComponentsByTypeInAvailable<T>(Space space)
    {
        if (space == null)
            throw new ArgumentNullException(nameof(space), "Cannot get available components by type from a null Space.");

        var availableSpaces = SpaceTree.GetAllAvailableSpacesFor(space);
        for (int i = 0; i < availableSpaces.Count; i++)
        {
            foreach (var component in GetComponentsByType<T>(availableSpaces[i]))
            {
                yield return component;
            }
        }
    }

    internal T GetComponentByType<T>(Space space)
    {
        if (space == null)
            throw new ArgumentNullException(nameof(space), "Cannot get component by type from a null Space.");

        var type = typeof(T);
        if (_componentPoolsInSpaces.TryGetValue(space, out var pools) && pools.TryGetValue(type, out var value))
        {
            var pool = (ComponentPool<T>)value;
            if (pool.Components.Count > 0)
                return pool.Components[0];
        }

        return default;
    }

    internal T GetComponentByTypeInAvailable<T>(Space space)
    {
        if (space == null)
            throw new ArgumentNullException(nameof(space), "Cannot get available component by type from a null Space.");

        var availableSpaces = SpaceTree.GetAllAvailableSpacesFor(space);
        for (int i = 0; i < availableSpaces.Count; i++)
        {
            var component = GetComponentByType<T>(availableSpaces[i]);
            if (component != null) return component;
        }

        return default;
    }

    internal IEnumerable<IComponentPool> GetComponentPoolsInSpace(Space space)
    {
        if (space == null)
            throw new ArgumentNullException(nameof(space), "Cannot get component pools from a null Space.");

        if (_componentPoolsInSpaces.TryGetValue(space, out var pools))
            return pools.Values;
        return [];
    }

    #endregion

    #region Runners Registration

    internal void RegisterRunner(IUpdateRunner runner)
    {
        if (runner == null)
            throw new ArgumentNullException(nameof(runner), "Cannot register a null IUpdateRunner.");
        UpdateRunners.Add(runner);
    }

    internal void RegisterFixedRunner(IUpdateRunner runner)
    {
        if (runner == null)
            throw new ArgumentNullException(nameof(runner), "Cannot register a null fixed IUpdateRunner.");
        FixedUpdateRunners.Add(runner);
    }

    internal void RegisterLateRunner(IUpdateRunner runner)
    {
        if (runner == null)
            throw new ArgumentNullException(nameof(runner), "Cannot register a null late IUpdateRunner.");
        LateUpdateRunners.Add(runner);
    }

    #endregion

    #region Helpers

    private void EnsureCapacity(uint internalId)
    {
        int index = (int)internalId;
        if (index < _capacity) return;

        int newCapacity = Math.Max(index + 1, _capacity * 2);

        Array.Resize(ref _versions, newCapacity);
        Array.Resize(ref _guids, newCapacity);
        Array.Resize(ref _spaces, newCapacity);
        Array.Resize(ref _metaData, newCapacity);
        Array.Resize(ref _isEnableds, newCapacity);
        Array.Resize(ref _reactiveProperties, newCapacity);
        Array.Resize(ref _bitmasks, newCapacity);

        _capacity = newCapacity;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool FastRemove(List<Entity> list, Entity entity)
    {
        int index = list.IndexOf(entity);
        if (index < 0) return false;

        int lastIndex = list.Count - 1;
        list[index] = list[lastIndex];
        list.RemoveAt(lastIndex);
        return true;
    }

    #endregion
}