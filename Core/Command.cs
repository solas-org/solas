using Solas.Assets;
using Solas.Components;
using Solas.Interfaces;
using Solas.Systems;
using Solas.World;

namespace Solas;

public static class Command
{
    #region Settings System

    public static void WriteExistingSettings(IData settings)
    {
        EngineContext.SettingsSystem.WriteExistingSettings(settings);
    }

    public static void WriteNewSettings(IData settings, string path)
    {
        EngineContext.SettingsSystem.WriteNewSettings(settings, path);
    }

    #endregion

    #region DI System

    public static T AutoInject<T>(Space space) where T : ILogic
    {
        return EngineContext.DISystem.AutoInject<T>(space);
    }

    public static T Inject<T>(Guid id, Guid spaceId) where T : IReferenceable
    {
        return DISystem.Inject<T>(id, spaceId);
    }

    #endregion

    #region Assets Pool

    public static void RegisterNewAsset(Asset asset)
    {
        EngineContext.AssetsPool.RegisterNewAsset(asset);
    }

    public static void WriteAsset(Asset asset, FileStream stream, BinaryWriter binaryWriter)
    {
        EngineContext.AssetsPool.WriteAsset(asset, stream, binaryWriter);
    }

    public static void WritePrefab(Entity entity, FileStream stream, BinaryWriter binaryWriter)
    {
        EngineContext.AssetsPool.WritePrefab(entity, stream, binaryWriter);
    }

    public static void SaveAsset(Asset asset)
    {
        EngineContext.AssetsPool.SaveAsset(asset);
    }

    public static void SaveNewAssets()
    {
        EngineContext.AssetsPool.SaveNewAssets();
    }

    public static void SaveAsPrefab(Entity entity)
    {
        EngineContext.AssetsPool.SaveAsPrefab(entity);
    }

    #endregion

    #region Space Pool

    public static Space LoadSpace(string path, bool immediateBuild = true)
    {
        return EngineContext.SpacePool.LoadSpace(path, immediateBuild);
    }

    public static Space LoadLocalSpace(string path, Space rootSpace = null)
    {
        return EngineContext.SpacePool.LoadLocalSpace(path, rootSpace);
    }

    public static void UnloadSpace(Space space)
    {
        EngineContext.SpacePool.UnloadSpace(space);
    }

    public static void SaveSpace(Space space)
    {
        EngineContext.SpacePool.SaveSpace(space);
    }

    #endregion

    #region Entity Pool

    public static void RegisterRunner(IUpdateRunner runner)
    {
        EngineContext.EntityPool.RegisterRunner(runner);
    }

    public static void RegisterFixedRunner(IUpdateRunner runner)
    {
        EngineContext.EntityPool.RegisterFixedRunner(runner);
    }

    public static void RegisterLateRunner(IUpdateRunner runner)
    {
        EngineContext.EntityPool.RegisterLateRunner(runner);
    }

    #endregion

    #region Registries

    public static void RegisterDataRead<T>(string typeName) where T : IData
    {
        EngineContext.DataSerializationRegistry.Register<T>(typeName);
    }

    public static void RegisterLogicAdd<T>(string typeName) where T : ILogic, new()
    {
        EngineContext.LogicAddingRegistry.Register<T>(typeName);
    }

    #endregion
}