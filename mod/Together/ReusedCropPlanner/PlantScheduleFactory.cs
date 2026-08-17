// Source: mschult2/stardew-planner @ 724c4ab5732f83a8e39d6d99650c07dc5a3f8f62
// Reused pure calculator; live game inputs and feasibility constraints are provided by Together.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace StardewCropCalculatorLibrary
{
    /// <summary>
    /// This scheduler algorithm is a mathematical ROI function. It's fast, but can't deal with limited tiles. It assumes all gold is invested and multipled.
    /// </summary>
    public class PlantScheduleFactory
    {
        /// <summary>
        /// The number of days to schedule.  Generally the length of a season, or 28 days.
        /// </summary>
        public readonly int numDays;

        #region MemoTable This table holds the best planting schedule, as well as additional statistics about each day. Indexed by day; 0th element is unused.
        struct MemoTable
        {
            // The most profitable crop to plant on a particular day.
            public PlantSchedule schedule;

            /// The maximum CUMULATIVE investment multiple that can be obtained from planting this crop on a particular day. (E.g., 3x means tripling you money.)
            /// Accounts for reinvestment of interest.
            public double[] cumMultiple;
        }
        #endregion

        private MemoTable memo;

        private int StartDay;

        public int PaydayDelay { get; private set; }

        public PlantScheduleFactory(int _numDays, int _startDay, int _paydayDelay)
        {
            if (_numDays < 1 || _startDay >= _numDays) AppUtils.Fail("PlantScheduleFactory(numDays) - numDays must be greater than 0");

            PaydayDelay = _paydayDelay;
            StartDay = _startDay;
            numDays = _numDays - _startDay + 1;

            // Initialize memo table
            memo.schedule = new PlantSchedule(numDays);

            memo.cumMultiple = new double[numDays + 1]; // array goes from day 0 (unused) to day numDays for ease of access
            memo.cumMultiple = memo.cumMultiple.Select(x => 1.0).ToArray(); // If you don't plant, your money multiplies by 1x (no change)
        }

        /// <summary>
        /// Calculates the best crop to plant on each day if there is no tile space limitation.
        /// Uses a dynamic programming algorithm which memoizes the best schedule by iterating through it backward.
        /// </summary>
        /// <param name="crops">The crops which are available to be purchased</param>
        /// <param name="schedule">Outputs the best schedule</param>
        /// <param name="day">The day to start planting on</param>
        /// <returns></returns>
        public double GetBestSchedule(List<Crop> crops, out PlantSchedule schedule)
        {
            crops = crops.Where(c => c.IsEnabled).ToList();

            // Check for free crop, which would result in infinite ROI.
            List<Crop> freeCrops = crops.Where(c => c.buyPrice <= 0 && c.sellPrice > 0).ToList();

            if (freeCrops.Count > 0)
            {
                freeCrops.ForEach(c => Console.WriteLine($"{c.name}: found free crop! Infinite ROI."));

                for (int day = 1; day <= numDays; ++day)
                {
                    Crop bestCrop = null;
                    int bestProfitIndex = 0;

                    foreach (var crop in freeCrops)
                    {
                        int profitIndex = crop.CurrentProfitIndex(day, numDays, PaydayDelay);

                        if (profitIndex > bestProfitIndex)
                        {
                            bestCrop = crop;
                            bestProfitIndex = profitIndex;
                        }
                    }

                    if (bestCrop != null)
                    {
                        memo.schedule.SetCrop(day, bestCrop);
                        memo.cumMultiple[day] = Double.MaxValue;
                    }
                    else
                    {
                        memo.cumMultiple[day] = 1;
                    }
                }

                // Return memo, which is now filled out with the best schedule.
                schedule = new PlantSchedule(memo.schedule);

                // Shift schedule if there's a particular start day
                if (StartDay > 1)
                    schedule = ShiftSchedule(schedule);

                return memo.cumMultiple[1];
            }

            // Find the best crop for each day, starting from end of season.
            for (int day = numDays; day > 0; --day)
            {
                foreach (var crop in crops)
                {
                    int numHarvests = crop.NumHarvests(day, numDays);
                    var cropMultiple = crop.InvestmentMultiple(day, numDays); // sell price divided by cost

                    // Base case: no harvest
                    if (crop.NumHarvests(day, numDays) < 1)
                        continue;

                    // Calculate the cumulative investment multiple of the planting.
                    double cumInvestmentMultiple = 0;
                    var harvestDay = day + crop.timeToMaturity;
                    for (int harvest = 1; harvest <= numHarvests; ++harvest)
                    {
                        var nextPlantDay = harvestDay + PaydayDelay; // Money comes in the day after a harvest
                        var nextPlantDayCumMultiple = nextPlantDay > numDays ? 1 : memo.cumMultiple[nextPlantDay];

                        cumInvestmentMultiple += (cropMultiple / numHarvests) * nextPlantDayCumMultiple;

                        harvestDay += crop.yieldRate;
                    }

                    // Update best crop for the current day.
                    if (cumInvestmentMultiple > memo.cumMultiple[day])
                    {
                        if (Double.IsPositiveInfinity(cumInvestmentMultiple))
                            Console.WriteLine($"Warning: ROI is too large of a number to be supported.");

                        memo.cumMultiple[day] = cumInvestmentMultiple;
                        memo.schedule.SetCrop(day, crop);
                    }
                }
            }

            // Return memo, which is now filled out with the best schedule.
            schedule = new PlantSchedule(memo.schedule);

            // Shift schedule if there's a particular start day
            if (StartDay > 1)
                schedule = ShiftSchedule(schedule);

            return memo.cumMultiple[1];
        }

        /// <summary>
        /// This method tells you the actual days to plant on for the last schedule computed. Based on when you get money from harvests.
        /// Returns an array of size numDays - true means you are scheduled to plant on that day.
        /// </summary>
        /// <param name="sameDayPayday">True if you get paid and replant on harvest day, false if you wait until the following day to replant your profit.</param>
        /// <param name="plantingDays">Days on which to plant a new plant batch.</param>
        /// <param name="harvestDays">Days on which a plant batch is harvested.</param>
        public void GetPlantingDays(out bool[] plantingDays, out bool[] harvestDays)
        {
            plantingDays = new bool[numDays + 1];
            harvestDays = new bool[numDays + 1];
            bool[] profitDays = new bool[numDays + 1];

            int day = 1;
            profitDays[1] = plantingDays[1] = true;

            while (day <= numDays && memo.schedule.GetCrop(day) != null)
            {
                // Skip days not previously computed to be profitable
                if (!profitDays[day])
                {
                    ++day;
                    continue;
                }

                Crop crop = memo.schedule.GetCrop(day);

                if (crop != null)
                {
                    // Make sure planting on this day gives us time to harvest
                    if (profitDays[day])
                    {
                        if (day + crop.timeToMaturity <= numDays)
                            plantingDays[day] = true;
                    }

                    // Traversal: note future profit days created by this crop
                    for (int harvestDay = day + crop.timeToMaturity; harvestDay <= numDays; harvestDay = harvestDay + crop.yieldRate)
                    {
                        harvestDays[harvestDay] = true;

                        if (harvestDay + PaydayDelay <= numDays)
                            profitDays[harvestDay + PaydayDelay] = true;
                    }
                }

                ++day;
            }

            // If not starting on day 1, convert back to full days
            if (StartDay > 1)
            {
                var shiftedPlantingDays = new bool[numDays + StartDay];

                for (int i = 1; i <= numDays; ++i)
                    shiftedPlantingDays[i + StartDay - 1] = plantingDays[i];

                plantingDays = shiftedPlantingDays;

                var shiftedHarvestDays = new bool[numDays + StartDay];

                for (int i = 1; i <= numDays; ++i)
                    shiftedHarvestDays[i + StartDay - 1] = harvestDays[i];

                harvestDays = shiftedHarvestDays;
            }
        }

        private PlantSchedule ShiftSchedule(in PlantSchedule plantSchedule)
        {
            if (StartDay > 1)
            {
                var shiftedSchedule = new PlantSchedule(numDays + StartDay - 1);

                for (int i = 1; i <= numDays; ++i)
                    shiftedSchedule.AddCrop(i + StartDay - 1, plantSchedule.GetCrop(i));

                return shiftedSchedule;
            }
            else
            {
                return new PlantSchedule(numDays);
            }
        }
    }
}

