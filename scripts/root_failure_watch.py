"""Immutable business events; polling cannot re-count receipts or re-label versions."""
class RootFailureWatch:
    def __init__(self):
        self.seen=set();self.counts={}
    def observe(self,event):
        if event.get('kind')!='task_finished':return None
        p=event['payload'];root=p.get('error','') or ''
        if p.get('state')!='failed' or not p.get('command_id') or not root.startswith('capacity_'):return None
        ident=(event.get('run'),p['command_id'])
        if ident in self.seen:return None
        self.seen.add(ident);key=(p.get('actor','player'),root,p['capacity_version'])
        self.counts[key]=self.counts.get(key,0)+1
        return dict(actor=key[0],root=key[1],capacity_version=key[2],count=self.counts[key]) if self.counts[key]>=3 else None
