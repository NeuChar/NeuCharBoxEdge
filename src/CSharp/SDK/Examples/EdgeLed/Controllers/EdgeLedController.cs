using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Senparc.CO2NET;
using Senparc.CO2NET.WebApi;
using Senparc.Ncf.Core.AppServices;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel;
using Register = Senparc.CO2NET.WebApi.Register;
using EdgeLed.Services;
using ModelContextProtocol.Server;
using static System.Runtime.InteropServices.JavaScript.JSType;
using Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Attributes;

namespace EdgeLed.Controllers
{
    /// <summary>
    /// Led控制
    /// </summary>
    [McpServerToolType]
    public class LedController : AppServiceBase
    {
        private readonly TM1637DisplayService _tM1637DisplayService;

        public LedController(IServiceProvider serviceProvider, TM1637DisplayService tM1637DisplayService) : base(serviceProvider)
        {
            _tM1637DisplayService = tM1637DisplayService;
        }


        /// <summary>
        /// 显示数字
        /// </summary>
        /// <param name="request"></param>
        /// <returns>返回显示的数字</returns>
        /// <exception cref="ArgumentException"></exception>
        [FunctionRender("数字管显示数字", "返回显示的数字", typeof(Register))]
        [ApiBind(ApiRequestMethod = ApiRequestMethod.Post)]
        public async Task<AppResponseBase<string>> DisplayNumber([FromBody] DisplayNumberRequest request)
        {
            return await this.GetResponseAsync<string>(async (response, logger) =>
            {
                try
                {
                    // 尝试将字符串转换为数字 | Try to parse string to number
                    if (float.TryParse(request.Number, out float numberValue))
                    {
                        var numberInt = (int)numberValue;

                        logger.Append($"Received request to display number: {numberInt}");
                        _tM1637DisplayService.DisplayNumber(numberInt);
                        return $"{numberInt}";
                    }
                    else
                    {
                        throw new ArgumentException("Invalid number format");
                    }
                }
                catch (Exception ex)
                {
                    response.Success = false;
                    response.ErrorMessage = ex.Message;
                    return string.Empty;
                }
            });
        }


        /// <summary>
        /// 显示数字
        /// </summary>
        /// <returns>返回显示的数字</returns>
        /// <exception cref="ArgumentException"></exception>
        [McpServerTool, Description("数字管显示数字")]
        public async Task<AppResponseBase<string>> DisplayNumber([Description("最大4位字符串，范围'-999'至'9999'")] string number)
        {
            return await this.GetResponseAsync<string>(async (response, logger) =>
            {
                try
                {
                    // 尝试将字符串转换为数字 | Try to parse string to number
                    if (float.TryParse(number, out float numberValue))
                    {
                        var numberInt = (int)numberValue;

                        logger.Append($"Received request to display number: {numberInt}");
                        _tM1637DisplayService.DisplayNumber(numberInt);
                        return $"{numberInt}";
                    }
                    else
                    {
                        throw new ArgumentException("Invalid number format");
                    }
                }
                catch (Exception ex)
                {
                    response.Success = false;
                    response.ErrorMessage = ex.Message;
                    return string.Empty;
                }
            });
        }



        /// <summary>
        /// 获取当前显示的内容
        /// </summary>
        /// <returns>返回当前显示的内容</returns>
        [EdgeDataPush("获取数字管当前显示的内容")]
        [McpServerTool, Description("获取当前数字管显示的内容")]
        [FunctionRender("获取当前数字管显示的内容", "返回当前显示的内容", typeof(Register))]
        [ApiBind(ApiRequestMethod = ApiRequestMethod.Post)]
        public async Task<AppResponseBase<string>> GetCurrentDisplay()
        {
            return await this.GetResponseAsync<string>(async (response, logger) =>
            {
                try
                {

                    logger.Append($"Received request to get current display content");
                    //var _display = ServiceProvider.GetService<TM1637DisplayService>();
                    return _tM1637DisplayService.GetCurrentDisplay();
                }
                catch (Exception ex)
                {
                    response.Success = false;
                    response.ErrorMessage = ex.Message;
                    return string.Empty;
                }
            });
        }

        /// <summary>
        /// 清空数字管显示的内容
        /// </summary>
        /// <returns>返回当前显示的内容</returns>
        [McpServerTool, Description("清空数字管显示的内容")]
        [FunctionRender("清空数字管显示的内容", "清空数字管显示的内容", typeof(Register))]
        [ApiBind(ApiRequestMethod = ApiRequestMethod.Post)]
        public async Task<AppResponseBase<string>> Clear()
        {
            return await this.GetResponseAsync<string>(async (response, logger) =>
            {
                try
                {
                    logger.Append($"Received request to clear current display content");
                    //var _display = ServiceProvider.GetService<TM1637DisplayService>();
                    _tM1637DisplayService.Clear();
                    return "ok";
                }
                catch (Exception ex)
                {
                    response.Success = false;
                    response.ErrorMessage = ex.Message;
                    return string.Empty;
                }
            });
        }



    }

    /// <summary>
    /// 
    /// </summary>
    public class DisplayNumberRequest
    {
        /// <summary>
        /// 字符串格式,最大长度为4位数字字符
        /// </summary>
        [Required]
        //[MaxLength(4)]
        [Description("最大4位字符串，范围'-999'至'9999'")]
        public string Number { get; set; }
    }

}
