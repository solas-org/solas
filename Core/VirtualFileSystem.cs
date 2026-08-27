namespace Solas;

public class VirtualFileSystem
{
    private readonly string _rootDirectory;
    private readonly Dictionary<string, string> _mounts = new(StringComparer.OrdinalIgnoreCase);

    public VirtualFileSystem(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Root directory path cannot be null or empty.", nameof(rootDirectory));

        _rootDirectory = rootDirectory;
    }

    public void Mount(string mountName, string mountPath)
    {
        if (string.IsNullOrWhiteSpace(mountName))
            throw new ArgumentException("Mount name cannot be null or empty.", nameof(mountName));

        if (string.IsNullOrWhiteSpace(mountPath))
            throw new ArgumentException("Mount path cannot be null or empty.", nameof(mountPath));

        _mounts[mountName] = Path.Combine(_rootDirectory, mountPath);
    }
    
    public string GetMountPath(string mount)
    {
        if (string.IsNullOrWhiteSpace(mount))
            throw new ArgumentException("Mount name cannot be null or empty.", nameof(mount));

        if (!_mounts.TryGetValue(mount, out var path))
            throw new KeyNotFoundException($"Mount '{mount}' is not registered in VirtualFileSystem.");

        return path;
    }

    public string GetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Virtual path cannot be null or empty.", nameof(path));

        var parts = path.Split(["://"], StringSplitOptions.None);

        if (parts.Length == 1)
            return GetMountPath(parts[0]);

        if (parts.Length > 2)
            throw new FormatException($"Malformed virtual path: '{path}'. Multiple '://' delimiters are not allowed.");

        var mount = parts[0];
        var relativePath = parts[1];

        return Path.Combine(GetMountPath(mount), relativePath);
    }
}