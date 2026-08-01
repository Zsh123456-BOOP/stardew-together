"""Persistent deterministic skill executor; high-level plans come from the LLM/user."""
from __future__ import annotations

from contextlib import contextmanager
import fcntl
import json
from pathlib import Path
import sqlite3
import time
import uuid

from .client import Bridge, BridgeError, ROOT, actor_state
from .navigation import astar, neighbours

class Paused(Exception):
    pass

def validate_plan(plan):
    if not isinstance(plan, dict) or not isinstance(plan.get('goal'), str):
        raise ValueError('plan requires a goal string')
    steps = plan.get('steps')
    if not isinstance(steps, list) or not 1 <= len(steps) <= 20:
        raise ValueError('plan requires 1..20 steps')
    actors = set()
    for step in steps:
        if not isinstance(step, dict):
            raise ValueError('each step must be an object')
        skill = step.get('skill')
        actor = step.get('actor_id', 'bot-1')
        if not isinstance(actor, str) or not actor or not all(c.isalnum() or c in '-_' for c in actor):
            raise ValueError('invalid actor_id')
        actors.add(actor)
        if skill not in ('move_to', 'water_area', 'harvest_area'):
            raise ValueError('unknown skill: ' + str(skill))
        field, length = ('target', 2) if skill == 'move_to' else ('area', 4)
        coords = step.get(field)
        if not isinstance(coords, list) or len(coords) != length or any(type(c) is not int or c < 0 for c in coords):
            raise ValueError('invalid ' + field)
        if field == 'area' and (coords[0] > coords[2] or coords[1] > coords[3]):
            raise ValueError('area bounds reversed')
    if len(actors) != 1:
        raise ValueError('single-actor task required; multi-actor coordination is a later stage')
    return next(iter(actors))

class Store:
    def __init__(self, path=None):
        self.path = Path(path or ROOT / 'work/tasks.sqlite')
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.db = sqlite3.connect(self.path, timeout=10)
        self.db.row_factory = sqlite3.Row
        self.db.execute('PRAGMA journal_mode=WAL')
        self.db.executescript('''
            CREATE TABLE IF NOT EXISTS tasks (
                id TEXT PRIMARY KEY, goal TEXT, plan TEXT, actor TEXT, session TEXT,
                step INTEGER DEFAULT 0, status TEXT DEFAULT 'queued', control TEXT DEFAULT 'run',
                error TEXT, created REAL, updated REAL);
            CREATE TABLE IF NOT EXISTS events (
                seq INTEGER PRIMARY KEY AUTOINCREMENT, task_id TEXT, time REAL, kind TEXT, data TEXT);
        ''')

    def create(self, plan, session):
        actor = validate_plan(plan)
        task_id = uuid.uuid4().hex[:12]
        now = time.time()
        with self.db:
            self.db.execute('INSERT INTO tasks(id,goal,plan,actor,session,created,updated) VALUES(?,?,?,?,?,?,?)',
                            (task_id, plan['goal'], json.dumps(plan, ensure_ascii=False), actor, session, now, now))
        return task_id

    def get(self, task_id):
        row = self.db.execute('SELECT * FROM tasks WHERE id=?', (task_id,)).fetchone()
        if row is None:
            raise ValueError('unknown task')
        result = dict(row)
        result['plan'] = json.loads(result['plan'])
        return result

    def update(self, task_id, **fields):
        allowed = {'step', 'status', 'control', 'error'}
        if not fields.keys() <= allowed:
            raise ValueError('invalid task update')
        fields['updated'] = time.time()
        with self.db:
            self.db.execute('UPDATE tasks SET ' + ','.join(k + '=?' for k in fields) + ' WHERE id=?', (*fields.values(), task_id))

    def event(self, task_id, kind, data):
        with self.db:
            self.db.execute('INSERT INTO events(task_id,time,kind,data) VALUES(?,?,?,?)',
                            (task_id, time.time(), kind, json.dumps(data, ensure_ascii=False)))

    def events(self, task_id):
        return [{'seq': r['seq'], 'time': r['time'], 'kind': r['kind'], 'data': json.loads(r['data'])}
                for r in self.db.execute('SELECT * FROM events WHERE task_id=? ORDER BY seq', (task_id,))]

    @contextmanager
    def actor_lock(self, actor):
        path = self.path.parent / f'actor-{actor}.lock'
        with path.open('a') as handle:
            try:
                fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError:
                raise BridgeError('actor_has_active_task') from None
            try:
                yield
            finally:
                fcntl.flock(handle, fcntl.LOCK_UN)

