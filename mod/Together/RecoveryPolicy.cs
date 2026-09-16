namespace Together;

// Expected resource/window exhaustion yields a plan. Unknown exceptions,
// conservation failures, permission conflicts and manual cancellation don't.
public static class RecoveryPolicy {
    private static readonly HashSet<string> WaitCodes=new(StringComparer.Ordinal) {
        "cleanup_insufficient_remaining_allowance","cleanup_day_replan","cleanup_tool_energy_unavailable","loadout_stock_changed","loadout_storage_busy","loadout_native_capacity_changed","loadout_parent_cancelled_or_expired","cleanup_yield_to_production","cleanup_committed_care_or_return_reserve","loadout_intermediate_capacity_or_stock_blocked","planned_labor_allowance_used","partner_labor_reserved_for_farm_or_exhausted","partner_labor_insufficient_before_dispatch","time_reserve_reached","energy_reserve_reached","work_budget_reached","no_matching_targets","remaining_targets_unreachable",
        "capacity_no_free_slot","capacity_no_stackable_room","capacity_no_reachable_storage","capacity_all_candidates_infeasible","capacity_all_items_protected","player_in_danger","animal_care_energy_reserve","targets_claimed_by_other_actor",
        "fishing_resource_or_time_reserve_reached","fishing_attempt_budget_reached","fishing_trip_time_or_attempt_budget","fishing_trip_supply_reserve","target_fish_no_current_reachable_conditions_or_sites",
        "mine_execution_budget_return","mine_time_reserve_return","mine_supply_reserve_return","mine_inventory_return","mine_energy_before_next_stone","mine_no_reachable_progress_route",
        "native_skull_order_forbids_food","native_nausea_requires_ginger","shop_closed","npc_unavailable_or_sleeping",
        "target_fish_window_closed","day_changed_replan"
    };
    public static bool CanWait(string? error)=>error!=null&&(WaitCodes.Contains(error)||error.StartsWith("loadout_shape_unsupported:")||error.StartsWith("loadout_missing:")||error.StartsWith("known_failure_conditions_unchanged:"));
}
