using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    internal void RecordMenuEvidence(string kind,object data)=>WriteBusinessLog(kind,AgentJson.Encode(data));
    internal void StopMenuNoEffect(string reason) {
        // Flush the third receipt and scene BEFORE Pause resets queues/runtime.
        RecordMenuEvidence("menu_execution_stop",new{reason,snapshot=AgentSnapshot(),inventory=AgentToolRegistry.Inventory(),held=NativeMenuTools.HeldItem() is {} h?AgentToolRegistry.ItemInfo(h):null,stage="before_pause"});
        businessWriter.Flush();PauseAutoplay(reason);
        RecordMenuEvidence("menu_execution_stopped",new{reason,status=Data.Autoplay.Status});businessWriter.Flush();
    }
}
