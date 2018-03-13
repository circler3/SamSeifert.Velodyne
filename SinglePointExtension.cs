using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SamSeifert.Velodyne.VLP_16;

namespace SamSeifert.Velodyne
{
    public static class SinglePointExtension
    {
        public static SinglePoint[] SubArray(this SinglePoint[] points, int start, int length)
        {
            SinglePoint[] p = new SinglePoint[length];
            Array.Copy(points, start, p, 0, length);
            return p;
        }
    }
}
