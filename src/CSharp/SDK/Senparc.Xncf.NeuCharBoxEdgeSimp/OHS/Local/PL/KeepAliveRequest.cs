using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Senparc.Xncf.NeuCharBoxEdgeSimp.OHS.Local.PL
{
    public class KeepAliveRequest
    {
        [Required(ErrorMessage = "缺少DID")]
        public string did { get; set; }
        [Required(ErrorMessage = "缺少UID")]
        public string uid { get; set; }
        [Required(ErrorMessage = "缺少deciveName")]
        public string deciveName { get; set; }
        public string version { get; set; }
    }
}
