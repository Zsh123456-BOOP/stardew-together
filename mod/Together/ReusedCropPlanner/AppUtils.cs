// Source: mschult2/stardew-planner @ 724c4ab5732f83a8e39d6d99650c07dc5a3f8f62
// Reused pure calculator; live game inputs and feasibility constraints are provided by Together.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StardewCropCalculatorLibrary
{
    /// <summary>
    /// Static utility functions for this Stardrew application.
    /// </summary>
    static class AppUtils
    {
        // Our application's fail policy
        public static void Fail(string errorMsg)
        {
            System.Diagnostics.Debug.WriteLine(errorMsg);
            throw new Exception(errorMsg);
        }
    }
}

