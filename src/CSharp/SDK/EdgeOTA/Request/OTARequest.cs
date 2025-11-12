using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EdgeOTA.Request
{
    /// <summary>
    /// OTA配置信息类
    /// </summary>
    public class OTARequest
    {
        /// <summary>
        /// 应用Key
        /// </summary>
        public string AppKey { get; set; } = string.Empty;

        /// <summary>
        /// 应用密钥
        /// </summary>
        public string AppSecret { get; set; } = string.Empty;

        /// <summary>
        /// 用户ID
        /// </summary>
        public string UID { get; set; } = string.Empty;

        /// <summary>
        /// 设备ID
        /// </summary>
        public string DID { get; set; } = string.Empty;

        /// <summary>
        /// 固件类型
        /// </summary>
        public string FirmwareType { get; set; } = string.Empty;
    }

    public class GetRemoteVersionInfoRequest:OTARequest{

    }

    public class CheckForUpdateRequest{
        /// <summary>
        /// 用户ID
        /// </summary>
        public string UID { get; set; } = string.Empty;

        /// <summary>
        /// 设备ID
        /// </summary>
        public string DID { get; set; } = string.Empty;

        /// <summary>
        /// 固件类型
        /// </summary>
        public string FirmwareType { get; set; } = string.Empty;
    }

    public class DownloadUpdateRequest:CheckForUpdateRequest{
        /// <summary>
        /// 基础URL
        /// </summary>
        public string BaseUrl { get; set; } = string.Empty;
    }

}
