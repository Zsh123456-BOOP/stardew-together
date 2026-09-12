using Together;
public static class CapacityChecks {
    public static void Run(Action<bool,string> check) {
        check(LoadoutSafety.Unsupported("player.build")=="construction_materials"&&LoadoutSafety.Unsupported("player.order_donate")=="partial_delivery"&&LoadoutSafety.Unsupported("player.craft")==null,"unsupported declarations rejected independently of capacity");
        check(LoadoutSafety.MultipleStorage(false,new[]{false,false},true)&&!LoadoutSafety.MultipleStorage(false,new[]{true,false},true)&&!LoadoutSafety.MultipleStorage(true,Array.Empty<bool>(),true),"multi-storage shape requires union and excludes already sufficient bag or single box");
        foreach(var code in new[]{"loadout_stock_changed","loadout_storage_busy","loadout_native_capacity_changed"})check(!LoadoutSafety.MustStop(code,true)&&LoadoutSafety.MustStop(code,false)&&LoadoutSafety.MustStop(code,null)&&RecoveryPolicy.CanWait(code),"transfer interruption requires verified conservation: "+code);
        check(LoadoutSafety.MustStop("loadout_conservation_failed",true),"explicit conservation failure remains fatal");
        check(RecoveryPolicy.CanWait("loadout_shape_unsupported:multiple_storage")&&CapacityState.IsConstraint("loadout_shape_unsupported:multiple_storage")&&!CapacityState.IsCapacity("loadout_shape_unsupported:multiple_storage"),"shape constraint cannot globally block unrelated capacity-feasible work");
        var clicks=new MenuEffectWatch();
        check(clicks.Observe("a","c12",false)==1&&clicks.Observe("a","c12",false)==2&&clicks.Observe("a","c12",false)==3,"third same native no-effect is synchronous");
        check(clicks.Observe("b","c12",false)==1&&clicks.Observe("a","c13",false)==1,"different token or target not the same no-effect");
        check(clicks.Observe("a","c12",true)==0&&clicks.Observe("a","c12",false)==1,"verified native progress resets no-effect run");
        var wood=new StackKey("wood",0,"");var chest=new StackKey("chest",0,"");
        CapacitySnapshot Bag(int woodCount,bool protect=false)=>new(2,new[]{(new StackKey("axe",0,""),1,1,false),(wood,woodCount,999,protect)});
        var plan=new CapacityOp[]{new TakeOp(wood,50),new PutOp(chest,1,1)};
        var source=Bag(50);var good=CapacityPlan.Simulate(source,plan);
        check(good.Feasible&&good.PeakOccupied==2&&good.FinalOccupied==2&&source.Occupied==2,"full bag consumption frees exact output slot without mutating source");
        var bad=CapacityPlan.Simulate(Bag(51),plan);check(!bad.Feasible&&bad.FailedStep==1,"partial stack consumption cannot invent a slot");
        check(!CapacityPlan.Simulate(Bag(50,true),plan).Feasible,"protected material cannot free capacity");
        check(!CapacityPlan.Simulate(Bag(998),new CapacityOp[]{new PutOp(wood,2,999)}).Feasible,"stack maximum checked for whole output");
        check(!CapacityPlan.Simulate(Bag(1),new CapacityOp[]{new PutOp(wood with{Quality=2},1,999)}).Feasible,"qualities never share capacity");
        check(!CapacityPlan.Simulate(new(1,Array.Empty<(StackKey,int,int,bool)>()),new CapacityOp[]{new PutOp(chest,2,1)}).Feasible,"unstackable outputs each require a slot");
        var one=new CapacitySnapshot(1,new[]{(wood,50,999,false)});var stone=new StackKey("stone",0,"");
        var warehouse=new CapacitySnapshot(1,new[]{(stone,20,999,false)});
        var swap=new[]{new PackingMove("put",false,wood,50,999),new PackingMove("get",true,stone,20,999)};
        check(!LoadoutPlan.Arrange(one,warehouse,swap).Feasible,"two full containers cannot pretend a simultaneous swap frees capacity");
        var roomy=new CapacitySnapshot(2,new[]{(stone,20,999,false)});
        var exchange=LoadoutPlan.Arrange(one,roomy,swap);
        check(exchange.Feasible&&exchange.Moves[0].Id=="put"&&one.FreeSlots==0&&roomy.FreeSlots==1,"deposit then withdraw fits intermediate states without mutating source");
        check(CapacityPlan.RequiredSlots("fish")==0,"unknown outputs no longer impose two permanently unused slots");
        var relief=CapacityRelief.Choose(Bag(51),new[]{new CapacityCandidate("craft",1,0,true,"",plan),new CapacityCandidate("sell",7,0,false,"",new CapacityOp[]{new TakeOp(wood,51)})},0);
        check(relief.Selected==null&&relief.Candidates.Count==2&&relief.Candidates.All(c=>!c.Eligible),"all exclusions retained; unauthorized sale never selected");
        var recursive=CapacityRelief.Choose(Bag(50),new[]{new CapacityCandidate("store",5,0,true,"",new CapacityOp[]{new TakeOp(wood,50)})},1);
        check(recursive.RootCause=="relief_depth_limit"&&!CapacityState.IsConstraint(recursive.RootCause)&&recursive.Selected==null&&recursive.Candidates[0].Reason=="relief_depth_limit","capacity relief cannot recursively resolve its own prerequisite");
        var facts=new CapacityState();facts.Observe("same_bag_and_storage");facts.Block("player","capacity_no_free_slot",4,630);long version=facts.Version;
        var survival=new SurvivalState();survival.NewDay(5);facts.Observe("same_bag_and_storage");
        check(facts.Version==version&&facts.Blocked("player"),"date alone does not release actor/root capacity constraint");
        facts.Block("player","capacity_no_free_slot",5,700);check(facts.Constraints.Count==1,"same root shared across tools, not a new independent counter");
        facts.Observe("wood_consumed_chest_placed");check(facts.Version==version+1&&!facts.Blocked("player"),"real capacity change releases constraint and increments version");
        var quality=new SurvivalQuality();quality.Current(0,500,20);quality.Sample(0,10,"progressing_work");quality.Sample(0,10,"waiting_model");
        quality.Days[0].Finalized=true;quality.Current(1).Finalized=true;
        check(quality.Days[0].EffectiveLaborMinutes==10&&quality.Days[0].AvailableMinutes==1200&&!quality.G2,"quality separates actual progress and does not shrink denominator for early sleep");
        quality.Days[0].DegradationTime=630;check(!quality.G1,"early degradation explicitly fails G1");
        var peak=CapacityPlan.Simulate(new(2,new[]{(wood,50,999,false)}),new CapacityOp[]{new PutOp(chest,1,1),new PutOp(new("stone",0,""),1,999),new TakeOp(wood,50)});
        check(!peak.Feasible&&peak.FailedStep==1&&peak.PeakOccupied==2,"intermediate overflow rejected even if final inventory would fit");
    }
}
