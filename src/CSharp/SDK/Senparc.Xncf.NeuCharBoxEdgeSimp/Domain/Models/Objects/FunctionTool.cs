using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Senparc.Xncf.NeuCharBoxEdgeSimp.Domain.Models.Objects
{

    #region tools

    public class Tool
    {
        public string type { get; set; }
        public string returnDescription { get; set; }
        public Tool_Function function { get; set; } = new Tool_Function();
    }

    public class Tool_Function
    {
        public string name { get; set; }
        public string description { get; set; }
        public Parameters parameters { get; set; }
        public bool strict { get; set; }
    }

    public class Parameters
    {
        public string type { get; set; }
        public Dictionary<string, Properties> properties { get; set; }
        public string[] required { get; set; }
        public bool additionalProperties { get; set; }
    }

    public class Properties
    {
        public string type { get; set; }
        public string description { get; set; }

        [JsonPropertyName("enum")]
        public string[]? Enum { get; set; }
    }



    #endregion
}
