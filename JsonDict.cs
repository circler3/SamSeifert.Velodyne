using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SamSeifert.Velodyne
{
    public class JsonDict
    {
        public JsonDict()
        {
            Set = new Dictionary<string, object>();
        }

        public Dictionary<string,object> Set;
        public object this[string index]
        {
            get => Set[index];
            set => Set[index] = value;
        }

        
    }
}
