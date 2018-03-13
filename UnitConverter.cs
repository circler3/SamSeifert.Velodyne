namespace SamSeifert.Velodyne
{
    internal class UnitConverter
    {
        public static float DegreesToRadians(float src)
        {
            return (float)(src * System.Math.PI / 180);
        }
    }
}