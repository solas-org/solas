using Solas.Enums;

namespace Solas.Docs.Introduction;

public static class Program
{
    public static void Main(string[] _)
    {
        Engine.SetSerializer(RuntimeConfig.SerializerName);
        CreateVfs();
        Engine.CreateUpdateSystems();
        Engine.CreateWorld();
        Engine.State = GameState.Start;
    }

    private static void CreateVfs()
    {
        var vfs = new VirtualFileSystem(Directory.GetCurrentDirectory());
        vfs.Mount("assets", "Assets");
        vfs.Mount("engine", "Solas");

        Engine.SetVfs(vfs);
        Engine.EnsureNeededDirectories(
            vfs.GetMountPath("assets"),
            vfs.GetMountPath("engine"),
            vfs.GetPath("engine://Settings"));
        Engine.LoadEngineSettings(vfs.GetPath("engine://Settings"));
    }
}

internal record struct RuntimeConfig
{
    internal static readonly string SerializerName;
}