namespace GrbLHALSender.Utility
{
    public static class Extensions
    {
        public static bool StringToBool(this string str)
        {
            var result = str switch
            {
                "0" => false,
                "1" => true,
                _ => false
            };
            return result;
        }

        
        public static int StringToInt(this string str)
        {
            int.TryParse(str, out int result);
            return result;
        }
        public static double StringToDouble(this string str)
        {
            double.TryParse(str, out double result);
            return result;
        }
    }
}
