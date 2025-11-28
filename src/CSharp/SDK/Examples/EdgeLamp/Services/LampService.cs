using System.Device.Gpio;

namespace EdgeLamp.Services;

public class LampService
{
    private readonly ILogger<LampService> _logger;
    private readonly GpioService _gpioService;
    //private const int GPIO_PIN = 12; // 树莓派为12
    private const int GPIO_PIN = 73; // 香橙派PC9引脚对应的BCM/Logical编号 73
    private bool _isRunning = false;

    // 添加状态跟踪字段
    private DateTime _startTime;
    private int _currentTimes = 0;
    private int _targetTimes = 0;
    private double _currentInterval = 0;
    private bool _isAsync = false;

    public LampService(ILogger<LampService> logger, GpioService gpioService)
    {
        _logger = logger;
        _gpioService = gpioService;
        try
        {
            _logger.LogInformation("LampService构造函数开始初始化...");
            InitializeGpio();
            _logger.LogInformation("LampService构造函数初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LampService构造函数初始化失败");
            throw;
        }
    }

    private void InitializeGpio()
    {
        try
        {
            _logger.LogInformation("开始初始化GPIO...");

            // 初始化灯光控制引脚
            _gpioService.InitializePin(GPIO_PIN, PinMode.Output);

            // 测试引脚 Test pin
            _logger.LogInformation("测试引脚...");
            _gpioService.WritePin(GPIO_PIN, PinValue.High);
            Thread.Sleep(100);
            _gpioService.WritePin(GPIO_PIN, PinValue.Low);
            _logger.LogInformation("引脚测试完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GPIO初始化失败");
            throw new Exception($"GPIO初始化失败: {ex.Message}");
        }
    }

    public void StartBlinking(int times = -1, double interval = 1.0, bool forceRestart = false)
    {
        if (_isRunning)
        {
            if (forceRestart)
            {
                _logger.LogInformation("强制停止当前闪烁任务，开始新任务");
                StopBlinking();
                // 等待一小段时间确保当前任务停止
                Thread.Sleep(100);
            }
            else
            {
                _logger.LogWarning("灯已在闪烁中，忽略新的闪烁请求");
                return;
            }
        }

        try
        {
            _logger.LogInformation($"开始闪烁: 次数={times}, 间隔={interval}秒");

            if (!_gpioService.IsPinOpen(GPIO_PIN))
            {
                _logger.LogError($"引脚 {GPIO_PIN} (BCM) 未打开，尝试重新打开...");
                _gpioService.InitializePin(GPIO_PIN, PinMode.Output);
            }

            // 初始化状态
            _isRunning = true;
            _isAsync = false;
            _startTime = DateTime.Now;
            _currentTimes = 0;
            _targetTimes = times;
            _currentInterval = interval;

            _logger.LogInformation("开始闪烁循环...");

            while (_isRunning && (times == -1 || _currentTimes < times))
            {
                try
                {
                    // 输出高电平 Output HIGH
                    _gpioService.WritePin(GPIO_PIN, PinValue.High);
                    _logger.LogDebug($"引脚 {GPIO_PIN} (BCM) 设置为高电平");
                    Thread.Sleep((int)(interval * 1000)); // 延时指定秒数

                    // 输出低电平 Output LOW
                    _gpioService.WritePin(GPIO_PIN, PinValue.Low);
                    _logger.LogDebug($"引脚 {GPIO_PIN} (BCM) 设置为低电平");
                    Thread.Sleep((int)(interval * 1000)); // 延时指定秒数

                    if (times != -1)
                    {
                        _currentTimes++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "闪烁循环中发生错误");
                    throw;
                }
            }

            // 确保最后是关闭状态 Ensure final state is off
            if (_gpioService.IsPinOpen(GPIO_PIN))
            {
                _gpioService.WritePin(GPIO_PIN, PinValue.Low);
                _logger.LogInformation($"引脚 {GPIO_PIN} (BCM) 设置为低电平");
            }

            _logger.LogInformation("同步灯闪烁完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动闪烁失败");
            throw new Exception($"启动闪烁失败: {ex.Message}");
        }
        finally
        {
            // ✅ 关键修复：无论成功还是失败，都要重置状态
            _isRunning = false;
            _currentTimes = 0;
            _targetTimes = 0;
            _currentInterval = 0;
            _logger.LogInformation("同步闪烁任务状态已重置");
        }
    }

