import copy
import tempfile
import unittest
from pathlib import Path
from agent.companion import validate_decision,decide_and_run,Memory


ACTOR={'id':'save:player:Abigail','name':'Abigail','location':'Farm',
       'candidates':[{'target_id':'real-rock','skill':'mine','tile':[49,18]}]}
REFUSAL={'decision':'refuse','speech':'今天想休息。','reason':'疲惫，不喜欢挖矿','skill':'wait','target_id':None}


class FakeModel:
    def json(self,system,context): return dict(REFUSAL),{'model':'fake-only-for-unit-test'}


class FakeBridge:
    def __init__(self): self.session='test'; self.requests=[]; self.stale=False; self.reads=0
    def state(self):
        self.reads+=1
        actor=copy.deepcopy(ACTOR)
        if self.stale and self.reads>1: actor['candidates']=[]
        return {'backend':'squad','actors':[actor],'location':'Farm','game_time':900}
    def request(self,method,path,body):
        self.requests.append((method,path,body))
        return {'status':'succeeded','command_id':body['command_id']}


class CompanionTests(unittest.TestCase):
    def test_refusal_cannot_smuggle_an_action(self):
        with self.assertRaisesRegex(ValueError,'decision_action_conflict'):
            validate_decision(dict(REFUSAL,skill='mine',target_id='real-rock'),ACTOR)

    def test_model_cannot_invent_target_or_force_flag(self):
        with self.assertRaisesRegex(ValueError,'invalid_target_id'):
            validate_decision(dict(REFUSAL,decision='accept',skill='mine',target_id='invented'),ACTOR)
        with self.assertRaisesRegex(ValueError,'invalid_decision_fields'):
            validate_decision(dict(REFUSAL,force=True),ACTOR)
        with self.assertRaisesRegex(ValueError,'invalid_target_id'):
            validate_decision(dict(REFUSAL,decision='accept',skill='mine',target_id={}),ACTOR)

    def test_invalid_model_decision_has_one_bounded_repair(self):
        class InvalidModel:
            calls=0
            def json(self,system,context):
                self.calls+=1
                return dict(REFUSAL,skill='mine',target_id='real-rock'),{'model':'fake'}
        with tempfile.TemporaryDirectory() as folder:
            model=InvalidModel(); bridge=FakeBridge()
            with self.assertRaisesRegex(RuntimeError,'decision_validation_failed'):
                decide_and_run('挖矿',bridge=bridge,model=model,memory=Memory(Path(folder)/'memory.sqlite'))
            self.assertEqual(model.calls,2)
            self.assertEqual(bridge.requests,[])

    def test_refusal_records_memory_without_dispatch(self):
        with tempfile.TemporaryDirectory() as folder:
            memory=Memory(Path(folder)/'memory.sqlite'); bridge=FakeBridge()
            result=decide_and_run('挖矿',bridge=bridge,model=FakeModel(),memory=memory)
            self.assertEqual(result['result']['status'],'not_dispatched')
            self.assertEqual(bridge.requests,[])
            self.assertEqual(len(memory.recent(ACTOR['id'])),1)
            self.assertEqual(memory.recent('other-save:player:Abigail'),[])

    def test_explicit_force_still_revalidates_world(self):
        with tempfile.TemporaryDirectory() as folder:
            memory=Memory(Path(folder)/'memory.sqlite'); bridge=FakeBridge(); bridge.stale=True
            result=decide_and_run('挖矿',force_skill='mine',bridge=bridge,model=FakeModel(),memory=memory)
            self.assertTrue(result['forced'])
            self.assertEqual(result['result']['error'],'stale_target')
            self.assertEqual(bridge.requests,[])

    def test_force_is_from_caller_and_result_is_persisted(self):
        with tempfile.TemporaryDirectory() as folder:
            memory=Memory(Path(folder)/'memory.sqlite'); bridge=FakeBridge()
            result=decide_and_run('挖矿',force_skill='mine',bridge=bridge,model=FakeModel(),memory=memory)
            self.assertEqual(result['model_decision']['decision'],'refuse')
            self.assertEqual(len(bridge.requests),1)
            self.assertEqual(memory.recent(ACTOR['id'])[0]['result']['status'],'succeeded')


if __name__=='__main__': unittest.main()
