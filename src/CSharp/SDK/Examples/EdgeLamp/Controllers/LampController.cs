using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ModelContextProtocol.Server;
using Senparc.CO2NET.WebApi;
using Senparc.CO2NET;
using Senparc.Ncf.Core.AppServices;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Attributes;
using System.ComponentModel;
using EdgeLamp.Services;

namespace EdgeLamp.Controllers;

/// <summary>
/// 灯控制接口
/// </summary>
[McpServerToolType]
public class LampController : AppServiceBase
{
    //private readonly LampService _lampService;
    private readonly ILogger<LampController> _logger;

    public LampController(
        IServiceProvider serviceProvider,
        //LampService lampService,
        ILogger<LampController> logger)
        : base(serviceProvider)
    {
        //_lampService = lampService;
        _logger = logger;
    }

    /// <summary>
    /// 控制灯闪烁（异步，不等待）
    /// </summary>
    /// <param name="request">闪烁参数</param>
    /// <returns>操作结果</returns>
    [FunctionRender("控制灯闪烁（异步，不等待完成）", "异步控制灯闪烁", typeof(Register))]
    [ApiBind(ApiRequestMethod = ApiRequestMethod.Post)]
    [ProducesResponseType(typeof(AppResponseBase<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AppResponseBase<string>), StatusCodes.Status400BadRequest)]
    public async Task<AppResponseBase<string>> BlinkAsync([FromBody] LampBlinkRequest request)
    {
        return await this.GetResponseAsync<string>(async (response, logger) =>
        {
            try
            {
                _logger.LogInformation("收到灯控制请求（异步）");

                if (request == null)
                {
                    throw new ArgumentNullException(nameof(request));
                }

                _logger.LogInformation($"请求参数: 次数={request.Times}, 间隔={request.Interval}秒");

                var _lampService = ServiceProvider.GetRequiredService<LampService>();
                // 异步调用StartBlinkingAsync，不等待灯闪烁完成
                _lampService.StartBlinkingAsync(request.Times, request.Interval);

                _logger.LogInformation("灯闪烁指令已发送");
                return "灯闪烁指令已发送";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "灯控制失败");
                throw new Exception($"灯控制失败: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 控制灯闪烁（异步，不等待）
    /// </summary>
    /// <returns>操作结果</returns>
    [McpServerTool, Description("控制灯闪烁（异步，不等待完成）")]
    public async Task<AppResponseBase<string>> BlinkAsync([Description("闪烁次数,传入-1则一直闪")] int Times, [Description("闪烁间隔（秒）")] double Interval)
    {
        return await this.GetResponseAsync<string>(async (response, logger) =>
        {
            try
            {
                _logger.LogInformation("收到灯控制请求（异步）");

                var _lampService = ServiceProvider.GetRequiredService<LampService>();
                // 异步调用StartBlinkingAsync，不等待灯闪烁完成
                _lampService.StartBlinkingAsync(Times, Interval);

                _logger.LogInformation("灯闪烁指令已发送");
                return "灯闪烁指令已发送";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "灯控制失败");
                throw new Exception($"灯控制失败: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 停止灯闪烁
    /// </summary>
    [McpServerTool, Description("停止灯闪烁")]
    [FunctionRender("停止灯闪烁", "停止灯闪烁", typeof(Register))]
    [ApiBind(ApiRequestMethod = ApiRequestMethod.Post)]
    [ProducesResponseType(typeof(AppResponseBase<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AppResponseBase<string>), StatusCodes.Status400BadRequest)]
    public async Task<AppResponseBase<string>> StopBlinking()
    {
        return await this.GetResponseAsync<string>(async (response, logger) =>
        {
            try
            {
                var _lampService = ServiceProvider.GetRequiredService<LampService>();
                _lampService.StopBlinking();
                return "LED停止闪烁";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止LED闪烁失败");
                throw new Exception($"停止LED闪烁失败: {ex.Message}");
            }
        });
    }


    /// <summary>
    /// 获取当前灯的运行状态
    /// </summary>
    [EdgeDataPush("获取当前灯的运行状态，返回Y表示正在运行，N表示未运行")]
    [McpServerTool, Description("获取当前灯的运行状态，返回Y表示正在运行，N表示未运行")]
    [FunctionRender("获取当前灯的运行状态，返回Y表示正在运行，N表示未运行", "获取LED灯的当前运行状态，Y表示正在运行，N表示未运行", typeof(Register))]
    [ApiBind(ApiRequestMethod = ApiRequestMethod.Post)]
    [ProducesResponseType(typeof(AppResponseBase<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AppResponseBase<string>), StatusCodes.Status400BadRequest)]
    public async Task<AppResponseBase<string>> LampStatus()
    {
        return await this.GetResponseAsync<string>(async (response, logger) =>
        {
            try
            {
                //_logger.LogInformation("获取灯状态请求");
                var _lampService = ServiceProvider.GetRequiredService<LampService>();
                bool isRunning = _lampService.IsRunning;
                string result = isRunning ? "Y" : "N";
                //_logger.LogInformation($"灯状态: {(isRunning ? "运行中" : "未运行")}");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取灯状态失败");
                throw new Exception($"获取灯状态失败: {ex.Message}");
            }
        });
    }

}

/// <summary>
/// 灯闪烁请求参数
/// </summary>
public class LampBlinkRequest
{
    /// <summary>
    /// 闪烁次数
    /// </summary>
    [Description("闪烁次数,传入-1则一直闪")]
    public int Times { get; set; }

    /// <summary>
    /// 闪烁间隔（秒）
    /// </summary>
    [Description("闪烁间隔（秒）")]
    public double Interval { get; set; }
}