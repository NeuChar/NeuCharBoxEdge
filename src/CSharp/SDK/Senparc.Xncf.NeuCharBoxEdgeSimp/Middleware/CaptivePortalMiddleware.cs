using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Services;
using System.Threading.Tasks;

namespace Senparc.Xncf.NeuCharBoxEdgeSimp.Middleware
{
    /// <summary>
    /// Captive Portal 中间件 - 自动重定向到配网页面
    /// </summary>
    public class CaptivePortalMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<CaptivePortalMiddleware> _logger;
        
        // 配网页面路径（简化路由）
        private const string PROVISION_PATH = "/provision";
        
        // 不需要重定向的路径（小写，用于比较）
        private static readonly string[] ExcludedPaths = new[]
        {
            "/provision",        // 配网页面（不区分大小写）
            "/admin/",           // Admin区域的其他页面
            "/api/",
            "/lib/",
            "/css/",
            "/js/",
            "/images/",
            "/favicon.ico",
            "/ncbui/",
            "/_framework/",      // Blazor框架文件
            "/_vs/",             // Visual Studio
            "/swagger"           // Swagger文档
        };

        public CaptivePortalMiddleware(RequestDelegate next, ILogger<CaptivePortalMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // 只有在热点激活时才进行重定向检查
            if (!WifiManagerService.IsHotspotActive)
            {
                await _next(context);
                return;
            }

            var path = context.Request.Path.Value ?? "";
            var pathLower = path.ToLower();
            
            // 如果是排除的路径，直接放行
            if (IsExcludedPath(pathLower))
            {
                await _next(context);
                return;
            }
            
            // 检查是否是热点模式访问（通过 Host 判断）
            var host = context.Request.Host.Host;
            
            // 检测常见的 Captive Portal 检测URL 或 热点IP访问
            // 注意：NetworkManager 默认热点网关IP是 10.42.0.1
            var isHotspotAccess = host == "10.42.0.1" ||         // NetworkManager 默认热点IP
                                  host == "192.168.42.1" ||       // 可能的自定义热点IP
                                  host == "localhost" ||
                                  host == "connectivitycheck.gstatic.com" || 
                                  host == "captive.apple.com" ||
                                  host.Contains("captive") || 
                                  host == "www.msftconnecttest.com" ||
                                  host == "detectportal.firefox.com" ||
                                  host == "clients3.google.com" ||
                                  host == "www.google.com";
            
            // 如果是热点访问，重定向到配网页面
            if (isHotspotAccess)
            {
                _logger.LogInformation($"[CaptivePortal] 检测到热点访问，重定向: {path} -> {PROVISION_PATH}");
                context.Response.Redirect(PROVISION_PATH);
                return;
            }
            
            await _next(context);
        }

        private bool IsExcludedPath(string pathLower)
        {
            foreach (var excludedPath in ExcludedPaths)
            {
                if (pathLower.StartsWith(excludedPath))
                {
                    return true;
                }
            }
            return false;
        }
    }
}

