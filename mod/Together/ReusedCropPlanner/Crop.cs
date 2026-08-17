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
    public class Crop
    {
        public int timeToMaturity { get; set; }
        public int yieldRate { get; set; }
        public string name { get; set; }

        public double buyPrice { get; set; }
        public double sellPrice { get; set; }
        public bool IsEnabled { get; set; } = true;
        // Spring, Summer, Fall, Winter
        public string Season { get; set; }
        // A few crops are two seasons long
        public string SecondSeason { get; set; }

        /// <summary>
        /// Per-tile profitability index starting on a specific day.
        /// </summary>
        public int CurrentProfitIndex(int day, int numDays, int paydayDelay)
        {
            if (IsPersistent(numDays))
            {
                var numHarvests = NumHarvests(day, numDays);

                if (numHarvests <= 0)
                    return (int) -buyPrice;
                else
                    return (int)((numHarvests * sellPrice) - buyPrice);
            }
            else
            {
                int totalDays = numDays - day;

                int numHarvests = totalDays / (timeToMaturity + paydayDelay);

                // The last iteration only needs to be timeToMaturity long to get a harvest, not timeToMaturity + paydayDelay.
                if (paydayDelay > 0)
                {
                    double remainingDays = totalDays % (double)(timeToMaturity + paydayDelay);

                    if (remainingDays >= timeToMaturity)
                        ++numHarvests;
                }

                if (numHarvests <= 0)
                    return (int) -buyPrice;
                else
                    return (int)((numHarvests * sellPrice) - (numHarvests * buyPrice));
            }
        }

        /// <summary>
        /// The real profit, in gold, if we plant the entire farm with this crop.
        /// </summary>
        public double CurrentProfit(int day, int availableTiles, double availableGold, int numDays, int PaydayDelay, out int numToPlant)
        {
            if (buyPrice > 0)
            {
                int unitsCanAfford = (int)(availableGold / buyPrice);
                bool goldLimited = availableTiles != -1 ? availableTiles >= unitsCanAfford : true;
                numToPlant = goldLimited ? unitsCanAfford : availableTiles;
            }
            else
            {
                if (availableTiles != -1)
                {
                    numToPlant = availableTiles;
                }
                else
                {
                    Console.WriteLine("[Crop.CurrentProfit] Warning: can't plant free crop if infinite tiles, becuase that would be infinite plants.");
                    numToPlant = 0;
                }

            }

            return numToPlant * CurrentProfitIndex(day, numDays, PaydayDelay);
        }

        /// <summary>
        /// Represents a crop type, like Wheat or Blueberry.
        /// </summary>
        /// <param name="timeToMaturity">time to crop's first yield</param>
        /// <param name="yieldRate">days between succesive yields after maturity</param>
        /// <param name="buyPrice">price that the seed was bought for</param>
        /// <param name="sellPrice">price that the crop will be sold at</param>
        public Crop(string name, int timeToMaturity, int yieldRate, double buyPrice, double sellPrice, bool isEnabled = true, string season = null, string secondSeason = null)
        {
            this.name = name;
            this.timeToMaturity = timeToMaturity;
            this.yieldRate = yieldRate;
            this.buyPrice = buyPrice;
            this.sellPrice = sellPrice;
            this.IsEnabled = isEnabled;
            this.Season = season;
            this.SecondSeason = secondSeason;
        }

        // If something costs $1 and sells for $3, its "multiple of money" is 3.  I.e., your money triples.
        public double InvestmentMultiple(int day, int maxDays)
        {
            return Return(day, maxDays) + 1;
        }

        /// <summary>
        /// The measure of PROFIT when harvesting this plant over the course of the year (as a multiple of the original cost).
        /// So a return of 2.0 means your profit is double the cost, which means you tripled your money. A return of 0 means no profit was made (and no money lost).
        /// A return of -1 means all money was lost.
        /// </summary>
        /// <param name="day">day crop is planted</param>
        /// <param name="maxDays">days in the season</param>
        /// <returns></returns>
        public double Return(int day, int maxDays)
        {
            return ((NumHarvests(day, maxDays) * sellPrice) - buyPrice) / buyPrice;
        }

        /// <summary>
        /// The number of harvests you get out of planting a crop on a given day.
        /// </summary>
        /// <param name="dayPlanted">day crop is planted</param>
        /// <param name="maxDays">days in the season</param>
        public int NumHarvests(int dayPlanted, int maxDays)
        {
            if (dayPlanted < 1 || maxDays < 1 || dayPlanted > maxDays)
                throw new Exception("dayPlanted and maxDays must be greater than 0, and dayPlanted must be less than or equal to maxDays.");

            int numHarvests = 0;

            if (yieldRate == -1)
                numHarvests = (maxDays - dayPlanted - timeToMaturity >= 0) ? 1 : 0;
           else
                numHarvests = (int)((maxDays - dayPlanted - timeToMaturity + yieldRate) / yieldRate); // rounds to floor

            return numHarvests < 0 ? 0 : numHarvests;
        }

        public List<int> HarvestDays(int plantDay, int numDays)
        {
            List<int> harvestDays = new List<int>();

            int harvestDate = plantDay + timeToMaturity;

            if (harvestDate <= numDays)
            {
                harvestDays.Add(harvestDate);

                while (harvestDate + yieldRate <= numDays)
                {
                    harvestDate += yieldRate;
                    harvestDays.Add(harvestDate);
                }
            }

            return harvestDays;
        }

        public bool IsPersistent(int numDays)
        {
            // YieldRate=1000 is a hacky way that the webpage specifies a crop as non-persistent, thus the check again a fairly big number.
            return yieldRate > 0 && yieldRate < 30;
        }

        public override string ToString()
        {
            return name;
        }

        public string Serialize()
        {
            return $"{name};{buyPrice};{sellPrice};{timeToMaturity};{yieldRate};{Season};{SecondSeason};{IsEnabled}";
        }

        public static Crop Deserialize(string serialized)
        {
            var cropParts = serialized.Split(';');

            var name = cropParts[0];
            var buyPrice = double.Parse(cropParts[1]);
            var sellPrice = double.Parse(cropParts[2]);
            var timeToMaturity = int.Parse(cropParts[3]);
            var yieldRate = int.Parse(cropParts[4]);
            var season = cropParts[5];
            var secondSeason = cropParts[6];
            var isEnabled = bool.Parse(cropParts[7]);

            return new Crop(name, timeToMaturity, yieldRate, buyPrice, sellPrice, isEnabled, season, secondSeason);
        }

        public static string SerializeCrops(IEnumerable<Crop> crops)
        {
            return string.Join("\n", crops.Select(c => c.Serialize()));
        }

        public static IEnumerable<Crop> DeserializeCrops(string serializedCrops)
        {
            return serializedCrops
                .Split(new[]{'\n'}, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => Deserialize(line));
        }

        ///// <summary>
        ///// Per-tile profitability index. How profitable a tile is, if we only plant this crop on it the entire month.
        /////
        ///// Note: this metric is misleading, because it's only useful if all tiles are filled.
        ///// But if you switch to another crop with a better TPI, that crop might not fill all the tiles at the same rate.
        ///// So it's only useful in situations where available tiles are so low, ANY crop can fill them up almost immediately.
        ///// </summary>
        //public int TPI
        //{
        //    get
        //    {
        //        if (IsPersistent)
        //        {
        //            var numHarvests = NumHarvests(1, numDays);
        //            return (int)((numHarvests * sellPrice) - buyPrice);
        //        }
        //        else
        //        {
        //            int numHarvests = numDays / (timeToMaturity + 1);
        //            return (int)((numHarvests * sellPrice) - (numHarvests * buyPrice));
        //        }
        //    }
        //}
    }
}

