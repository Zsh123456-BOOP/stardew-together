import tempfile
from pathlib import Path
import unittest
from agent.tasks import Store, validate_plan

class TaskTests(unittest.TestCase):
    def test_rejects_unknown_skills_and_invalid_areas(self):
        for step in [{'skill': 'teleport', 'target': [1, 1]}, {'skill': 'water_area', 'area': [4, 4, 1, 1]}]:
            with self.assertRaises(ValueError):
                validate_plan({'goal': 'test', 'steps': [step]})

    def test_pause_visible_across_connections(self):
        with tempfile.TemporaryDirectory() as d:
            path = Path(d) / 'tasks.sqlite'
            a, b = Store(path), Store(path)
            task_id = a.create({'goal': 'test', 'steps': [{'skill': 'move_to', 'target': [1, 1]}]}, 's1')
            b.update(task_id, control='pause')
            self.assertEqual(a.get(task_id)['control'], 'pause')
            a.event(task_id, 'action', {'status': 'succeeded'})
            self.assertEqual(b.events(task_id)[0]['data']['status'], 'succeeded')
            a.db.close()
            b.db.close()

if __name__ == '__main__':
    unittest.main()
