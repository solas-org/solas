using System.Diagnostics;
using System.Runtime.InteropServices;
using Solas.Interfaces;
using Solas.Settings;
using Solas.World;

namespace Solas.Containers;

internal class SpacePool
{
    private readonly List<Space> _localSpaces = [];
    private readonly Dictionary<Space, List<SpaceFolder>> _spaceFolders = [];
    private string[] _localSpacesPaths = [];
    private WorldSettings WorldSettings => Query.GetSettings<WorldSettings>();

    #region Update

    internal void InjectPoolsInUpdateRunners(ReadOnlySpan<IUpdateRunner> runners)
    {
        List<IComponentPool> allContainers = new List<IComponentPool>();

        for (int i = 0; i < _localSpaces.Count; i++)
        {
            allContainers.AddRange(EngineContext.EntityPool.GetComponentPoolsInSpace(_localSpaces[i]));
        }

        if (WorldContext.GlobalSpace != null)
        {
            allContainers.AddRange(EngineContext.EntityPool.GetComponentPoolsInSpace(WorldContext.GlobalSpace));
        }

        var span = CollectionsMarshal.AsSpan(allContainers);
        for (int i = 0; i < runners.Length; i++)
        {
            runners[i]?.InjectPools(span);
        }
    }

    internal void RunUpdateSystemInAllSpaces(IUpdateSystem system)
    {
        if (system == null)
            throw new ArgumentNullException(nameof(system), "Cannot execute update on a null IUpdateSystem.");

        for (int i = 0; i < _localSpaces.Count; i++)
        {
            system.Update(_localSpaces[i]);
        }

        if (WorldContext.GlobalSpace != null)
        {
            system.Update(WorldContext.GlobalSpace);
        }
    }

    #endregion

    #region SpaceFolders

    internal void RegisterSpaceFolder(SpaceFolder folder, Space space)
    {
        if (folder == null)
            throw new ArgumentNullException(nameof(folder), "Cannot register a null SpaceFolder.");
        if (space == null)
            throw new ArgumentNullException(nameof(space), "Cannot register folder to a null Space.");

        if (!_spaceFolders.TryGetValue(space, out var list))
        {
            list = [];
            _spaceFolders[space] = list;
        }

        list.Add(folder);
    }

    internal void UnregisterSpaceFolder(SpaceFolder folder, Space space)
    {
        if (folder == null || space == null) return;

        if (_spaceFolders.TryGetValue(space, out var folders))
        {
            folders.Remove(folder);
        }
    }

    internal SpaceFolder GetSpaceFolderWith(Guid guid, Space space)
    {
        if (space == null)
            throw new ArgumentNullException(nameof(space), "Space cannot be null when querying SpaceFolder.");

        return _spaceFolders.TryGetValue(space, out var folders)
            ? folders.FirstOrDefault(x => x.Id == guid)
            : null;
    }

    internal SpaceFolder GetSpaceFolderWith(Guid guid, Guid spaceId)
    {
        var space = GetSpace(spaceId);
        if (space == null)
            throw new KeyNotFoundException($"Space with ID '{spaceId}' was not found.");

        return GetSpaceFolderWith(guid, space);
    }

    internal IEnumerable<SpaceFolder> GetSpaceFoldersWith(List<Guid> guids, Space space)
    {
        if (guids == null || space == null) return [];

        return _spaceFolders.TryGetValue(space, out var folders)
            ? folders.Where(x => guids.Contains(x.Id))
            : [];
    }

    internal List<SpaceFolder> GetAllSpaceFoldersIn(Space space)
    {
        if (space == null)
            throw new ArgumentNullException(nameof(space), "Space cannot be null.");

        return _spaceFolders.TryGetValue(space, out var folders) ? folders : [];
    }

    #endregion

    #region Spaces

    internal void SetPaths(string localSpacesFolder)
    {
        if (string.IsNullOrWhiteSpace(localSpacesFolder))
            throw new ArgumentException("Local spaces folder path cannot be null or empty.", nameof(localSpacesFolder));

        if (!Directory.Exists(localSpacesFolder))
            throw new DirectoryNotFoundException($"Directory '{localSpacesFolder}' does not exist.");

        _localSpacesPaths = Directory.GetFiles(localSpacesFolder, "*.space", SearchOption.AllDirectories);
    }

    internal string[] GetPaths() => _localSpacesPaths;

    internal IEnumerable<Task> InitializeLocalSpaces()
    {
        return _localSpaces.Where(x => x?.Initializer != null)
                           .SelectMany(x => x.Initializer.InitializeDependencies());
    }

    internal Space GetSpace(Guid guid)
    {
        if (WorldContext.GlobalSpace?.Id == guid)
            return WorldContext.GlobalSpace;

        return _localSpaces.FirstOrDefault(x => x.Id == guid);
    }

    internal Space LoadLocalSpace(string path, Space rootSpace = null)
    {
        var space = LoadSpace(path);
        _localSpaces.Add(space);
        SpaceTree.Attach(space, rootSpace ?? WorldContext.GlobalSpace);
        return space;
    }

    internal Space LoadSpace(string path, bool immediateBuild = true)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Space file path cannot be null or empty.", nameof(path));

        Space space;
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            space = new Space(Guid.NewGuid())
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Path = path,
                Initializer =
                {
                    Pool = new InitializationPool()
                }
            };
        }
        else
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read);
            space = EngineContext.Serializer.Read<Space>(stream);
            if (space == null)
                throw new InvalidOperationException($"Failed to deserialize Space from file: '{path}'.");

            space.Name = Path.GetFileNameWithoutExtension(path);
            space.Path = path;
        }

        Debug.WriteLine($"Loading space: {space.Name} with id {space.Id}");
        if (immediateBuild)
            EngineContext.DISystem.BuildDependencies(space);

        return space;
    }

    internal void LoadSavedSpaces()
    {
        if (WorldSettings.Spaces == null) return;

        foreach (var path in WorldSettings.Spaces)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            _localSpaces.Add(LoadSpace(path, false));
        }

        foreach (var space in _localSpaces)
            EngineContext.DISystem.BuildDependencies(space);

        SpaceTree.Create(_localSpaces);
    }

    internal void UnloadSpace(Space space)
    {
        if (space == null) return;

        _localSpaces.Remove(space);
        _spaceFolders.Remove(space);
        SpaceTree.Detach(space);
        EngineContext.Destroyer.DestroyIn(space);
        EngineContext.EntityPool.UnregisterSpace(space);
    }

    internal void UnloadAllSpaces()
    {
        var spacesToUnload = _localSpaces.ToArray();
        foreach (var space in spacesToUnload)
        {
            UnloadSpace(space);
        }

        if (WorldContext.GlobalSpace != null)
        {
            UnloadSpace(WorldContext.GlobalSpace);
        }
    }

    internal void SaveSpace(Space space)
    {
        if (space == null)
            throw new ArgumentNullException(nameof(space), "Cannot save a null Space.");

        if (string.IsNullOrWhiteSpace(space.Path))
            throw new InvalidOperationException($"Cannot save Space '{space.Name}' because its Path property is not set.");

        var dir = Path.GetDirectoryName(space.Path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var stream = File.Open(space.Path, FileMode.Create, FileAccess.Write);
        EngineContext.Serializer.Write(space, stream);
    }

    #endregion
}