using Microsoft.Extensions.Logging;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Models;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Services;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Senparc.CO2NET.Exceptions;
using Senparc.Ncf.Core.Exceptions;
using Senparc.Xncf.NeuCharBoxEdgeSimp.OHS.Local.PL;
using Newtonsoft.Json;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Helper;
using System.Text.RegularExpressions;

namespace Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.BackgroundServices
{
    /// <summary>
    /// Linux蓝牙系统调用定义
    /// </summary>
    public static class BluetoothSyscalls
    {
        // 蓝牙地址结构
        [StructLayout(LayoutKind.Sequential)]
        public struct bdaddr_t
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] b;
            
            public bdaddr_t(byte[] address)
            {
                b = new byte[6];
                if (address != null && address.Length == 6)
                    Array.Copy(address, b, 6);
            }
        }

        // RFCOMM套接字地址结构
        [StructLayout(LayoutKind.Sequential)]
        public struct sockaddr_rc
        {
            public ushort rc_family;    // AF_BLUETOOTH = 31
            public bdaddr_t rc_bdaddr;  // 蓝牙地址
            public byte rc_channel;     // RFCOMM通道
        }

        // 套接字常量
        public const int AF_BLUETOOTH = 31;
        public const int SOCK_STREAM = 1;
        public const int BTPROTO_RFCOMM = 3;
        public const int SOL_SOCKET = 1;
        public const int SO_REUSEADDR = 2;

        // Linux系统调用 - 添加正确的调用约定
        [DllImport("libc", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        public static extern int socket(int domain, int type, int protocol);

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        public static extern int bind(int sockfd, ref sockaddr_rc addr, int addrlen);

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        public static extern int listen(int sockfd, int backlog);

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        public static extern int accept(int sockfd, ref sockaddr_rc addr, ref int addrlen);

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        public static extern int recv(int sockfd, byte[] buf, int len, int flags);

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        public static extern int send(int sockfd, byte[] buf, int len, int flags);

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        public static extern int close(int fd);

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        public static extern int setsockopt(int sockfd, int level, int optname, ref int optval, int optlen);

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr strerror(int errnum);

        [DllImport("libc", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr __errno_location();
        
        // 安全的errno获取方法
        public static int errno()
        {
            try
            {
                var errnoPtr = __errno_location();
                if (errnoPtr == IntPtr.Zero)
                {
                    return -1; // errno指针无效
                }
                return Marshal.ReadInt32(errnoPtr);
            }
            catch (AccessViolationException)
            {
                return 104; // ECONNRESET - 连接被重置
            }
            catch
            {
                return -1; // 如果获取errno失败，返回通用错误
            }
        }
    }

    /// <summary>
    /// 经典蓝牙RFCOMM服务端 - 真正的原生C#实现
    /// Classic Bluetooth RFCOMM Server - Real native C# implementation
    /// </summary>
    public class BluetoothBackgroundService : Microsoft.Extensions.Hosting.BackgroundService
    {
        private readonly ILogger<BluetoothBackgroundService> _logger;
        private readonly SenderReceiverSet _senderReceiverSet;
        private readonly WifiManagerService _wifiManagerService;
        
        // 连接的客户端管理
        private readonly ConcurrentDictionary<string, BluetoothClientConnection> _connectedClients;
        
        // RFCOMM服务端配置
        private const int RFCOMM_CHANNEL = 1;
        private const string SERVICE_NAME = "NeuChar-RFCOMM-Service";
        private readonly string SERVICE_UUID; // 从配置读取或生成唯一UUID
        
        // 设备信息
        private string _deviceName;
        private string _bluetoothName;
        private string _bluetoothAddress;
        
        // 服务端进程和状态
        private Process _rfcommListenerProcess;
        private Process _bluetoothAgentProcess;
        private volatile bool _isRunning = false;
        private string _namedPipePath;

        public BluetoothBackgroundService(
            ILogger<BluetoothBackgroundService> logger,
            SenderReceiverSet senderReceiverSet,
            WifiManagerService wifiManagerService)
        {
            _logger = logger;
            _senderReceiverSet = senderReceiverSet;
            _wifiManagerService = wifiManagerService;
            _connectedClients = new ConcurrentDictionary<string, BluetoothClientConnection>();
            _deviceName = _senderReceiverSet.deciveName ?? "NeuChar-EdgeDevice";
            
            // 生成蓝牙名称：NCBEdge_{DID的最后6位}
            var did = _senderReceiverSet.dId ?? "DEFAULT";
            var lastSixDigits ="";
            if (did != "DEFAULT") {
                var splitDid = did.Split("-");
                if (splitDid.Length > 1) {
                    lastSixDigits = splitDid[splitDid.Length - 2].Length >= 4 ? splitDid[splitDid.Length - 2].Substring(splitDid[splitDid.Length - 2].Length - 4) : splitDid[splitDid.Length - 2].PadLeft(4, '0');
                    lastSixDigits += "-" + splitDid[splitDid.Length - 1];
                } else {
                    lastSixDigits = did.Length >= 6 ? did.Substring(did.Length - 6) : did.PadLeft(6, '0');
                }
            }
            else {
                lastSixDigits = did;
            }

            //_bluetoothName = $"NCBEdge_{lastSixDigits}";
            _bluetoothName = $"NCBEdge_{lastSixDigits}_{_deviceName}";
            
            // 配置SERVICE_UUID（支持多种自定义方式）
            SERVICE_UUID = GetOrGenerateServiceUUID(_senderReceiverSet);
            
            _namedPipePath = $"/tmp/neuchar_bluetooth_{DateTime.Now.Ticks}";
        }

        /// <summary>
        /// 获取或生成SERVICE_UUID（支持多种自定义方式）
        /// </summary>
        /// <param name="senderReceiverSet">配置对象</param>
        /// <returns>SERVICE_UUID字符串</returns>
        private string GetOrGenerateServiceUUID(SenderReceiverSet senderReceiverSet)
        {
            try
            {
                // 方式2：基于DID生成唯一UUID（每个设备不同）
                var did = senderReceiverSet.dId ?? "DEFAULT";
                if (!string.IsNullOrEmpty(did) && did != "DEFAULT")
                {
                    var deviceHash = Math.Abs(did.GetHashCode()).ToString("X8");
                    if (deviceHash.Length > 8) deviceHash = deviceHash.Substring(0, 8);
                    if (deviceHash.Length < 8) deviceHash = deviceHash.PadLeft(8, '0');
                    var uniqueUUID = $"12345678-1234-5678-1234-56789abc{deviceHash.ToLower()}";
                    _logger.LogInformation($"基于DID生成唯一SERVICE_UUID: {uniqueUUID}");
                    return uniqueUUID;
                }
                
                // 方式3：使用默认的统一UUID（所有NCBEdge设备相同）
                var defaultUUID = "12345678-1234-5678-1234-56789abcdef0";
                _logger.LogInformation($"使用默认SERVICE_UUID: {defaultUUID}");
                return defaultUUID;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取SERVICE_UUID时发生错误，使用默认UUID");
                return "12345678-1234-5678-1234-56789abcdef0";
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("经典蓝牙RFCOMM服务端启动中...");

            try
            {
                // 初始化蓝牙适配器
                await InitializeBluetoothAdapterAsync();
                
                // 配置蓝牙服务
                await ConfigureBluetoothServiceAsync();

                // 移除未连接的蓝牙设备
                Task.Run(async () => {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        await RemoveNoConnectDevicesAsync();
                        await Task.Delay(300000);
                    }
                });
                
                // 🔴 新增：定期检查蓝牙可发现性（防止外部因素修改状态）
                Task.Run(async () => {
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(60000, stoppingToken); // 每60秒检查一次
                            
                            var checkResult = await ExecuteCommandAsync("bluetoothctl show | grep 'Discoverable: yes'");
                            if (!checkResult.Success || string.IsNullOrEmpty(checkResult.Output))
                            {
                                _logger.LogWarning("⚠️ 检测到蓝牙不可发现，正在自动恢复...");
                                
                                await ExecuteCommandAsync("sudo rfkill unblock bluetooth");
                                await Task.Delay(300);
                                await ExecuteCommandAsync("echo 'power on' | bluetoothctl");
                                await Task.Delay(300);
                                await ExecuteCommandAsync("echo 'discoverable on' | bluetoothctl");
                                await Task.Delay(300);
                                await ExecuteCommandAsync("echo 'pairable on' | bluetoothctl");
                                
                                _logger.LogInformation("✅ 蓝牙可发现性已自动恢复");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "检查蓝牙可发现性时发生错误");
                        }
                    }
                });
                
                // 启动RFCOMM监听服务
                await StartRfcommListenerAsync();
                
                // 保持服务运行，直到取消
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("经典蓝牙RFCOMM服务端已被取消");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "经典蓝牙RFCOMM服务端执行失败");
            }
            finally
            {
                await CleanupAsync();
            }
        }

        /// <summary>
        /// 初始化蓝牙适配器
        /// </summary>
        private async Task InitializeBluetoothAdapterAsync()
        {
            try
            {
                _logger.LogInformation("正在初始化蓝牙适配器...");
                
                // 启用蓝牙适配器
                var enableResult = await ExecuteCommandAsync("sudo hciconfig hci0 up");
                if (!enableResult.Success)
                {
                    throw new InvalidOperationException($"启用蓝牙适配器失败: {enableResult.Error}");
                }
                
                // 设置蓝牙适配器为可发现和可连接
                await ExecuteCommandAsync("sudo hciconfig hci0 piscan");
                
                // 获取蓝牙地址
                var addressResult = await ExecuteCommandAsync("hciconfig hci0 | grep 'BD Address' | awk '{print $3}'");
                if (addressResult.Success && !string.IsNullOrEmpty(addressResult.Output))
                {
                    _bluetoothAddress = addressResult.Output.Trim();
                }
                
                // 设置蓝牙名称
                await ExecuteCommandAsync($"sudo bluetoothctl system-alias '{_bluetoothName}'");
                await ExecuteCommandAsync($"sudo hciconfig hci0 name '{_bluetoothName}'");
                //await ExecuteCommandAsync($"sudo btmgmt -i hci0 name \"{_bluetoothName}\"");

                //关闭广告（防止修改时冲突）
                //await ExecuteCommandAsync($"sudo btmgmt advertising off");
                //添加高频广播（interval min=20ms, max=20ms）
                //await ExecuteCommandAsync($"sudo btmgmt add-adv -i0x0020 -g0x0020 -t0 -c0x02");
                //await ExecuteCommandAsync($"sudo btmgmt add-adv -i0x0020 -g0x0020 -t0 -c0x07"); //20ms
                //await ExecuteCommandAsync($"sudo btmgmt add-adv -i0x0050 -g0x0050 -t0 -c0x07"); //50ms
                //await ExecuteCommandAsync($"sudo btmgmt add-adv -i0x00A0 -g0x00A0 -t0 -c0x07"); //100ms


                // 🔴 启用 BLE 广播（异步后台任务，不阻塞主流程）
                _logger.LogInformation("启动 BLE 广播后台任务...");
                
                _ = Task.Run(async () => {
                    try
                    {
                        // 等待蓝牙完全初始化
                        _logger.LogInformation("BLE 广播后台任务：等待蓝牙初始化...");
                        await Task.Delay(5000);
                        
                        _logger.LogInformation("BLE 广播后台任务：执行广播命令...");
                        
                        // 使用超时机制执行广播命令
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        var advTask = ExecuteCommandAsync($"sudo btmgmt advertising on");
                        
                        if (await Task.WhenAny(advTask, Task.Delay(15000, cts.Token)) == advTask)
                        {
                            var advResult = await advTask;
                            if (advResult.Success)
                            {
                                _logger.LogInformation($"✅ BLE 广播启用成功: {advResult.Output}");
                            }
                            else
                            {
                                _logger.LogWarning($"⚠️ BLE 广播启用失败: {advResult.Error}");
                            }
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ BLE 广播命令超时（15秒），跳过此步骤");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "BLE 广播后台任务失败（不影响RFCOMM功能）");
                    }
                });
                
                _logger.LogInformation("BLE 广播后台任务已启动（异步执行，不阻塞主流程）");
                
                _logger.LogInformation($"蓝牙适配器初始化完成:");
                _logger.LogInformation($"  设备名称: {_deviceName}");
                _logger.LogInformation($"  蓝牙名称: {_bluetoothName}");
                _logger.LogInformation($"  蓝牙地址: {_bluetoothAddress ?? "未知"}");
                _logger.LogInformation($"  RFCOMM通道: {RFCOMM_CHANNEL}");
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化蓝牙适配器失败");
                throw;
            }
        }


        /// <summary>
        /// 配置蓝牙服务
        /// </summary>
        private async Task ConfigureBluetoothServiceAsync()
        {
            try
            {
                _logger.LogInformation("正在配置蓝牙SDP服务...");
                
                // 创建SDP服务记录文件（使用带时间戳的文件名，避免权限冲突）
                var sdpRecord = CreateSdpServiceRecord();
                var sdpFilePath = $"/tmp/neuchar_sdp_record_{DateTime.Now.Ticks}.xml";
                await File.WriteAllTextAsync(sdpFilePath, sdpRecord);
                
                // 注册SDP服务 - 使用更兼容的方式
                var sdpResult = await ExecuteCommandAsync($"which sdptool");
                if (sdpResult.Success)
                {
                    _logger.LogInformation($"sdptool路径: {sdpResult.Output}");
                    
                    // 检查蓝牙守护进程状态
                    await CheckBluetoothServiceStatusAsync();
                    
                    // 尝试注册SDP服务
                    bool sdpSuccess = await TryRegisterSdpServiceAsync();
                    
                    if (!sdpSuccess)
                    {
                        _logger.LogWarning("SDP服务注册失败，但RFCOMM通信仍可正常工作");
                        _logger.LogInformation("影响：客户端需要手动指定通道号连接，无法通过服务发现自动连接");
                        _logger.LogInformation("客户端连接方式：直接连接到设备地址的通道1");
                        
                        // 尝试备选方案
                        await TryAlternativeSdpRegistrationAsync();
                    }
                }
                else
                {
                    _logger.LogWarning("sdptool工具未找到，跳过SDP服务注册");
                    _logger.LogInformation("提示：可通过 'sudo apt-get install bluez-tools' 安装sdptool");
                    _logger.LogInformation("或者：可通过 'sudo apt-get install bluez bluez-hcidump' 安装完整蓝牙工具包");
                }
                
                // 设置免配对（如果需要）
                await SetupPairingModeAsync();
                
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, $"配置蓝牙服务失败（权限问题）: {ex.Message}");
                _logger.LogWarning("SDP 服务配置失败，但 RFCOMM 监听仍会继续启动");
                _logger.LogInformation("提示：可能是临时文件权限问题，请执行: sudo rm -f /tmp/neuchar_sdp_record*.xml");
                // 权限问题不应导致整个服务崩溃，继续运行
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"配置蓝牙服务失败: {ex.Message}");
                _logger.LogWarning("SDP 服务配置失败，但 RFCOMM 监听仍会继续启动");
                // 其他异常也不应导致整个服务崩溃，客户端可以通过指定通道号连接
            }
        }

        /// <summary>
        /// 设置配对模式
        /// </summary>
        private async Task SetupPairingModeAsync()
        {
            try
            {
                _logger.LogInformation("配置蓝牙配对模式...");
                
                // 检查bluetoothctl是否可用
                var bluetoothctlCheck = await ExecuteCommandAsync("which bluetoothctl");
                if (bluetoothctlCheck.Success)
                {
                    _logger.LogInformation($"bluetoothctl路径: {bluetoothctlCheck.Output}");

                    //🔴 注释掉：初始化时重启蓝牙会导致可发现性丢失
                    // await ExecuteCommandAsync("sudo systemctl restart bluetooth");
                    // await Task.Delay(1000);

                    // 再次设置蓝牙名称
                    await ExecuteCommandAsync($"sudo bluetoothctl system-alias {_bluetoothName}");
                    await Task.Delay(1000);

                    // 启动持续的蓝牙代理进程，保持agent活跃
                    //await StartBluetoothAgentAsync();

                    //移除所有蓝牙设备
                    await RemoveAllDevicesAsync();
                    
                    // 设置蓝牙为可发现和可配对模式
                    var discoveryCommands = new[]
                    {
                        "echo 'discoverable-timeout 0' | bluetoothctl",
                        "echo 'discoverable on' | bluetoothctl",
                        "echo 'pairable on' | bluetoothctl"
                    };
                    
                    _logger.LogInformation("设置蓝牙可发现和可配对模式...");
                    foreach (var cmd in discoveryCommands)
                    {
                        var result = await ExecuteCommandAsync(cmd);
                        if (result.Success)
                        {
                            _logger.LogDebug($"命令执行成功: {cmd}");
                        }
                        else
                        {
                            _logger.LogWarning($"命令执行失败: {cmd} - {result.Error}");
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("bluetoothctl工具未找到，跳过配对模式配置");
                    _logger.LogInformation("提示：可通过 'sudo apt-get install bluez' 安装bluetoothctl");
                }
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置配对模式失败");
            }
        }

        /// <summary>
        /// 启动持续的蓝牙代理进程
        /// </summary>
        private async Task StartBluetoothAgentAsync()
        {
            try
            {
                _logger.LogInformation("启动持续的蓝牙代理进程...");
                
                // 如果之前的代理进程还在运行，先停止它
                if (_bluetoothAgentProcess != null && !_bluetoothAgentProcess.HasExited)
                {
                    _bluetoothAgentProcess.Kill();
                    _bluetoothAgentProcess.Dispose();
                }

                // 创建bluetoothctl进程，保持运行状态
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"bluetoothctl\"",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // 设置环境变量
                startInfo.Environment["PATH"] = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";
                startInfo.Environment["HOME"] = Environment.GetEnvironmentVariable("HOME") ?? "/root";
                
                _bluetoothAgentProcess = new Process { StartInfo = startInfo };
                _bluetoothAgentProcess.OutputDataReceived += OnBluetoothAgentOutput;
                _bluetoothAgentProcess.ErrorDataReceived += OnBluetoothAgentError;
                
                _bluetoothAgentProcess.Start();
                _bluetoothAgentProcess.BeginOutputReadLine();
                _bluetoothAgentProcess.BeginErrorReadLine();

                // 等待一会让bluetoothctl启动完成
                await Task.Delay(1000);

                // 发送代理配置命令
                var commands = new[]
                {
                    "agent off",
                    "agent NoInputNoOutput",
                    "default-agent"
                };

                foreach (var command in commands)
                {
                    _logger.LogDebug($"发送bluetoothctl命令: {command}");
                    await _bluetoothAgentProcess.StandardInput.WriteLineAsync(command);
                    await _bluetoothAgentProcess.StandardInput.FlushAsync();
                    
                    // 短暂等待命令执行
                    await Task.Delay(500);
                }

                _logger.LogInformation("蓝牙代理进程已启动并配置完成，将保持运行状态");
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动蓝牙代理进程失败");
            }
        }

        /// <summary>
        /// 处理蓝牙代理进程输出
        /// </summary>
        private void OnBluetoothAgentOutput(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _logger.LogDebug($"BluetoothAgent输出: {e.Data}");
            }
        }

        /// <summary>
        /// 处理蓝牙代理进程错误
        /// </summary>
        private void OnBluetoothAgentError(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _logger.LogWarning($"BluetoothAgent错误: {e.Data}");
            }
        }

        /// <summary>
        /// 启动RFCOMM监听服务
        /// </summary>
        private async Task StartRfcommListenerAsync()
        {
            try
            {
                _logger.LogInformation($"启动RFCOMM监听服务，通道: {RFCOMM_CHANNEL}");
                
                // 清理可能存在的设备绑定
                await ExecuteCommandAsync($"sudo rfcomm release {RFCOMM_CHANNEL}");
                
                // 优先使用rfcomm listen方式（真正的监听）
                await StartRfcommListenAsync();
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动RFCOMM监听服务失败");
                throw;
            }
        }
        

        
        /// <summary>
        /// 使用原生C#蓝牙套接字实现（真正的蓝牙服务端）
        /// </summary>
        private async Task StartRfcommListenAsync()
        {
            try
            {
                _logger.LogInformation("启动原生C#蓝牙RFCOMM服务端...");
                
                // 启动一个后台任务来运行原生蓝牙服务端
                _ = Task.Run(async () => await RunNativeBluetoothServerAsync());
                
                _logger.LogInformation($"原生蓝牙RFCOMM服务端已启动，监听通道: {RFCOMM_CHANNEL}");
                _logger.LogInformation($"等待客户端蓝牙连接到设备: {_bluetoothName}");
                _logger.LogInformation($"服务UUID: {SERVICE_UUID}");
                
                _isRunning = true;
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动原生蓝牙服务端失败");
                throw;
            }
        }

        /// <summary>
        /// 运行原生蓝牙服务端主循环（真正的C#蓝牙实现）
        /// </summary>
        private async Task RunNativeBluetoothServerAsync()
        {
            int serverSocket = -1;
            
            try
            {
                // 只创建一次服务端socket，持续监听
                serverSocket = await CreatePersistentServerSocketAsync();
                
            while (_isRunning)
            {
                try
                {
                        _logger.LogInformation("等待新的客户端连接...");
                        
                        // 接受客户端连接
                        var clientSocket = await AcceptClientConnectionAsync(serverSocket);
                        if (clientSocket >= 0)
                        {
                            _logger.LogInformation($"[重连调试] 开始处理客户端socket: {clientSocket}");
                            
                                                    // 处理客户端通信
                        await HandleNativeBluetoothClientAsync(clientSocket);
                        _logger.LogInformation("[重连调试] 客户端通信处理完成，准备等待新的连接...");
                        
                        // 确保客户端socket已被清理
                        try
                        {
                            BluetoothSyscalls.close(clientSocket);
                            _logger.LogDebug($"[重连调试] 确保客户端socket已关闭: {clientSocket}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, $"[重连调试] 关闭客户端socket时出错: {clientSocket}");
                        }
                        }
                        else
                        {
                            _logger.LogWarning("[重连调试] accept返回无效的客户端socket");
                        }
                        
                        // 移除断开的蓝牙设备记录
                    //await RemoveAllDevicesAsync();

                        await Task.Delay(1000); // 短暂等待后继续监听
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("原生蓝牙服务端被取消");
                    break;
                }
                catch (Exception ex)
                {
                        _logger.LogError(ex, "处理客户端连接时发生错误，继续监听...");
                    await Task.Delay(2000);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建服务端socket失败");
            }
            finally
            {
                // 清理服务端socket
                if (serverSocket >= 0)
                {
                    try
                    {
                        BluetoothSyscalls.close(serverSocket);
                        _logger.LogInformation("服务端socket已关闭");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "关闭服务端socket失败");
                    }
                }
            }
        }

        private async Task RemoveAllDevicesAsync()
        {
            var devicesResult = await ExecuteCommandAsync("echo 'devices' | bluetoothctl");
            if (devicesResult.Success && !string.IsNullOrEmpty(devicesResult.Output))
            {
                await RemoveDevicesAsync(devicesResult.Output);
            }
        }

        /// <summary>
        /// 解析并更新设备列表（优化版本 - 只对新设备或长时间未更新的设备进行详细检测）
        /// </summary>
        private async Task RemoveDevicesAsync(string output)
        {
            try
            {
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var discoveredAddresses = new HashSet<string>();
                
                DateTime now=DateTime.Now;
                foreach (var line in lines)
                {
                    // 解析格式: Device AA:BB:CC:DD:EE:FF Device Name
                    var match = Regex.Match(line.Trim(), @"Device\s+([A-Fa-f0-9:]{17})\s+(.+)");
                    if (match.Success)
                    {
                        var deviceAddress = match.Groups[1].Value;
                        var deviceName = match.Groups[2].Value;

                        await ExecuteCommandAsync($"echo 'remove {deviceAddress}' | bluetoothctl");
                        _logger.LogInformation($"移除蓝牙设备: {deviceAddress}，{deviceName}");

                        await ExecuteCommandAsync($"sudo rm -rf /var/lib/bluetooth/{_bluetoothAddress}/{deviceAddress}");
                        _logger.LogInformation($"移除蓝牙设备文件: {deviceAddress}");

                        await ExecuteCommandAsync($"sudo rm -rf /var/lib/bluetooth/{_bluetoothAddress}/cache/*");
                        _logger.LogInformation($"移除蓝牙设备缓存: {deviceAddress}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解析蓝牙设备列表失败");
            }
        }


        private async Task RemoveNoConnectDevicesAsync()
        {
            try
            {
                var devicesResult = await ExecuteCommandAsync("echo 'devices' | bluetoothctl");
                if (devicesResult.Success && !string.IsNullOrEmpty(devicesResult.Output))
                {
                    await RemoveNoConnectDeviceAsync(devicesResult.Output);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "移除未连接的蓝牙设备失败");
            }
        }

        private async Task RemoveNoConnectDeviceAsync(string output)
        {
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var match = Regex.Match(line.Trim(), @"Device\s+([A-Fa-f0-9:]{17})\s+(.+)");
                if (match.Success){
                    var deviceAddress = match.Groups[1].Value;
                    var devicesResult = await ExecuteCommandAsync($"echo 'info {deviceAddress}' | bluetoothctl");
                    if (devicesResult.Success && !string.IsNullOrEmpty(devicesResult.Output))
                    {
                        if(!devicesResult.Output.Trim().ToLower().Contains("connected: yes"))
                        {
                            await ExecuteCommandAsync($"echo 'remove {deviceAddress}' | bluetoothctl");
                            _logger.LogInformation($"移除未连接的蓝牙设备: {deviceAddress}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 创建持久的服务端socket（只创建一次，持续监听）
        /// </summary>
        private async Task<int> CreatePersistentServerSocketAsync()
        {
            int serverSocket = -1;
            
            try
            {
                // 先清理可能存在的RFCOMM绑定
                await CleanupExistingRfcommBindingsAsync();
                
                _logger.LogInformation("创建持久的蓝牙RFCOMM服务端套接字...");
                
                // 1. 创建蓝牙RFCOMM套接字
                serverSocket = BluetoothSyscalls.socket(BluetoothSyscalls.AF_BLUETOOTH, BluetoothSyscalls.SOCK_STREAM, BluetoothSyscalls.BTPROTO_RFCOMM);
                if (serverSocket < 0)
                {
                    var errorMsg = GetLastError();
                    throw new InvalidOperationException($"创建蓝牙套接字失败: {errorMsg}");
                }
                
                _logger.LogInformation($"蓝牙套接字创建成功，套接字描述符: {serverSocket}");
                
                // 2. 设置套接字选项（允许地址重用和端口重用）
                int optval = 1;
                var reuseAddrResult = BluetoothSyscalls.setsockopt(serverSocket, BluetoothSyscalls.SOL_SOCKET, BluetoothSyscalls.SO_REUSEADDR, ref optval, sizeof(int));
                if (reuseAddrResult < 0)
                {
                    _logger.LogWarning($"设置SO_REUSEADDR失败: {GetLastError()}");
                }
                
                // 3. 绑定到本地蓝牙地址和RFCOMM通道
                var localAddr = new BluetoothSyscalls.sockaddr_rc
                {
                    rc_family = BluetoothSyscalls.AF_BLUETOOTH,
                    rc_bdaddr = new BluetoothSyscalls.bdaddr_t(new byte[6]), // BDADDR_ANY
                    rc_channel = (byte)RFCOMM_CHANNEL
                };
                
                var bindResult = BluetoothSyscalls.bind(serverSocket, ref localAddr, Marshal.SizeOf<BluetoothSyscalls.sockaddr_rc>());
                if (bindResult < 0)
                {
                    var errorMsg = GetLastError();
                    if (errorMsg.Contains("Address already in use"))
                    {
                        _logger.LogWarning($"RFCOMM通道 {RFCOMM_CHANNEL} 被占用，尝试强力清理...");
                        await ForceCleanupRfcommChannelAsync();
                        await Task.Delay(2000);
                        
                        bindResult = BluetoothSyscalls.bind(serverSocket, ref localAddr, Marshal.SizeOf<BluetoothSyscalls.sockaddr_rc>());
                        if (bindResult < 0)
                        {
                            var retryErrorMsg = GetLastError();
                            throw new InvalidOperationException($"重试后仍然绑定失败: {retryErrorMsg}");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"绑定蓝牙套接字失败: {errorMsg}");
                    }
                }
                
                _logger.LogInformation($"蓝牙套接字绑定成功，通道: {RFCOMM_CHANNEL}");
                
                // 4. 开始监听连接
                var listenResult = BluetoothSyscalls.listen(serverSocket, 5); // 增加队列长度支持多个连接
                if (listenResult < 0)
                {
                    var errorMsg = GetLastError();
                    throw new InvalidOperationException($"监听蓝牙连接失败: {errorMsg}");
                }
                
                _logger.LogInformation("持久蓝牙服务端socket创建成功，开始监听连接...");
                return serverSocket;
            }
            catch (Exception ex)
            {
                if (serverSocket >= 0)
                {
                    BluetoothSyscalls.close(serverSocket);
                }
                _logger.LogError(ex, "创建持久服务端socket失败");
                throw;
            }
        }
        
        /// <summary>
        /// 接受客户端连接
        /// </summary>
        private async Task<int> AcceptClientConnectionAsync(int serverSocket)
        {
            try
            {
                _logger.LogInformation("等待客户端连接...");
                
                var clientAddr = new BluetoothSyscalls.sockaddr_rc();
                int clientAddrLen = Marshal.SizeOf<BluetoothSyscalls.sockaddr_rc>();
                
                // 在后台线程中执行阻塞的accept调用
                var clientSocket = await Task.Run(() => 
                {
                    return BluetoothSyscalls.accept(serverSocket, ref clientAddr, ref clientAddrLen);
                });
                
                if (clientSocket < 0)
                {
                    var errorMsg = GetLastError();
                    _logger.LogError($"接受蓝牙连接失败: {errorMsg}");
                    return -1;
                }
                
                // 提取客户端蓝牙地址
                var clientBluetoothAddr = FormatBluetoothAddress(clientAddr.rc_bdaddr.b);
                _logger.LogInformation($"蓝牙客户端已连接，地址: {clientBluetoothAddr}，通道: {clientAddr.rc_channel}");
                
                return clientSocket;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "接受客户端连接时发生错误");
                return -1;
            }
        }

        /// <summary>
        /// 清理可能存在的RFCOMM绑定
        /// </summary>
        private async Task CleanupExistingRfcommBindingsAsync()
        {
            try
            {
                _logger.LogDebug("清理现有的RFCOMM绑定...");
                
                // 查看当前RFCOMM状态
                var rfcommStatus = await ExecuteCommandAsync("sudo rfcomm -a 2>/dev/null || true");
                if (!string.IsNullOrEmpty(rfcommStatus.Output))
                {
                    _logger.LogInformation($"当前RFCOMM状态: {rfcommStatus.Output}");
                }
                
                // 释放可能存在的RFCOMM设备绑定
                await ExecuteCommandAsync($"sudo rfcomm release {RFCOMM_CHANNEL} 2>/dev/null || true");
                
                // 检查并终止可能在使用该通道的进程
                var lsofCheck = await ExecuteCommandAsync($"sudo lsof -i:{RFCOMM_CHANNEL} 2>/dev/null || true");
                if (!string.IsNullOrEmpty(lsofCheck.Output))
                {
                    _logger.LogWarning($"检测到进程正在使用通道 {RFCOMM_CHANNEL}: {lsofCheck.Output}");
                }
                
                // 等待一下让系统完成清理
                await Task.Delay(500);
                
                _logger.LogDebug("RFCOMM绑定清理完成");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理RFCOMM绑定时发生错误，但将继续尝试启动服务");
            }
        }

        /// <summary>
        /// 强制清理RFCOMM通道
        /// </summary>
        private async Task ForceCleanupRfcommChannelAsync()
        {
            try
            {
                _logger.LogInformation("开始强制清理RFCOMM通道...");
                
                // 1. 强制释放RFCOMM设备
                await ExecuteCommandAsync($"sudo rfcomm release {RFCOMM_CHANNEL}");
                
                // 2. 查找并终止使用该通道的进程
                var netstatResult = await ExecuteCommandAsync($"sudo netstat -ap | grep :{RFCOMM_CHANNEL} || true");
                if (!string.IsNullOrEmpty(netstatResult.Output))
                {
                    _logger.LogWarning($"发现使用通道 {RFCOMM_CHANNEL} 的进程: {netstatResult.Output}");
                }
                
                // 3. 查找蓝牙相关进程
                var bluetoothProcs = await ExecuteCommandAsync("pgrep -f 'rfcomm|bluetooth' || true");
                if (!string.IsNullOrEmpty(bluetoothProcs.Output))
                {
                    _logger.LogDebug($"当前蓝牙相关进程: {bluetoothProcs.Output}");
                }
                
                // 4. 重启蓝牙服务（谨慎操作）
                _logger.LogWarning("尝试重启蓝牙服务以清理资源...");
                await ExecuteCommandAsync("sudo systemctl restart bluetooth");
                
                // 5. 等待蓝牙服务重新启动
                await Task.Delay(3000);
                
                // 6. 重新初始化蓝牙适配器
                await ExecuteCommandAsync("sudo hciconfig hci0 up");
                await ExecuteCommandAsync("sudo hciconfig hci0 piscan");
                await ExecuteCommandAsync($"sudo hciconfig hci0 name '{_bluetoothName}'");
                
                // 🔴 7. 恢复可发现性（关键！防止重启后不可被发现）
                _logger.LogInformation("恢复蓝牙可发现性...");
                await ExecuteCommandAsync("sudo rfkill unblock bluetooth");
                await Task.Delay(500);
                
                // 使用 bluetoothctl 恢复状态
                await ExecuteCommandAsync("echo 'power on' | bluetoothctl");
                await Task.Delay(300);
                await ExecuteCommandAsync("echo 'discoverable-timeout 0' | bluetoothctl");
                await Task.Delay(300);
                await ExecuteCommandAsync("echo 'discoverable on' | bluetoothctl");
                await Task.Delay(300);
                await ExecuteCommandAsync("echo 'pairable on' | bluetoothctl");
                await Task.Delay(300);
                
                // 验证状态
                var verifyResult = await ExecuteCommandAsync("bluetoothctl show | grep -E 'Powered|Discoverable'");
                _logger.LogInformation($"蓝牙状态验证: {verifyResult.Output}");
                
                _logger.LogInformation("强制清理完成，已恢复蓝牙可发现性");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "强制清理RFCOMM通道时发生错误");
            }
        }

        /// <summary>
        /// 获取最后的系统错误信息
        /// </summary>
        private string GetLastError()
        {
            try
            {
                var errorCode = BluetoothSyscalls.errno();
                if (errorCode == -1)
                {
                    return "Cannot access errno";
                }
                if (errorCode == 0) 
                {
                    return "Success";
                }
                
                var errorPtr = BluetoothSyscalls.strerror(errorCode);
                if (errorPtr == IntPtr.Zero)
                {
                    return $"Error {errorCode} (no description available)";
                }
                
                return Marshal.PtrToStringAnsi(errorPtr) ?? $"Error {errorCode}";
            }
            catch (AccessViolationException)
            {
                return "Connection reset by peer (Access violation while reading error)";
            }
            catch
            {
                return "Unknown error";
            }
        }

        /// <summary>
        /// 格式化蓝牙地址为字符串
        /// </summary>
        private string FormatBluetoothAddress(byte[] address)
        {
            if (address == null || address.Length != 6)
                return "Unknown";
                
            return $"{address[5]:X2}:{address[4]:X2}:{address[3]:X2}:{address[2]:X2}:{address[1]:X2}:{address[0]:X2}";
        }

        /// <summary>
        /// 处理原生蓝牙客户端通信（真正的套接字数据传输）
        /// </summary>
        private async Task HandleNativeBluetoothClientAsync(int clientSocket)
        {
            var clientId = $"Native_Client_{DateTime.Now.Ticks % 10000}";
            
            try
            {
                _logger.LogInformation("开始处理原生蓝牙客户端通信...");
                
                // 创建客户端连接记录
                var clientConnection = new BluetoothClientConnection
                {
                    Id = clientId,
                    RemoteEndpoint = clientId,
                    ConnectedTime = DateTime.Now,
                    LastActivityTime = DateTime.Now
                };
                
                _connectedClients.TryAdd(clientId, clientConnection);
                
                // 不发送欢迎消息，等待客户端主动发送数据（模仿Python脚本的行为）
                _logger.LogInformation("等待客户端发送数据...");
                
                // 主通信循环 - 简化版本
                var lastSentMessage = ""; // 追踪最后发送的消息，避免回声循环
                
                _logger.LogInformation($"[蓝牙通信] 开始监听客户端数据... ClientSocket: {clientSocket}");

                while (_isRunning)
                {
                    try
                    {
                        // 尝试简化版本的数据接收
                        var (simpleReceiveResult, sentMessage) = await TrySimpleReceiveAsync(clientSocket, lastSentMessage);
                        if (simpleReceiveResult == true)
                        {
                            // 成功接收并处理了数据
                            if (!string.IsNullOrEmpty(sentMessage))
                            {
                                lastSentMessage = sentMessage;
                            }
                            continue;
                        }
                        else if (simpleReceiveResult == false)
                        {
                            // 连接已断开，退出循环
                            _logger.LogInformation("[连接状态] 简化接收检测到连接断开，退出通信循环");
                            break;
                        }
                        // simpleReceiveResult == null 表示没有数据但连接正常
                        
                        // 如果简化接收没有数据，短暂等待后继续
                        await Task.Delay(100);
                        continue;
                        
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "处理客户端通信时发生错误");
                        
                        // 检查是否为连接断开
                        if (ex.Message.Contains("Connection reset") || 
                            ex.Message.Contains("Broken pipe") ||
                            ex.Message.Contains("Connection refused"))
                        {
                            _logger.LogInformation("客户端断开连接");
                            break;
                        }

                        // 其他错误等待一下继续尝试
                        await Task.Delay(500);
                    }
                }
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理原生蓝牙客户端连接时发生错误");
            }
            finally
            {
                // 清理客户端连接记录（socket在外层已经关闭）
                _connectedClients.TryRemove(clientId, out _);
                _logger.LogInformation($"[重连调试] 已清理原生蓝牙客户端连接记录: {clientId}");
            }
        }

        /// <summary>
        /// 简化版本的数据接收（更健壮）
        /// 返回值：(true, sentMessage)=成功接收数据，(false, null)=连接断开，(null, null)=无数据但连接正常
        /// </summary>
        private async Task<(bool?, string)> TrySimpleReceiveAsync(int clientSocket, string lastSentMessage)
        {
            try
            {
                var buffer = new byte[1024];
                var allData = new List<byte>();
                
                // 使用超时机制的非阻塞读取
                var startTime = DateTime.Now;
                const int timeoutMs = 100; // 100ms超时
                
                while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
                {
                    var bytesReceived = BluetoothSyscalls.recv(clientSocket, buffer, buffer.Length, 0);
                    
                    if (bytesReceived > 0)
                    {
                        allData.AddRange(buffer.Take(bytesReceived));
                        var rawData = Encoding.UTF8.GetString(buffer, 0, bytesReceived);
                        _logger.LogInformation($"[简化接收] 收到 {bytesReceived} 字节数据: {rawData}");
                        
                        // 检查是否收到完整消息（以换行符结尾）
                        var currentData = Encoding.UTF8.GetString(allData.ToArray());
                        if (currentData.EndsWith("\n") || currentData.EndsWith("\r\n"))
                        {
                            var sentMessage = await ProcessSimpleReceivedData(clientSocket, currentData.TrimEnd('\r', '\n'), lastSentMessage);
                            return (true, sentMessage);
                        }
                    }
                    else if (bytesReceived == 0)
                    {
                        _logger.LogInformation("[简化接收] 客户端关闭连接，退出简化接收");
                        return (false, null); // 返回false表示连接已断开
                    }
                    else
                    {
                        // 检查是否是真正的错误
                        var errorCode = BluetoothSyscalls.errno();
                        if (errorCode == 11 || errorCode == 35) // EAGAIN = 11, EWOULDBLOCK = 35
                        {
                            // 没有数据可读，短暂等待
                            await Task.Delay(10);
                        }
                        else if (errorCode == 104 || errorCode == 32) // ECONNRESET = 104, EPIPE = 32
                        {
                            _logger.LogInformation($"[简化接收] 连接被重置，错误码: {errorCode}");
                            return (false, null); // 连接断开
                        }
                        else
                        {
                            _logger.LogInformation($"[简化接收] 接收错误，错误码: {errorCode}");
                            return (false, null); // 返回false表示连接有问题
                        }
                    }
                }
                
                // 如果有数据但没有换行符，也尝试处理
                if (allData.Count > 0)
                {
                    var currentData = Encoding.UTF8.GetString(allData.ToArray());
                    var sentMessage = await ProcessSimpleReceivedData(clientSocket, currentData, lastSentMessage);
                    return (true, sentMessage);
                }
                
                return (null, null); // 没有接收到数据，但连接正常
            }
            catch (AccessViolationException ex)
            {
                _logger.LogError(ex, "[简化接收] 内存访问违规，连接可能已断开");
                return (false, null);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[简化接收] 简化接收失败");
                return (null, null);
            }
        }
        
        /// <summary>
        /// 处理简化接收的数据
        /// </summary>
        private async Task<string> ProcessSimpleReceivedData(int clientSocket, string rawData, string lastSentMessage)
        {
            try
            {
                _logger.LogInformation($"[简化处理] 原始数据: {rawData}");
                
                string processedMessage = rawData;
                
                // 尝试Base64解码
                try
                {
                    var decodedBytes = Convert.FromBase64String(rawData);
                    processedMessage = Encoding.UTF8.GetString(decodedBytes);
                    _logger.LogInformation($"[简化处理] Base64解码成功: {processedMessage}");
                }
                catch (FormatException)
                {
                    _logger.LogInformation($"[简化处理] 非Base64数据，直接使用: {rawData}");
                }
                
                // 避免回声
                if (processedMessage.Equals(lastSentMessage))
                {
                    _logger.LogInformation($"[简化处理] 检测到回声消息，忽略: {processedMessage}");
                    return null;
                }
                
                // 处理消息并发送回复
                var response = await ProcessReceivedMessage(processedMessage);
                if (!string.IsNullOrEmpty(response))
                {
                    _logger.LogInformation($"[简化处理] 发送回复: {response}");
                    await SendNativeBluetoothMessage(clientSocket, response + "\n");
                    return response; // 返回发送的消息，用于回声检测
                }
                
                return null; // 没有发送回复
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[简化处理] 处理简化数据时发生错误");
                return null;
            }
        }

        /// <summary>
        /// 处理收到的消息并生成回复
        /// </summary>
        private async Task<string> ProcessReceivedMessage(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message)){
                    return null;
                }
                if(message.StartsWith("{")&& message.EndsWith("}")){
                    string msgId="";
                    DateTime msgTime=DateTime.MinValue;
                    int msgType=0;
                    string msgData="";
                    try{
                        BluetoothMsg bluetoothMsg = JsonConvert.DeserializeObject<BluetoothMsg>(message);
                        msgId=bluetoothMsg.MsgId;
                        msgTime=bluetoothMsg.Time;
                        msgType=bluetoothMsg.Type;
                        if(bluetoothMsg.Type==10000) // 获取设备ID
                        {
                            msgData=_senderReceiverSet.dId;
                        }
                        else if(bluetoothMsg.Type==10050)// 连接WiFi网络
                        {
                            Console.WriteLine($"蓝牙消息类型: {bluetoothMsg.Type}");
                            Console.WriteLine($"蓝牙消息数据: {bluetoothMsg.Data}");
                           var jsonMsg= CertHepler.RsaDecryptWithPrivateKey( bluetoothMsg.Data);
                           Console.WriteLine($"蓝牙消息解密后数据: {jsonMsg}");
                           var wifiConfigMsg=JsonConvert.DeserializeObject<WifiConfigMsg>(jsonMsg);

                            // 验证参数
                            if (string.IsNullOrWhiteSpace(wifiConfigMsg.SSID))
                            {
                                _logger.LogWarning("WiFi SSID为空，跳过WiFi连接");
                                throw new NcfExceptionBase("WiFi SSID为空");
                            }

                            if (string.IsNullOrWhiteSpace(wifiConfigMsg.NCBIP))
                            {
                                throw new NcfExceptionBase("NCBIP Is Empty");
                            }

                            _logger.LogInformation($"[蓝牙配网] 收到WiFi配网请求: SSID={wifiConfigMsg.SSID}, NCBIP={wifiConfigMsg.NCBIP}");
                            
                            // 🔴 使用统一的 WifiManagerService 连接 WiFi
                            var (connectSuccess, connectMessage) = await _wifiManagerService.ConnectToWifiAsync(
                                wifiConfigMsg.SSID, 
                                wifiConfigMsg.Password, 
                                wifiConfigMsg.NCBIP);
                            
                            if (!connectSuccess)
                            {
                                throw new NcfExceptionBase(connectMessage);
                            }

                            msgData="SUCCESS";
                        }
                        else{
                            throw new NcfExceptionBase($"The message type is not supported: {bluetoothMsg.Type}");
                        }
                        return JsonConvert.SerializeObject(new BluetoothMsgRsp(){
                            MsgId = msgId,
                            Time = msgTime,
                            Type = msgType,
                            Success = true,
                            Message = "Success",
                            Data = msgData,
                            Sign = CertHepler.RsaEncryptWithPrivateKey(msgData)
                        });
                    }
                    catch(NcfExceptionBase ex){
                        _logger.LogError(ex, "处理消息时发生错误");
                        return JsonConvert.SerializeObject(new BluetoothMsgRsp(){
                            MsgId = msgId,
                            Time = msgTime,
                            Type = msgType,
                            Success = false,
                            Message = ex.Message,
                        });
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogError(ex, "处理消息时发生错误");
                        return JsonConvert.SerializeObject(new BluetoothMsgRsp()
                        {
                            MsgId = msgId,
                            Time = msgTime,
                            Type = msgType,
                            Success = false,
                            Message = "Error Happened，Encrypt Failed",
                        });
                    }
                    catch(Exception ex){
                        _logger.LogError(ex, "处理消息时发生错误");
                        return JsonConvert.SerializeObject(new BluetoothMsgRsp(){
                            MsgId = msgId,
                            Time = msgTime,
                            Type = msgType,
                            Success = false,
                            Message = "Error Happened",
                        });
                    }
                }
                var upperMessage = message.ToUpper().Trim();
                // PING-PONG 响应
                if (upperMessage == "PING")
                {
                    return "PONG";
                }
                // 状态查询
                if (upperMessage == "STATUS")
                {
                    return $"OK - NCBEdge_{_bluetoothName} Online";
                }
                // 时间查询
                if (upperMessage == "TIME")
                {
                    return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                }
                // 设备信息查询
                if (upperMessage == "INFO" || upperMessage == "DEVICE_INFO")
                {
                    return $"Device: {_bluetoothName}, Address: {_bluetoothAddress}, Channel: {RFCOMM_CHANNEL}";
                }
                // 帮助信息
                if (upperMessage == "HELP")
                {
                    return "Commands: PING, STATUS, TIME, INFO, HELP";
                }
                // 默认回显响应
                return $"Echo: {message}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理消息时发生错误");
                return "Error processing message";
            }
        }

        /// <summary>
        /// 通过原生蓝牙套接字发送消息
        /// </summary>
        private async Task SendNativeBluetoothMessage(int clientSocket, string message)
        {
            try
            {
                var messageBytes = Encoding.UTF8.GetBytes(message);
                
                // 添加发送前的延迟，避免发送过快导致缓冲区满
                await Task.Delay(50);
                
                var bytesSent = BluetoothSyscalls.send(clientSocket, messageBytes, messageBytes.Length, 0);
                
                if (bytesSent < 0)
                {
                    var errorMsg = GetLastError();
                    
                    // 检查是否是资源暂时不可用的错误
                    if (errorMsg.Contains("Resource temporarily unavailable") || 
                        errorMsg.Contains("would block"))
                    {
                        _logger.LogWarning($"发送缓冲区满，延迟后重试发送: {message.Trim()}");
                        
                        // 等待更长时间后重试
                        await Task.Delay(500);
                        bytesSent = BluetoothSyscalls.send(clientSocket, messageBytes, messageBytes.Length, 0);
                        
                        if (bytesSent < 0)
                        {
                            var retryErrorMsg = GetLastError();
                            throw new InvalidOperationException($"重试后仍发送失败: {retryErrorMsg}");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"发送消息失败: {errorMsg}");
                    }
                }
                
                if (bytesSent != messageBytes.Length)
                {
                    _logger.LogWarning($"消息未完整发送，期望: {messageBytes.Length} 字节，实际: {bytesSent} 字节");
                }
                
                _logger.LogDebug($"成功发送消息: {message.Length} 字符，{bytesSent} 字节");
                
                // 发送后延迟，给客户端处理时间
                await Task.Delay(10);
            }
            catch (AccessViolationException ex)
            {
                _logger.LogError(ex, "发送消息时内存访问违规，连接可能已断开");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "通过原生蓝牙套接字发送消息失败");
                throw;
            }
        }


        /// <summary>
        /// 检查蓝牙服务状态
        /// </summary>
        private async Task CheckBluetoothServiceStatusAsync()
        {
            try
            {
                // 检查蓝牙服务状态
                var bluetoothStatus = await ExecuteCommandAsync("systemctl is-active bluetooth");
                _logger.LogInformation($"蓝牙服务状态: {bluetoothStatus.Output}");
                
                // 检查bluetoothd进程
                var bluetoothdStatus = await ExecuteCommandAsync("pgrep bluetoothd");
                if (bluetoothdStatus.Success)
                {
                    _logger.LogInformation($"bluetoothd进程运行中: PID {bluetoothdStatus.Output}");
                }
                else
                {
                    _logger.LogWarning("bluetoothd进程未运行，这可能是SDP注册失败的原因");
                }
                
                // 检查D-Bus服务
                var dbusStatus = await ExecuteCommandAsync("systemctl is-active dbus");
                _logger.LogInformation($"D-Bus服务状态: {dbusStatus.Output}");
                
                // 检查当前用户和组
                var whoami = await ExecuteCommandAsync("whoami");
                var groups = await ExecuteCommandAsync("groups");
                _logger.LogInformation($"当前用户: {whoami.Output}, 用户组: {groups.Output}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查蓝牙服务状态失败");
            }
        }

        /// <summary>
        /// 尝试注册SDP服务
        /// </summary>
        private async Task<bool> TryRegisterSdpServiceAsync()
        {
            try
            {
                // 尝试不同的sdptool命令方式
                var commands = new[]
                {
                    $"sudo sdptool add --channel={RFCOMM_CHANNEL} SP",
                    $"sudo $(which sdptool) add --channel={RFCOMM_CHANNEL} SP", 
                    $"sudo /usr/bin/sdptool add --channel={RFCOMM_CHANNEL} SP",
                    $"sdptool add --channel={RFCOMM_CHANNEL} SP"
                };
                
                foreach (var cmd in commands)
                {
                    _logger.LogInformation($"尝试执行: {cmd}");
                    var addResult = await ExecuteCommandAsync(cmd);
                    
                    if (addResult.Success)
                    {
                        _logger.LogInformation("SDP服务注册成功");
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning($"命令失败 (退出码: {addResult.ExitCode}): {cmd}");
                        if (!string.IsNullOrEmpty(addResult.Error))
                            _logger.LogWarning($"错误输出: {addResult.Error}");
                        if (!string.IsNullOrEmpty(addResult.Output))
                            _logger.LogWarning($"标准输出: {addResult.Output}");
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注册SDP服务时发生异常");
                return false;
            }
        }

        /// <summary>
        /// 尝试备选的SDP注册方案
        /// </summary>
        private async Task TryAlternativeSdpRegistrationAsync()
        {
            try
            {
                _logger.LogInformation("尝试备选SDP注册方案...");
                
                // 方案1：使用bluetoothctl设置可发现性和服务信息
                var bluetoothctlCommands = new[]
                {
                    "power on",
                    "discoverable on", 
                    "pairable on",
                    $"advertise on"
                };
                
                foreach (var cmd in bluetoothctlCommands)
                {
                    var result = await ExecuteCommandAsync($"echo '{cmd}' | bluetoothctl");
                    if (result.Success)
                    {
                        _logger.LogDebug($"bluetoothctl命令执行成功: {cmd}");
                    }
                }
                
                // 方案2：直接写入SDP记录文件（如果支持）
                await TryWriteSdpRecordFileAsync();
                
                _logger.LogInformation("备选SDP注册方案完成");
                _logger.LogInformation("注意：客户端连接时请直接使用通道1，或扫描可用服务");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行备选SDP注册方案失败");
            }
        }

        /// <summary>
        /// 尝试写入SDP记录文件
        /// </summary>
        private async Task TryWriteSdpRecordFileAsync()
        {
            try
            {
                var sdpRecord = CreateSdpServiceRecord();
                var sdpFilePath = "/tmp/neuchar_sdp_record.xml";
                
                await File.WriteAllTextAsync(sdpFilePath, sdpRecord);
                _logger.LogInformation($"SDP记录文件已创建: {sdpFilePath}");
                
                // 尝试使用其他方式加载SDP记录
                var loadCommands = new[]
                {
                    $"sudo sdptool add --file={sdpFilePath}",
                    $"sudo hciconfig hci0 class 0x1f00" // 设置设备类型为通用计算机
                };
                
                foreach (var cmd in loadCommands)
                {
                    var result = await ExecuteCommandAsync(cmd);
                    if (result.Success)
                    {
                        _logger.LogInformation($"备选SDP命令执行成功: {cmd}");
                    }
                    else
                    {
                        _logger.LogDebug($"备选SDP命令失败: {cmd}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "写入SDP记录文件失败");
            }
        }

        /// <summary>
        /// 创建SDP服务记录
        /// </summary>
        private string CreateSdpServiceRecord()
        {
            return $@"<?xml version=""1.0"" encoding=""UTF-8"" ?>
<record>
    <attribute id=""0x0001"">
        <sequence>
            <uuid value=""{SERVICE_UUID}"" />
        </sequence>
    </attribute>
    <attribute id=""0x0004"">
        <sequence>
            <sequence>
                <uuid value=""0x0100"" />
            </sequence>
            <sequence>
                <uuid value=""0x0003"" />
                <uint8 value=""{RFCOMM_CHANNEL}"" />
            </sequence>
        </sequence>
    </attribute>
    <attribute id=""0x0100"">
        <text value=""{SERVICE_NAME}"" />
    </attribute>
</record>";
        }


        /// <summary>
        /// 执行系统命令
        /// </summary>
        private async Task<CommandResult> ExecuteCommandAsync(string command)
        {
            try
            {
                _logger.LogDebug($"执行命令: {command}");
                
                var processInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{command}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                // 设置环境变量，确保PATH正确
                processInfo.Environment["PATH"] = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";
                processInfo.Environment["HOME"] = Environment.GetEnvironmentVariable("HOME") ?? "/root";
                processInfo.UseShellExecute = false;

                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    return new CommandResult { Success = false, Error = "无法启动进程" };
                }
                
                await process.WaitForExitAsync();
                
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                
                var result = new CommandResult
                {
                    Success = process.ExitCode == 0,
                    Output = output?.Trim(),
                    Error = error?.Trim(),
                    ExitCode = process.ExitCode
                };
                
                if (result.Success)
                {
                    if (!string.IsNullOrEmpty(result.Output))
                    {
                        _logger.LogDebug($"命令执行成功: {result.Output}");
                    }
                }
                else
                {
                    _logger.LogWarning($"命令执行失败 (退出码: {result.ExitCode}): {result.Error}");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"执行命令失败: {command}");
                return new CommandResult { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        private async Task CleanupAsync()
        {
            try
            {
                _isRunning = false;
                
                _logger.LogInformation("正在清理蓝牙服务端资源...");
                
                // 停止RFCOMM进程
                if (_rfcommListenerProcess != null && !_rfcommListenerProcess.HasExited)
                {
                    try
                    {
                        _rfcommListenerProcess.Kill();
                        await _rfcommListenerProcess.WaitForExitAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "停止RFCOMM进程失败");
                    }
                    finally
                    {
                        _rfcommListenerProcess?.Dispose();
                    }
                }
                
                // 停止蓝牙代理进程
                if (_bluetoothAgentProcess != null && !_bluetoothAgentProcess.HasExited)
                {
                    try
                    {
                        _bluetoothAgentProcess.Kill();
                        await _bluetoothAgentProcess.WaitForExitAsync();
                        _logger.LogInformation("蓝牙代理进程已停止");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "停止蓝牙代理进程失败");
                    }
                    finally
                    {
                        _bluetoothAgentProcess?.Dispose();
                    }
                }
                
                // 释放RFCOMM设备绑定
                await ExecuteCommandAsync($"sudo rfcomm release {RFCOMM_CHANNEL}");
                
                // 清理命名管道
                if (File.Exists(_namedPipePath))
                {
                    File.Delete(_namedPipePath);
                }
                
                // 清理客户端连接
                _connectedClients.Clear();
                
                // 注销SDP服务（如果sdptool可用）
                var sdpToolCheck = await ExecuteCommandAsync("which sdptool");
                if (sdpToolCheck.Success)
                {
                    await ExecuteCommandAsync("sudo sdptool del SP");
                }
                
                _logger.LogInformation("蓝牙服务端资源清理完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理资源失败");
            }
        }

        public override void Dispose()
        {
            _isRunning = false;
            _rfcommListenerProcess?.Kill();
            _rfcommListenerProcess?.Dispose();
            _bluetoothAgentProcess?.Kill();
            _bluetoothAgentProcess?.Dispose();
            base.Dispose();
        }
    }

    /// <summary>
    /// 蓝牙客户端连接信息
    /// </summary>
    public class BluetoothClientConnection
    {
        public string Id { get; set; }
        public string RemoteEndpoint { get; set; }
        public DateTime ConnectedTime { get; set; }
        public DateTime LastActivityTime { get; set; }
    }

    /// <summary>
    /// 命令执行结果
    /// </summary>
    public class CommandResult
    {
        public bool Success { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }
        public int ExitCode { get; set; }
    }
} 