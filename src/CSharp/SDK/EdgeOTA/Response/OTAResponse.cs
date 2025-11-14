using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EdgeOTA.Response
{
    public class OTAResponse
    {
        public string Id { get; set; }
        public string DevicePoolId { get; set; }
        public string OperatingSystem { get; set; }
        public string DevelopmentMode { get; set; }
        public string OtaMode { get; set; }
        public string FirmwareType { get; set; }
        public string FirmwareVersion { get; set; }
        public string FirmwarePackage { get; set; }
        public decimal FirmwareSize { get; set; }
        public string UpdateDescription { get; set; }
    }
}
