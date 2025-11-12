using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EdgeOTA.Entity
{
    /// <summary>
    /// 管理下属边缘设备版本信息
    /// </summary>
    public class OTAEdgeConfig
    {
        public string FirmwareType { get; set; }
        public string DID { get; set; }
        public string UID { get; set; }
        /// <summary>
        /// 远程版本
        /// </summary>
        public string RemoteVersion { get; set; }
        /// <summary>
        /// 远程文件路径
        /// </summary>
        public string RemoteFilePath { get; set; }
    }
}
