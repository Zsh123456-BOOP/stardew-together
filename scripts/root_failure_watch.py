"""Count distinct declaration/execution attempts, never repeated polling of receipts."""
class RootFailureWatch:
    def __init__(self):
        self.seen=set();self.counts={}
    def observe(self,event):
        p=event.get('payload',{});kind=event.get('kind')
        if kind=='task_finished':
            if p.get('state')!='failed':return None
            root=p.get('error') or '';ident=p.get('attempt_id') or p.get('command_id')
        elif kind=='tool_result':
            result=p.get('result',{})
            if not isinstance(result,dict) or not result.get('error'):return None
            # An accepted task has its own terminal event. Do not count twice.
            if result.get('command_id') or result.get('task_id'):return None
            root=p.get('root_cause') or result['error'];ident=p.get('attempt_id')
        else:return None
        if not ident or not root:return None
        version=p.get('capacity_version')
        prefix='known_failure_conditions_unchanged:'
        if root.startswith(prefix):
            root=root[len(prefix):].split(':evidence=',1)[0]
            if root.startswith(('capacity:','capacity_relief:')):
                try:version=int(root.rsplit(':',1)[1])
                except ValueError:return None
                root='capacity_all_candidates_infeasible'
        if version is None:return None
        identity=(event.get('run'),ident)
        if identity in self.seen:return None
        self.seen.add(identity);key=(p.get('actor','player'),root,version)
        self.counts[key]=self.counts.get(key,0)+1
        return dict(actor=key[0],root=key[1],capacity_version=key[2],attempt_id=ident,count=self.counts[key],source=kind) if self.counts[key]>=3 else None