class Runner:
    def __init__(self, bridge=None, store=None):
        self.bridge = bridge or Bridge()
        self.store = store or Store()
        self.task_id = None
        self.deadline = 0

    def checkpoint(self):
        if self.store.get(self.task_id)['control'] == 'pause':
            raise Paused()
        if time.monotonic() > self.deadline:
            raise BridgeError('task_timeout')

    def perform(self, actor, skill, target):
        self.checkpoint()
        command_id = uuid.uuid4().hex
        self.store.event(self.task_id, 'dispatch', {'command_id': command_id, 'actor': actor, 'skill': skill, 'target': target})
        try:
            result = self.bridge.action(actor, skill, target, command_id=command_id)
        except BridgeError as e:
            self.store.event(self.task_id, 'action_error', {'command_id': command_id, 'error': str(e)})
            raise
        self.store.event(self.task_id, 'action', result)
        self.checkpoint()
        return result

    def move(self, actor, goals):
        retries = 0
        while True:
            self.checkpoint()
            state = self.bridge.state()
            current = actor_state(state, actor)['tile']
            if tuple(current) in set(map(tuple, goals)):
                return
            grid = self.bridge.map(actor)
            route = astar(current, goals, grid['passable'])
            if route is None:
                raise BridgeError('unreachable')
            try:
                for point in route[1:]:
                    self.perform(actor, 'step', point)
                return
            except BridgeError as e:
                # Only confirmed movement failures can be safely replanned automatically.
                if str(e) not in ('unreachable', 'effect_not_observed') or retries >= 2:
                    raise
                retries += 1
                self.store.event(self.task_id, 'replan', {'reason': str(e), 'attempt': retries})

    def area(self, actor, skill, area):
        x1, y1, x2, y2 = area
        predicate = 'needs_water' if skill == 'water_area' else 'harvestable'
        primitive = 'water' if skill == 'water_area' else 'harvest'
        blocked = set()
        for _ in range(512):
            self.checkpoint()
            state = self.bridge.state()
            candidates = [tuple(c['tile']) for c in state['crops'] if c[predicate]
                          and x1 <= c['tile'][0] <= x2 and y1 <= c['tile'][1] <= y2]
            if not candidates:
                return
            grid = self.bridge.map(actor)
            position = actor_state(state, actor)['tile']
            routes = [(astar(position, neighbours(t), grid['passable']), t) for t in candidates if t not in blocked]
            reachable = [(route, target) for route, target in routes if route is not None]
            if not reachable:
                raise BridgeError('unreachable_targets:' + json.dumps(candidates))
            _, target = min(reachable, key=lambda pair: (len(pair[0]), pair[1]))
            try:
                self.move(actor, neighbours(target))
            except BridgeError as e:
                if str(e) == 'unreachable':
                    blocked.add(target)
                    self.store.event(self.task_id, 'target_blocked', {'target': target})
                    continue
                raise
            # A player can finish/remove the target while the robot is walking.
            fresh = self.bridge.state()
            crop = next((c for c in fresh['crops'] if tuple(c['tile']) == target), None)
            if crop is None or not crop[predicate]:
                self.store.event(self.task_id, 'target_already_done', {'target': target})
                continue
            self.perform(actor, primitive, target)
        raise BridgeError('target_iteration_limit')

    def run(self, task_id, timeout=300):
        self.task_id, self.deadline = task_id, time.monotonic() + timeout
        task = self.store.get(task_id)
        if task['status'] == 'succeeded':
            return task
        with self.store.actor_lock(task['actor']):
            try:
                state = self.bridge.state()
                if state['session_id'] != task['session']:
                    raise BridgeError('stale_session: create a new plan after load/reset')
                self.store.update(task_id, status='running', error=None)
                for index in range(task['step'], len(task['plan']['steps'])):
                    self.checkpoint()
                    step = task['plan']['steps'][index]
                    self.store.event(task_id, 'step_started', {'index': index, 'step': step})
                    if step['skill'] == 'move_to':
                        self.move(task['actor'], [step['target']])
                    else:
                        self.area(task['actor'], step['skill'], step['area'])
                    self.store.update(task_id, step=index+1)
                self.store.update(task_id, status='succeeded')
            except Paused:
                self.store.update(task_id, status='paused')
                self.store.event(task_id, 'paused', {'boundary': 'completed_action'})
            except (BridgeError, ValueError, StopIteration) as e:
                self.store.update(task_id, status='failed', error=str(e))
                self.store.event(task_id, 'failed', {'error': str(e)})
        return self.store.get(task_id)
