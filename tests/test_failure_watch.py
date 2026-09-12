import sys,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
from root_failure_watch import RootFailureWatch

class FailureWatchChecks(unittest.TestCase):
 def event(self,attempt,version=105,tool=True):
  p=dict(attempt_id=attempt,actor='player',capacity_version=version)
  if tool:p['result']=dict(error=f'known_failure_conditions_unchanged:capacity:{version}')
  else:p.update(state='failed',error='capacity_all_candidates_infeasible',command_id='native-'+attempt)
  return dict(run='test',kind='tool_result' if tool else 'task_finished',payload=p)
 def test_declarations_and_execution_share_physical_root(self):
  w=RootFailureWatch()
  for i in range(3):
   e=self.event(str(i),tool=i!=0);stop=w.observe(e)
   self.assertEqual(stop is not None,i==2);self.assertIsNone(w.observe(e))
  self.assertIsNone(w.observe(self.event('new-version',106)))
 def test_explicit_physical_cause_wins_over_generic_guard(self):
  w=RootFailureWatch()
  for i in range(3):
   e=self.event(str(i));e['payload']['root_cause']='capacity_no_stackable_room'
   stop=w.observe(e)
  self.assertEqual(stop['root'],'capacity_no_stackable_room')
 def test_queued_and_terminal_not_double_counted(self):
  w=RootFailureWatch();e=self.event('one');e['payload']['result']['task_id']='one'
  self.assertIsNone(w.observe(e));self.assertIsNone(w.observe(self.event('one',tool=False)))
  self.assertEqual(sum(w.counts.values()),1)
if __name__=='__main__':unittest.main()
