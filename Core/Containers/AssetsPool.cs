using Solas.Assets;
using Solas.Components;
using Solas.Interfaces;
using Solas.Serialization.Binary;

namespace Solas.Containers;

internal class AssetsPool
{
    private readonly List<Asset> _createdAssets = [];
    private readonly List<Asset> _loadedAssets = [];
    private Dictionary<Guid, uint> _assetsPointers = new();
    private Dictionary<Guid, uint> _prefabPointers = new();

    private static void EnsureFileAndDirectoryExists(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new InvalidOperationException("Resolved file path cannot be null or empty.");

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(filePath))
        {
            using var created = File.Create(filePath);
        }
    }

    internal void ReadPointers()
    {
        var rawPackPath = WorldContext.CoreSettings?.AssetsPackPath 
            ?? throw new InvalidOperationException("CoreSettings.AssetsPackPath is not configured.");
        var rawSpacePath = WorldContext.CoreSettings?.AssetsSpacePath 
            ?? throw new InvalidOperationException("CoreSettings.AssetsSpacePath is not configured.");

        var assetsPackPath = EngineContext.Vfs.GetPath(rawPackPath);
        var assetsSpacePath = EngineContext.Vfs.GetPath(rawSpacePath);

        EnsureFileAndDirectoryExists(assetsPackPath);
        EnsureFileAndDirectoryExists(assetsSpacePath);

        var packLookupPath = assetsPackPath + ".lookup";
        var spaceLookupPath = assetsSpacePath + ".lookup";

        _assetsPointers = File.Exists(packLookupPath) 
            ? IdLookupSerializer.ReadAll(packLookupPath) 
            : new Dictionary<Guid, uint>();

        _prefabPointers = File.Exists(spaceLookupPath) 
            ? IdLookupSerializer.ReadAll(spaceLookupPath) 
            : new Dictionary<Guid, uint>();
    }

    internal void RegisterNewAsset(Asset asset)
    {
        if (asset == null)
            throw new ArgumentNullException(nameof(asset), "Cannot register a null asset.");

        _createdAssets.Add(asset);
    }

    internal Asset GetLoadedAsset(Guid id)
    {
        return _loadedAssets.Find(x => x.Id == id);
    }

    internal T GetAsset<T>(Guid id) where T : Asset
    {
        var existing = _loadedAssets.Find(x => x.Id == id);
        if (existing != null)
        {
            if (existing is T typedAsset) return typedAsset;
            throw new InvalidCastException($"Loaded asset '{id}' is of type '{existing.GetType().FullName}', expected '{typeof(T).FullName}'.");
        }

        return LoadAsset<T>(id);
    }

    internal Asset GetUnknownAsset(FileStream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream), "FileStream cannot be null.");

        var typeName = EngineContext.Serializer.ReadString(stream);
        if (string.IsNullOrEmpty(typeName)) return null;

        var asset = EngineContext.AssetsSerializationRegistry.Read(typeName, stream);
        if (asset == null)
            throw new InvalidOperationException($"Failed to deserialize asset for registered type: '{typeName}'.");

        return asset;
    }

    internal void WriteAsset(Asset asset, FileStream stream, BinaryWriter binaryWriter)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(binaryWriter);

        IdLookupSerializer.Write(binaryWriter, asset.Id, (uint)stream.Position);
        var type = asset.GetType();

        EngineContext.Serializer.Write($"{type.FullName}, {type.Assembly.GetName().Name}", stream);
        EngineContext.Serializer.BeginObject(stream);
        EngineContext.AssetsSerializationRegistry.Write(type, asset, stream);
        EngineContext.Serializer.EndObject(stream);
    }

    internal void WritePrefab(Entity entity, FileStream stream, BinaryWriter binaryWriter)
    {
        if (entity.IsNull) throw new ArgumentNullException(nameof(entity));
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(binaryWriter);

        IdLookupSerializer.Write(binaryWriter, entity.Id, (uint)stream.Position);

        EngineContext.Serializer.BeginObject(stream);
        EngineContext.Serializer.Write(entity, stream);
        EngineContext.Serializer.EndObject(stream);
    }

    internal void SaveNewAssets()
    {
        if (_createdAssets.Count == 0) return;

        var assetsPackPath = EngineContext.Vfs.GetPath(WorldContext.CoreSettings.AssetsPackPath);
        EnsureFileAndDirectoryExists(assetsPackPath);

        using var stream = File.Open(assetsPackPath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        using var binaryWriter = new BinaryWriter(File.Open(assetsPackPath + ".lookup", FileMode.OpenOrCreate, FileAccess.Write));

        binaryWriter.BaseStream.Seek(0, SeekOrigin.End);

        EngineContext.Serializer.Open(stream, stream.Length == 0);
        foreach (var asset in _createdAssets)
            WriteAsset(asset, stream, binaryWriter);
        EngineContext.Serializer.Close(stream);

        binaryWriter.Flush();
        _createdAssets.Clear();
    }

    internal void SaveAsset(Asset asset)
    {
        if (asset == null)
            throw new ArgumentNullException(nameof(asset), "Cannot save a null asset.");

        var assetsPackPath = EngineContext.Vfs.GetPath(WorldContext.CoreSettings.AssetsPackPath);
        EnsureFileAndDirectoryExists(assetsPackPath);

        using var stream = File.Open(assetsPackPath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        using var binaryWriter = new BinaryWriter(File.Open(assetsPackPath + ".lookup", FileMode.OpenOrCreate, FileAccess.Write));

        binaryWriter.BaseStream.Seek(0, SeekOrigin.End);

        EngineContext.Serializer.Open(stream, stream.Length == 0);
        WriteAsset(asset, stream, binaryWriter);
        EngineContext.Serializer.Close(stream);

        binaryWriter.Flush();
    }

    internal T LoadAsset<T>(Guid id) where T : IReferenceable
    {
        if (!_assetsPointers.TryGetValue(id, out var pointer))
            throw new KeyNotFoundException($"Asset with ID '{id}' was not found in the asset lookup index.");

        var assetsPackPath = EngineContext.Vfs.GetPath(WorldContext.CoreSettings.AssetsPackPath);
        if (!File.Exists(assetsPackPath))
            throw new FileNotFoundException($"Asset pack file was not found at '{assetsPackPath}'.", assetsPackPath);

        using var stream = File.Open(assetsPackPath, FileMode.Open, FileAccess.Read);
        if (pointer >= stream.Length)
            throw new IndexOutOfRangeException($"Asset pointer {pointer} exceeds file length {stream.Length} for asset ID '{id}'.");

        stream.Position = pointer;
        var asset = EngineContext.Serializer.Read<T>(stream);
        if (asset == null)
            throw new InvalidOperationException($"Failed to deserialize asset ID '{id}' as type '{typeof(T).FullName}'.");

        if (asset is Asset assetObj)
            _loadedAssets.Add(assetObj);

        return asset;
    }

    internal void SaveAsPrefab(Entity entity)
    {
        if (entity.IsNull)
            throw new ArgumentNullException(nameof(entity), "Cannot save null Entity as prefab.");

        var assetsSpacePath = EngineContext.Vfs.GetPath(WorldContext.CoreSettings.AssetsSpacePath);
        EnsureFileAndDirectoryExists(assetsSpacePath);

        using var stream = File.Open(assetsSpacePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        using var binaryWriter = new BinaryWriter(File.Open(assetsSpacePath + ".lookup", FileMode.OpenOrCreate, FileAccess.Write));

        binaryWriter.BaseStream.Seek(0, SeekOrigin.End);

        EngineContext.Serializer.Open(stream, stream.Length == 0);
        WritePrefab(entity, stream, binaryWriter);
        EngineContext.Serializer.Close(stream);

        binaryWriter.Flush();
    }

    internal Entity LoadPrefab(Guid id)
    {
        if (!_prefabPointers.TryGetValue(id, out var pointer))
            throw new KeyNotFoundException($"Prefab with ID '{id}' was not found in the prefab lookup index.");

        var assetsSpacePath = EngineContext.Vfs.GetPath(WorldContext.CoreSettings.AssetsSpacePath);
        if (!File.Exists(assetsSpacePath))
            throw new FileNotFoundException($"Prefab storage file was not found at '{assetsSpacePath}'.", assetsSpacePath);

        using var stream = File.Open(assetsSpacePath, FileMode.Open, FileAccess.Read);
        if (pointer >= stream.Length)
            throw new IndexOutOfRangeException($"Prefab pointer {pointer} exceeds file length {stream.Length} for prefab ID '{id}'.");

        stream.Position = pointer;
        var entity = EngineContext.Serializer.Read<Entity>(stream);
        if (entity.IsNull)
            throw new InvalidOperationException($"Failed to deserialize prefab Entity for ID '{id}'.");

        EngineContext.DISystem.BuildDependencies(WorldContext.GlobalSpace);

        return entity;
    }

    internal void UnloadAllAssets()
    {
        _createdAssets.Clear();
        _loadedAssets.Clear();
        _assetsPointers.Clear();
        _prefabPointers.Clear();
    }
}