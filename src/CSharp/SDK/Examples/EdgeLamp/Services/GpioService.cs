using Microsoft.VisualBasic;
using System.Collections.Concurrent;
using System.Device.Gpio;

namespace EdgeLamp.Services;

public class GpioService : IDisposable
{
    private readonly ILogger<GpioService> _logger;
    private GpioController? _gpioController;
    private readonly ConcurrentDictionary<int, bool> _openPins = new ConcurrentDictionary<int, bool>();
    private readonly object _lockObject = new object();

    public GpioService(ILogger<GpioService> logger)
    {
        _logger = logger;
        InitializeController();
    }

    private void InitializeController()
    {
        lock (_lockObject)
        {
            if (_gpioController == null)
            {
                try
                {
                    _logger.LogInformation("初始化共享GPIO控制器...");
                    _gpioController = new GpioController(PinNumberingScheme.Logical);
                    _logger.LogInformation("共享GPIO控制器初始化成功 (使用BCM/Logical引脚编号)");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "GPIO控制器初始化失败");
                    throw;
                }
            }
        }
    }

    public GpioController GetController()
    {
        if (_gpioController == null)
        {
            InitializeController();
        }
        return _gpioController!;
    }

    public void InitializePin(int pin, PinMode mode)
    {
        lock (_lockObject)
        {
            try
            {
                if (_gpioController == null)
                {
                    InitializeController();
                }

                if (!_gpioController!.IsPinOpen(pin))
                {
                    _logger.LogInformation($"初始化引脚 {pin} (BCM), 模式: {mode}");
                    _gpioController.OpenPin(pin, mode);
                    _openPins[pin] = true;
                    _logger.LogInformation($"引脚 {pin} (BCM) 初始化成功");
                }
                else
                {
                    _logger.LogWarning($"引脚 {pin} (BCM) 已经打开");
                }
                _logger.LogInformation($"当前已打开的引脚: {string.Join(", ", _openPins.Where(p => p.Value).Select(p => p.Key))}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"初始化引脚 {pin} 失败");
                throw;
            }
        }
    }

    public bool IsPinOpen(int pin)
    {
        return _gpioController?.IsPinOpen(pin) ?? false;
    }

    public void WritePin(int pin, PinValue value)
    {
        if (_gpioController == null)
        {
            throw new InvalidOperationException("GPIO控制器未初始化");
        }

        if (!_gpioController.IsPinOpen(pin))
        {
            throw new InvalidOperationException($"引脚 {pin} 未打开");
        }

        _gpioController.Write(pin, value);
    }

    public void ReleasePin(int pin)
    {
        lock (_lockObject)
        {
            try
            {
                if (_gpioController != null && _gpioController.IsPinOpen(pin))
                {
                    _gpioController.Write(pin, PinValue.Low);
                    _gpioController.ClosePin(pin);
                    _openPins.TryRemove(pin, out _);
                    _logger.LogInformation($"引脚 {pin} (BCM) 已释放");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"释放引脚 {pin} 失败");
            }
        }
    }

    public void Dispose()
    {
        lock (_lockObject)
        {
            try
            {
                _logger.LogInformation("开始清理共享GPIO控制器...");

                if (_gpioController != null)
                {
                    // 关闭所有打开的引脚
                    foreach (var pin in _openPins.Keys.ToArray())
                    {
                        ReleasePin(pin);
                    }

                    _gpioController.Dispose();
                    _gpioController = null;
                    _logger.LogInformation("共享GPIO控制器清理完成");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理GPIO控制器时发生错误");
            }
        }
    }
}