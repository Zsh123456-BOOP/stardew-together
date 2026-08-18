// Source: mschult2/stardew-planner @ 724c4ab5732f83a8e39d6d99650c07dc5a3f8f62
// Extracted UpdateCalendar: explicit per-call payday delay; no web worker, global
// cache or rendering dependencies. Callers enforce real budget, stock and land.
namespace StardewCropCalculatorLibrary;
public static class CalendarCashFlow {
        public static void Apply(GameStateCalendar calendar, in int unitsToPlant, in Crop crop, int day, in int numDays,int paydayDelay=1)
        {
            if (unitsToPlant <= 0)
                return;

            int availableTiles = calendar.GameStates[day].FreeTiles;

            // Modify current day state.
            double cost = unitsToPlant * crop.buyPrice;
            double sale = unitsToPlant * crop.sellPrice;
            PlantBatch plantBatch = new PlantBatch(crop, unitsToPlant, day, numDays);
            var harvestDays = plantBatch.HarvestDays;

            double cumulativeSale = 0;
            int curUnits = unitsToPlant;

            calendar.GameStates[day].DayOfInterest = true;

            // Update game state calendar based on today's crop purchase.
            for (int j = day; j <= calendar.NumDays + 1; ++j)
            {
                if(j>numDays)curUnits=0; // crop-specific growing window may end before the global calendar.
                // Harvest day might increase tiles:
                if (plantBatch.HarvestDays.Contains(j))
                {
                    if (!plantBatch.Persistent && curUnits != 0)
                        curUnits = 0;
                }

                // Payday increases gold:
                if (plantBatch.HarvestDays.Contains(j - paydayDelay))
                {
                    cumulativeSale += sale;
                    calendar.GameStates[j].DayOfInterest = true;
                }

                // Decrease tiles if plant isn't dead
                if (curUnits > 0)
                {
                    if (availableTiles != -1)
                        calendar.GameStates[j].FreeTiles = calendar.GameStates[j].FreeTiles - curUnits;

                    calendar.GameStates[j].Plants.Add(plantBatch);
                }

                // Modify gold
                calendar.GameStates[j].Wallet = calendar.GameStates[j].Wallet + cumulativeSale - cost;
            }
        }
}

