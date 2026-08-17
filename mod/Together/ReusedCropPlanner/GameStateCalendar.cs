// Source: mschult2/stardew-planner @ 724c4ab5732f83a8e39d6d99650c07dc5a3f8f62
// Only detached calendar / batch models reused; UI and unbounded factory search excluded.
// Fix: preserve NumDays when copying PlantBatch.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StardewCropCalculatorLibrary
{
    public static class QueueExtensions
    {
        public static void EnqueueRange<T>(this Queue<T> queue, IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                queue.Enqueue(item);
            }
        }
    }

    /// <summary> The state of our farm on every day. Includes the day after the last day of the season, since we still get paid on that day. </summary>
    public class GameStateCalendar
    {
        public readonly int NumDays;

        public double Wealth => GameStates[NumDays + 1].Wallet;

        /// <summary> The state of our farm on a particular day. Ie, how many plants, free tiles, and gold we have. </summary>
        public readonly SortedDictionary<int, GameState> GameStates = new SortedDictionary<int, GameState>();

        /// <summary>
        /// Get all planted crops in the order we plant them.
        /// </summary>
        /// <returns>Non-null list. Empty if nothing planted.</returns>
        public List<PlantBatch> GetPlantSequence()
        {
            var plantBatchSequence = new List<PlantBatch>();
            var plantBatchIdSequence = new List<string>();

            foreach (KeyValuePair<int, GameState> curGameStatePair in GameStates)
            {
                var curDay = curGameStatePair.Key;
                var curGameState = curGameStatePair.Value;

                if (curGameState.DayOfInterest)
                {
                    foreach (var plantBatch in curGameState.Plants)
                    {
                        if (!plantBatchIdSequence.Contains(plantBatch.Id))
                        {
                            plantBatchSequence.Add(plantBatch);
                            plantBatchIdSequence.Add(plantBatch.Id);
                        }
                    }
                }
            }

            return plantBatchSequence;
        }

        private GameStateCalendar(int numDays)
        {
            NumDays = numDays;
        }

        public GameStateCalendar(int numDays, int availableTiles, double availableGold)
        {
            NumDays = numDays;

            // Adding one more day in case a crop is harvested on the last day.
            // In this case, we don't get our payday until the following day. So technically
            // that following day may have a state we care about, ie a larger balance.
            for (int i = 1; i <= numDays + 1; ++i)
            {
                GameStates.Add(i, new GameState());
                GameStates[i].Wallet = availableGold;
                GameStates[i].FreeTiles = availableTiles;
            }
        }

        /// <summary>
        /// Deserialize GameStateCalendar.
        /// Days before the first listed are omitted for perf reasons.
        /// Plants are omitted for perf reasions.
        /// </summary>
        public GameStateCalendar(int numDays, Dictionary<string, Crop> cropDictonary, string serializedCalendar)
        {
            NumDays = numDays;

            string[] lines = serializedCalendar.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var daysOfInterest = new Dictionary<int, GameState>();
            int firstDay = 1;
            bool haveFirstDay = false;

            foreach (var line in lines)
            {
                // Each line is: day_wallet_freeTiles
                var parts = line.Split('_');

                int day = int.Parse(parts[0]);
                double wallet = double.Parse(parts[1]);
                int freeTiles = int.Parse(parts[2]);

                if (!haveFirstDay)
                {
                    haveFirstDay = true;
                    firstDay = day;
                }

                // Create GameState
                daysOfInterest[day] = new GameState() { Wallet = wallet, FreeTiles = freeTiles, DayOfInterest = true };

                // Append plant list to GameState
                string serializedPlantBatches = parts[3];
                if (!string.IsNullOrWhiteSpace(serializedPlantBatches))
                {
                    var plantBatchesParts = serializedPlantBatches.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var serializedPlantBatch in plantBatchesParts)
                    {
                        var plantBatchParts = serializedPlantBatch.Split(';');

                        string cropName = plantBatchParts[0];
                        int cropCount = int.Parse(plantBatchParts[1]);
                        int plantDay = int.Parse(plantBatchParts[2]);

                        daysOfInterest[day].Plants.Add(new PlantBatch(cropDictonary[cropName], cropCount, plantDay, numDays));
                    }
                }
            }

            double lastWallet = 0;
            int lastTiles = 0;
            var lastPlants = new List<PlantBatch>();

            for (int i = firstDay; i <= numDays + 1; ++i)
            {
                if (daysOfInterest.TryGetValue(i, out var dayOfInterest))
                {
                    GameStates.Add(i, dayOfInterest);
                    lastWallet = dayOfInterest.Wallet;
                    lastTiles = dayOfInterest.FreeTiles;
                    lastPlants = dayOfInterest.Plants;
                }
                else
                {
                    GameStates.Add(i, new GameState());
                    GameStates[i].Wallet = lastWallet;
                    GameStates[i].FreeTiles = lastTiles;

                    foreach (var curLastPlant in lastPlants)
                        GameStates[i].Plants.Add(curLastPlant);
                }
            }
        }

        /// <summary>
        /// Clone input calendar. Input range is deep copy that is safe to modify. The other days are omitted.
        /// </summary>
        /// <param name="otherCalendar">Input calendar to copy.</param>
        /// <param name="startingDay">Use 1 to start at beginning of season.</param>
        /// <param name="endingDay">Use 0 to go to end of season.</param>
        public GameStateCalendar(GameStateCalendar otherCalendar, int startingDay, int endingDay, bool deep = true)
        {
            NumDays = otherCalendar.NumDays;

            // Deep copy of indicated range
            if (deep)
            {
                for (int i = startingDay; i <= endingDay; ++i)
                    GameStates.Add(i, new GameState());
            }

            Merge(otherCalendar, startingDay, endingDay, deep);

            //// Shallow copy of other range
            //for (int i = 1; i <= NumDays + 1; ++i)
            //{
            //    if (i < startingDay || i > endingDay)
            //        GameStates.Add(i, otherCalendar.GameStates[i]);
            //}
        }

        /// <summary>
        /// DeepCopy otherCalendar onto this calendar, within a certain range.
        /// Values outside the indicated range are left untouched.
        /// </summary>
        /// <param name="otherCalendar">The calendar to copy</param>
        /// <param name="startingDay">The day to copy from. Default is 1.</param>
        /// <param name="endingDay">The day to end copying on. Default is day after the last day of the season.</param>
        public void Merge(GameStateCalendar otherCalendar, int startingDay = 1, int endingDay = 0, bool deep = true)
        {
            if (endingDay == 0)
                endingDay = NumDays + 1;

            for (int i = startingDay; i <= endingDay; ++i)
            {
                if (deep)
                {
                    GameStates[i].Wallet = otherCalendar.GameStates[i].Wallet;
                    GameStates[i].FreeTiles = otherCalendar.GameStates[i].FreeTiles;
                    GameStates[i].DayOfInterest = otherCalendar.GameStates[i].DayOfInterest;

                    GameStates[i].Plants.Clear();

                    foreach (PlantBatch otherPlantBatch in otherCalendar.GameStates[i].Plants)
                        GameStates[i].Plants.Add(new PlantBatch(otherPlantBatch));
                }
                else
                {
                    GameStates[i] = otherCalendar.GameStates[i];
                }
            }
        }

        /// <summary>
        /// Return a new schedule shifted forward by a few days (shallow copy). Example: instead of starting at day 1, it starts at day 15.
        /// </summary>
        /// <param name="calendar">The calendar to use a shifted version of.</param>
        /// <param name="daysToShift">Number of days to shift the schedule forward.</param>
        static public GameStateCalendar Shift(GameStateCalendar calendar, int daysToShift)
        {
            GameStateCalendar newCalendar = new GameStateCalendar(calendar.NumDays);

            foreach (var gameStatePair in calendar.GameStates)
            {
                var oldState = gameStatePair.Value;

                var newState = newCalendar.GameStates[gameStatePair.Key + daysToShift] = oldState;

                for (int i = 0; i < oldState.Plants.Count; ++i)
                {
                    PlantBatch oldBatch = newState.Plants[i];
                    newState.Plants[i] = new PlantBatch(oldBatch.CropType, oldBatch.Count, oldBatch.PlantDay + daysToShift, oldBatch.NumDays + daysToShift);
                }
            }

            return newCalendar;
        }

        public override string ToString()
        {
            StringBuilder sb = new();

            foreach (var gameStatePair in GameStates)
            {
                var day = gameStatePair.Key;
                var gameState = gameStatePair.Value;

                sb.Append($"Day {day}: {gameState.Plants.Count} plants, {gameState.Wallet}g  ");

                sb.Append('(');
                for (int plantIndex = 0; plantIndex < gameState.Plants.Count; ++plantIndex)
                {
                    if (plantIndex > 0)
                        sb.Append('-');

                    var plant = gameState.Plants[plantIndex];
                    sb.Append($"{plant.Count} {plant.CropType}; {plant.PlantDay}");
                }
                sb.AppendLine(")");
            }

            return sb.ToString();
        }
    }

    /// <summary> The state of our farm on a given day. </summary>
    public class GameState
    {
        /// <summary>  How much gold we have. </summary>
        public double Wallet = 0;

        /// <summary>  How many free tiles we have. </summary>
        public int FreeTiles = 0;

        /// <summary> Crops currently planted on the farm. Crops are grouped by batch. </summary>
        public readonly List<PlantBatch> Plants = new List<PlantBatch>();

        /// <summary>  Something happens on this day - either we get more gold, or more tiles. </summary>
        public bool DayOfInterest = false;

        public override string ToString()
        {
            string plantsDescription = "";
            foreach (var batch in Plants)
                plantsDescription += $"{batch.CropType.name}: {batch.Count}, ";

            return $"{plantsDescription}available tiles: {FreeTiles}, available gold: {Wallet}";
        }
    }

    /// <summary>
    /// A batch of crops planted on our farm. All one type, all planted at the same time.
    /// A batch can be treated as one super-plant harvested all at once.
    /// This class meant to be IMMUTABLE. Do not modify after construction.
    /// </summary>
    public class PlantBatch
    {
        public Crop CropType { get; }
        public int Count { get; }
        /// <summary> PlantBatch is supposed to be read-only so it can be reused, so don't modify this after construction. </summary>
        public SortedSet<int> HarvestDays { get; } = new SortedSet<int>();
        public bool Persistent => CropType.IsPersistent(NumDays);
        public int NumDays { get; }

        public string Id { get; }

        public int PlantDay { get; }

        public PlantBatch(Crop cropType, int cropCount, int plantDay, int numDays)
        {
            Id = Guid.NewGuid().ToString();

            NumDays = numDays;
            CropType = cropType;
            Count = cropCount;
            PlantDay = plantDay;

            foreach (var harvestDay in cropType.HarvestDays(plantDay, numDays))
                HarvestDays.Add(harvestDay);
        }

        public PlantBatch(PlantBatch otherPlantBatch)
        {
            Id = otherPlantBatch.Id;
            CropType = otherPlantBatch.CropType;
            NumDays = otherPlantBatch.NumDays;
            Count = otherPlantBatch.Count;
            PlantDay = otherPlantBatch.PlantDay;

            foreach (int otherHarvestDay in otherPlantBatch.HarvestDays)
                HarvestDays.Add(otherHarvestDay);
        }

        public override string ToString()
        {
            return $"{Count} {CropType.name}";
        }
    }


}

