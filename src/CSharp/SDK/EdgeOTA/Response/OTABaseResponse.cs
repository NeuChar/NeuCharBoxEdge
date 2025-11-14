using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EdgeOTA.Response
{
    public class OTABaseResponse<T>
    {
        public OTABaseResponse()
        {
            Success = false;
            Message = string.Empty;
            Data = default;
        }
        /// <summary>
        /// 构造函数
        /// </summary>
        public OTABaseResponse(bool success, string message)
        {
            Success = success;
            Message = message;
            Data = default;
        }
        /// <summary>
        /// 构造函数
        /// </summary>
        public OTABaseResponse(bool success, string message, T data)
        {
            Success = success;
            Message = message;
            Data = data;
        }

        
        public bool Success { get; set; }
        public string Message { get; set; }
        public T Data { get; set; }
    }
}
