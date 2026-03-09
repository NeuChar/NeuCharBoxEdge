using Microsoft.AspNetCore.Mvc;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.BackgroundServices;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Services;
using Senparc.Xncf.NeuCharBoxEdgeSimp.OHS.Local.PL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Senparc.Xncf.NeuCharBoxEdgeSimp.Areas.Admin.Controllers
{
    /// <summary>
    /// 热点配网 API Controller
    /// </summary>
    [Area("Admin")]
    [Route("api/[area]/[controller]")]
    [ApiController]
    public class ProvisionController : ControllerBase
    {
        private readonly WifiManagerService _wifiManagerService;

        public ProvisionController(WifiManagerService wifiManagerService)
        {
            _wifiManagerService = wifiManagerService;
        }

        /// <summary>
        /// 获取可用的 WiFi 网络列表
        /// </summary>
        /// <returns></returns>
        [HttpGet("networks")]
        public async Task<IActionResult> GetNetworks()
        {
            try
            {
                // 检查 WiFi 是否启用
                if (!WifiBackgroundService.IsWifiEnabled)
                {
                    return Ok(new { success = false, errorMessage = "WiFi功能未启用" });
                }

                // 获取所有可用网络
                var networks = WifiBackgroundService.GetAllAvailableNetworks();
                
                var networkDtos = networks.Select(n => new WifiNetworkDto
                {
                    SSID = n.SSID,
                    Signal = n.Signal,
                    Security = n.Security,
                    Frequency = n.Frequency
                }).ToList();

                return Ok(new { success = true, data = networkDtos });
            }
            catch (System.Exception ex)
            {
                return Ok(new { success = false, errorMessage = $"获取WiFi列表失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 连接到指定的 WiFi 网络
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("connect")]
        public async Task<IActionResult> Connect([FromBody] ConnectWifiRequest request)
        {
            try
            {
                // 参数验证
                if (string.IsNullOrWhiteSpace(request.SSID))
                {
                    return Ok(new { success = false, errorMessage = "SSID不能为空" });
                }

                if (string.IsNullOrWhiteSpace(request.NCBIP))
                {
                    return Ok(new { success = false, errorMessage = "NCBIP不能为空" });
                }

                // 🔥 关键改进：先返回响应，再异步执行网络切换
                // 这样可以确保客户端收到响应，即使后续网络会断开
                
                // 在后台线程执行WiFi连接（延迟2秒，确保响应已发送）
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // 延迟2秒，确保HTTP响应已经发送完成
                        await Task.Delay(2000);
                        
                        // 调用 WiFi 管理服务连接 WiFi
                        var (success, message) = await _wifiManagerService.ConnectToWifiAsync(
                            request.SSID, 
                            request.Password, 
                            request.NCBIP);
                            
                        // 这里的结果无法返回给客户端，但会记录在日志中
                        if (!success)
                        {
                            Console.WriteLine($"❌ WiFi连接失败: {message}");
                        }
                        else
                        {
                            Console.WriteLine($"✅ WiFi连接成功，热点已关闭");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Console.WriteLine($"❌ 后台WiFi连接异常: {ex.Message}");
                    }
                });

                // 立即返回成功响应（此时WiFi还未真正切换）
                return Ok(new 
                { 
                    success = true, 
                    data = "配网指令已接收，设备将切换网络",
                    message = "请稍候重新连接到您的主WiFi网络"
                });
            }
            catch (System.Exception ex)
            {
                return Ok(new { success = false, errorMessage = $"连接WiFi失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 获取热点状态
        /// </summary>
        /// <returns></returns>
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            try
            {
                var status = new HotspotStatusDto
                {
                    IsActive = WifiManagerService.IsHotspotActive,
                    SSID = WifiManagerService.HotspotSSID,
                    Password = WifiManagerService.IsHotspotActive ? "12345678" : null,
                    ConfigUrl = WifiManagerService.IsHotspotActive ? "http://10.42.0.1:5000/provision" : null
                };

                return Ok(new { success = true, data = status });
            }
            catch (System.Exception ex)
            {
                return Ok(new { success = false, errorMessage = $"获取热点状态失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 手动启动热点
        /// </summary>
        /// <returns></returns>
        [HttpPost("start")]
        public async Task<IActionResult> Start()
        {
            try
            {
                var (success, message) = await _wifiManagerService.StartHotspotAsync();
                
                if (success)
                {
                    return Ok(new { success = true, data = message });
                }
                else
                {
                    return Ok(new { success = false, errorMessage = message });
                }
            }
            catch (System.Exception ex)
            {
                return Ok(new { success = false, errorMessage = $"启动热点失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 手动停止热点
        /// </summary>
        /// <returns></returns>
        [HttpPost("stop")]
        public async Task<IActionResult> Stop()
        {
            try
            {
                var (success, message) = await _wifiManagerService.StopHotspotAsync();
                
                if (success)
                {
                    return Ok(new { success = true, data = message });
                }
                else
                {
                    return Ok(new { success = false, errorMessage = message });
                }
            }
            catch (System.Exception ex)
            {
                return Ok(new { success = false, errorMessage = $"停止热点失败: {ex.Message}" });
            }
        }
    }
}

