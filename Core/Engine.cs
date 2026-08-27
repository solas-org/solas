using Solas.Enums;
using Solas.Interfaces;
using Solas.Registries;
using Solas.Serialization.Core;
using Solas.Settings;

namespace Solas;

public static class Engine
{
    public static GameState State
    {
        get;
        set
        {
            field = value;
            switch (value)
            {
                case GameState.Start:
                    StartGame();
                    break;

                case GameState.Update:
                    StartUpdate();
                    break;

                case GameState.Pause:
                    StopUpdate();
                    break;

                case GameState.None:
                    StopGame();
                    break;
            }
        }
    }

    public static void SetSerializer(string typeName)
    {
        EngineContext.Serializer = (Serializer)Activator.CreateInstance(Type.GetType(typeName));
        EngineContext.DataSerializationRegistry = new DataSerializationRegistry();
        EngineContext.AssetsSerializationRegistry = new AssetsSerializationRegistry();
        EngineContext.LogicAddingRegistry = new LogicAddingRegistry();
        new UpdateRunnersRegistry().RegisterAll();
    }

    public static void UpdateSerializer(Serializer serializer)
    {
        EngineContext.Serializer = serializer;
        EngineContext.DataSerializationRegistry = new DataSerializationRegistry();
        EngineContext.AssetsSerializationRegistry = new AssetsSerializationRegistry();
    }

    public static void SetVfs(VirtualFileSystem vfs)
    {
        EngineContext.Vfs = vfs;
    }

    public static void LoadEngineSettings(string pathToSettingsFolder)
    {
        EngineContext.SettingsSystem.ReadAllSettings(pathToSettingsFolder);
        WorldContext.CoreSettings = EngineContext.SettingsSystem.GetSettings<CoreSettings>();
    }

    public static void EnsureNeededDirectories(params string[] directories)
    {
        foreach (var directory in directories)
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory!);
    }

    public static void CreateUpdateSystems()
    {
        foreach (var typeName in WorldContext.CoreSettings.UpdateSystems)
        {
            var type = Type.GetType(typeName)!;
            var instance = (IUpdateSystem)Activator.CreateInstance(type)!;
            switch (instance.UpdateType)
            {
                case UpdateType.Update:
                    EngineContext.Updater.UpdateSystems.Add(instance);
                    break;
                case UpdateType.FixedUpdate:
                    EngineContext.Updater.FixedUpdateSystems.Add(instance);
                    break;
                case UpdateType.LateUpdate:
                    EngineContext.Updater.LateUpdateSystems.Add(instance);
                    break;
            }
        }
    }

    public static void CreateWorld()
    {
        EngineContext.AssetsPool.ReadPointers();

        WorldContext.GlobalSpace = EngineContext.SpacePool.LoadSpace(EngineContext.Vfs
            .GetPath(WorldContext.CoreSettings.GlobalSpacePath), false);
        EngineContext.DISystem.BuildDependencies(WorldContext.GlobalSpace);

        EngineContext.SpacePool.SetPaths(EngineContext.Vfs
            .GetPath(WorldContext.CoreSettings.LocalSpacesDirectory));
        EngineContext.SpacePool.LoadSavedSpaces();
    }

    private static void StartGame()
    {
        var initializationTasks = WorldContext.GlobalSpace.Initializer.InitializeDependencies().ToList();
        initializationTasks.AddRange(EngineContext.SpacePool.InitializeLocalSpaces());
        Task.WhenAll(initializationTasks).Wait();
        State = GameState.Update;
    }

    private static void StartUpdate()
    {
        Time.TimeScale = 1;
        EngineContext.Updater.Start();
    }

    private static void StopUpdate()
    {
        Time.TimeScale = 0;
    }

    private static void StopGame()
    {
        EngineContext.Updater.Stop();
        EngineContext.SpacePool.UnloadAllSpaces();
        EngineContext.AssetsPool.UnloadAllAssets();
    }
}