    /// <summary>
    /// 异步启动灯闪烁（不等待完成）
    /// </summary>
    /// <param name="times">闪烁次数，-1表示无限闪烁</param>
    /// <param name="interval">闪烁间隔（秒）</param>
    /// <param name="forceRestart">是否强制重启（停止当前任务并开始新任务）</param>
    public void StartBlinkingAsync(int times = -1, double interval = 1.0, bool forceRestart = false)
    {
        if (_isRunning)
        {
            if (forceRestart)
            {
                _logger.LogInformation("强制停止当前闪烁任务，开始新任务");
                StopBlinking();
                // 等待一小段时间确保当前任务停止
                Thread.Sleep(100);
            }
            else
            {
                _logger.LogWarning("灯已在闪烁中，忽略新的闪烁请求");
                return;
            }
        }

        // 初始化状态
        _isRunning = true;
        _isAsync = true;
        _startTime = DateTime.Now;
        _currentTimes = 0;
        _targetTimes = times;
        _currentInterval = interval;

        // 在后台任务中运行灯闪烁控制
        Task.Run(() =>
        {
            try
            {
                _logger.LogInformation($"开始异步闪烁: 次数={times}, 间隔={interval}秒");

                if (!_gpioService.IsPinOpen(GPIO_PIN))
                {
                    _logger.LogError($"引脚 {GPIO_PIN} (BCM) 未打开，尝试重新打开...");
                    _gpioService.InitializePin(GPIO_PIN, PinMode.Output);
                }

                _logger.LogInformation("开始异步闪烁循环...");

                while (_isRunning && (times == -1 || _currentTimes < times))
                {
                    try
                    {
                        // 输出高电平 Output HIGH
                        _gpioService.WritePin(GPIO_PIN, PinValue.High);
                        _logger.LogDebug($"引脚 {GPIO_PIN} (BCM) 设置为高电平");
                        Thread.Sleep((int)(interval * 1000)); // 延时指定秒数

                        // 输出低电平 Output LOW
                        _gpioService.WritePin(GPIO_PIN, PinValue.Low);
                        _logger.LogDebug($"引脚 {GPIO_PIN} (BCM) 设置为低电平");
                        Thread.Sleep((int)(interval * 1000)); // 延时指定秒数

                        if (times != -1)
                        {
                            _currentTimes++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "异步闪烁循环中发生错误");
                        throw;
                    }
                }

                // 确保最后是关闭状态 Ensure final state is off
                if (_gpioService.IsPinOpen(GPIO_PIN))
                {
                    _gpioService.WritePin(GPIO_PIN, PinValue.Low);
                }

                _logger.LogInformation("异步灯闪烁完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "异步启动闪烁失败");
                throw new Exception($"异步启动闪烁失败: {ex.Message}");
            }
            finally
            {
                // ✅ 关键修复：无论成功还是失败，都要重置状态
                _isRunning = false;
                _currentTimes = 0;
                _targetTimes = 0;
                _currentInterval = 0;
                _logger.LogInformation("异步闪烁任务状态已重置");
            }
        });
    }

    public void StopBlinking()
    {
        try
        {
            _logger.LogInformation("停止闪烁...");
            _isRunning = false;

            if (_gpioService.IsPinOpen(GPIO_PIN))
            {
                _gpioService.WritePin(GPIO_PIN, PinValue.Low);
                _logger.LogInformation($"引脚 {GPIO_PIN} (BCM) 设置为低电平");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止闪烁失败");
            throw new Exception($"停止闪烁失败: {ex.Message}");
        }
    }

    public void Dispose()
    {
        try
        {
            _logger.LogInformation("开始清理LampService资源...");
            StopBlinking();
            // 注意：不需要释放GPIO服务，因为它是共享的
            _logger.LogInformation("LampService资源清理完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理LampService时发生错误");
            throw;
        }
    }

    /// <summary>
    /// 获取灯的当前状态
    /// </summary>
    /// <returns>灯的状态信息</returns>
    public LampStatus GetStatus()
    {
        return new LampStatus
        {
            IsRunning = _isRunning,
            IsAsync = _isAsync,
            StartTime = _startTime,
            CurrentTimes = _currentTimes,
            TargetTimes = _targetTimes,
            CurrentInterval = _currentInterval,
            RunningDuration = _isRunning ? DateTime.Now - _startTime : TimeSpan.Zero,
            Progress = _targetTimes > 0 ? (double)_currentTimes / _targetTimes * 100 : 0
        };
    }

    /// <summary>
    /// 检查灯是否正在运行
    /// </summary>
    /// <returns>是否正在运行</returns>
    public bool IsRunning => _isRunning;
}

/// <summary>
/// 灯状态信息
/// </summary>
public class LampStatus
{
    /// <summary>
    /// 是否正在运行
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// 是否为异步模式
    /// </summary>
    public bool IsAsync { get; set; }

    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 当前已完成次数
    /// </summary>
    public int CurrentTimes { get; set; }

    /// <summary>
    /// 目标次数（-1表示无限循环）
    /// </summary>
    public int TargetTimes { get; set; }

    /// <summary>
    /// 当前闪烁间隔（秒）
    /// </summary>
    public double CurrentInterval { get; set; }

    /// <summary>
    /// 运行时长
    /// </summary>
    public TimeSpan RunningDuration { get; set; }

    /// <summary>
    /// 进度百分比（0-100，无限循环时为0）
    /// </summary>
    public double Progress { get; set; }
}