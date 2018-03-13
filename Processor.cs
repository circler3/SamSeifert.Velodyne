using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace SamSeifert.Velodyne
{
    public class Processor
    {
        FileStream fs;
        public Processor()
        {
            fs = new FileStream("d:\\12.obj", FileMode.Create);
        }
        public static void PacketRecieved(VLP_16.Packet p, IPEndPoint ip)
        {
            var result = p.Pack();
            foreach (var n in result["Blocks"] as Array)
            {

                //System.Diagnostics.Debug.WriteLine(n.ToString());
            }
        }
    }
}
