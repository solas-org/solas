using Silk.NET.Core;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Solas.Attributes;
using Solas.Components;
using Solas.Enums;
using Solas.Render.Data;
using Solas.Render.Settings;
using Solas.Render.Vulkan;
using Solas.Transform;

namespace Solas.Render.Logics;

[Update, LateUpdate]
public class RenderLogic : ILogic
{
    public Entity Entity { get; set; }
    private WindowSettings _windowSettings = null!;
    private IWindow _window = null!;
    private IRenderer _renderer = null!;
    private TransformData _cameraTransform = null!;
    private CameraData _cameraData = null!;
    private SceneCameraLogic _sceneCameraLogic = null!;
    private bool _isDestroyed;

    private void Setup()
    {
        _cameraTransform = Entity.GetData<TransformData>();
        _cameraData = Entity.GetData<CameraData>();
        _sceneCameraLogic = Entity.GetLogic<SceneCameraLogic>();
        _windowSettings = Query.GetSettings<WindowSettings>();
    }

    private void CreateWindow()
    {
        var options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(_windowSettings.Width, _windowSettings.Height),
            Title = _windowSettings.WindowTitle,
            VSync = false,
            API = (GraphicsBackend)_windowSettings.Api == GraphicsBackend.Vulkan
                ? GraphicsAPI.DefaultVulkan
                : GraphicsAPI.Default,
            FramesPerSecond = WorldContext.CoreSettings.TargetFrameTime,
            WindowState = (WindowState)_windowSettings.StartWindowsState,
        };

        _window = Window.Create(options);
        _window.Initialize();
        _sceneCameraLogic.InputContext = _window.CreateInput();

        var loadedIcon = LoadIcon(_windowSettings.IconPath);
        if (loadedIcon != null)
            _window.SetWindowIcon([(RawImage)loadedIcon]);

        if ((GraphicsBackend)_windowSettings.Api == GraphicsBackend.Vulkan && _window.VkSurface is null)
        {
            throw new Exception("Windowing platform doesn't support Vulkan.");
        }
    }

    private RawImage? LoadIcon(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        string fullPath;
        try
        {
            fullPath = Query.GetPath(path);
        }
        catch
        {
            return null;
        }

        if (!File.Exists(fullPath)) return null;
        var rawBytes = File.ReadAllBytes(fullPath);

        return new RawImage(256, 256, rawBytes);
    }

    private void CreateRenderer()
    {
        _renderer = (GraphicsBackend)_windowSettings.Api switch
        {
            GraphicsBackend.Vulkan => new VulkanRenderer(),
            _ => throw new NotSupportedException(
                $"Graphics backend {(GraphicsBackend)_windowSettings.Api} is not supported.")
        };

        _renderer.Start(_window, _cameraTransform, _cameraData);
        _window.Resize += _renderer.OnResize;
    }

    public void Initialize()
    {
        Setup();
        CreateWindow();
        CreateRenderer();
    }

    public void Update()
    {
        if (_isDestroyed) return;
        if (!_window.IsClosing) _window.DoEvents();
    }

    public void LateUpdate()
    {
        if (_isDestroyed) return;
        if (!_window.IsClosing) _renderer.DrawFrame();
        else
        {
            Engine.State = GameState.None;
            //Game end
        }
    }

    public void Dispose()
    {
        if (_isDestroyed) return;
        _renderer.Dispose();
        _window.Dispose();
        _isDestroyed = true;
    }
}