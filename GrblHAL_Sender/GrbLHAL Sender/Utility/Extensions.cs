using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrbLHAL_Sender.Utility
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
    }
}
