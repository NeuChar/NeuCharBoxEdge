using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EdgeOTA.Entity
{
    public class OTAConfig
    {
        public string FirmwareType { get; set; }
        public string DID { get; set; }
        public string UID { get; set; }
        /// <summary>
        /// 当前版本
        /// </summary>
        public string CurrentVersion { get; set; }
        /// <summary>
        /// 远程版本
        /// </summary>
        public string RemoteVersion { get; set; }
        /// <summary>
        /// 远程文件路径
        /// </summary>
        public string RemoteFilePath { get; set; }
        /// <summary>
        /// 忽略版本
        /// </summary>
        public string IgnoreVersion { get; set; }
        /// <summary>
        /// 文件路径
        /// </summary>
        public string FilePath { get; set; }
    }
}
