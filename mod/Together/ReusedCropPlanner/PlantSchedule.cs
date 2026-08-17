// Source: mschult2/stardew-planner @ 724c4ab5732f83a8e39d6d99650c07dc5a3f8f62
// Reused pure calculator; live game inputs and feasibility constraints are provided by Together.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StardewCropCalculatorLibrary
{
    /// <summary>
    /// Daily planting schedule for the given time period.
    /// </summary>
    public class PlantSchedule
    {
        // The crops planted on a given day. Index represents the day, from day 1 to day n. 0 index is unused.
        // Technically there can be multiple crops planted on a given day, but the max-profit algorithm (cf: MaxTotalWealth()) will likely never decide to do so.
        List<Crop>[] plantingSchedule;
        int maxDays;

        public int MaxDays
        {
            get { return maxDays; }
        }

        public PlantSchedule(int maxDays)
        {
            this.maxDays = maxDays;
            plantingSchedule = new List<Crop>[maxDays + 1];

            // Empty-list-init all elements
            for (int day = 1; day <= maxDays; ++day)
            {
                plantingSchedule[day] = new List<Crop>();
            }
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        public PlantSchedule(PlantSchedule otherSchedule)
        {
            this.maxDays = otherSchedule.maxDays;
            plantingSchedule = new List<Crop>[this.maxDays + 1];

            for (int day = 1; day <= this.maxDays; ++day)
            {
                this.plantingSchedule[day] = new List<Crop>(otherSchedule.GetCrops(day));
            }
        }

        /// <summary>
        /// Returns a copy of the crop list for that day
        /// </summary>
        /// <param name="day"></param>
        /// <returns></returns>
        public List<Crop> GetCrops(int day)
        {
            return new List<Crop>(plantingSchedule[day]);
        }

        /// <summary>
        /// Returns the first crop listed on a particular day (there should generally only be one, anyway)
        /// </summary>
        /// <param name="day"></param>
        /// <returns>Null if no crop on this day</returns>
        public Crop GetCrop(int day)
        {
            var crops = plantingSchedule[day];

            if (crops == null || crops.Count == 0)
                return null;
            else
                return crops.First();
        }

        /// <summary>
        /// Add crop to the list of crops for this day.
        /// </summary>
        public void AddCrop(int day, Crop crop)
        {
            if (plantingSchedule[day] == null)
                plantingSchedule[day] = new List<Crop>();

            plantingSchedule[day].Add(crop);
        }

        /// <summary>
        /// Set crop as the only crop for this day.
        /// </summary>
        /// <param name="day"></param>
        /// <param name="crop"></param>
        public void SetCrop(int day, Crop crop)
        {
            plantingSchedule[day] = new List<Crop>();
            plantingSchedule[day].Add(crop);
        }

        /// <summary>
        /// Add the input schedule to this schedule.
        /// </summary>
        /// <param name="otherSchedule"></param>
        public void Merge(PlantSchedule otherSchedule)
        {
            if (otherSchedule.maxDays != this.maxDays)
            {
                AppUtils.Fail("PlantingSchedule.Merge(otherSchedule) - otherSchedule and this schedule must have same number of days! Operation failed");
            }

            for (int day = 1; day <= otherSchedule.maxDays; ++day)
            {
                var otherDayCrops = otherSchedule.GetCrops(day);

                foreach (var crop in otherDayCrops)
                {
                    this.AddCrop(day, crop);
                }
            }
        }

        public void Clear()
        {
            plantingSchedule = new List<Crop>[maxDays + 1];

            // Empty-list-init all elements
            for (int day = 1; day <= maxDays; ++day)
                plantingSchedule[day] = new List<Crop>();
        }

        /// <summary>
        /// Print the crops planted on each day.
        /// </summary>
        override public string ToString() 
        {
            StringBuilder sb = new StringBuilder();

            for (int day = 1; day < plantingSchedule.Length; ++day)
            {
                sb.AppendLine("Day " + day + ": ");

                // Should generally only be one distinct item.
                var distinctCrops = plantingSchedule[day].Distinct();

                if (distinctCrops.Count() > 1)
                {
                    sb.AppendLine("WARNING! The algorithm is suggesting you buy multiple crops, which is unusual.");

                    foreach (Crop crop in distinctCrops)
                        sb.Append(crop.name + ", ");
                }
                else if (distinctCrops.Count() == 1)
                {
                    sb.Append(distinctCrops.First().name);
                }

                if (plantingSchedule[day].Count != 0) sb.AppendLine();
                sb.AppendLine("--");
            }

            string scheduleStr = sb.ToString();
            Debug.WriteLine(scheduleStr);

            return scheduleStr;
        }
    }
}

