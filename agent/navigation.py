"""Deterministic four-neighbour A*; no model calls or screen input."""
from heapq import heappop, heappush

def neighbours(tile):
    x, y = tile
    return [(x, y-1), (x+1, y), (x, y+1), (x-1, y)]

def astar(start, goals, passable):
    start, goals, allowed = tuple(start), set(map(tuple, goals)), set(map(tuple, passable))
    if not goals:
        return None
    goals &= allowed | {start}
    if not goals:
        return None
    heuristic = lambda p: min(abs(p[0]-g[0]) + abs(p[1]-g[1]) for g in goals)
    queue, cost, parent = [(heuristic(start), 0, start)], {start: 0}, {}
    while queue:
        _, distance, node = heappop(queue)
        if distance != cost[node]:
            continue
        if node in goals:
            path = [node]
            while node in parent:
                node = parent[node]
                path.append(node)
            return path[::-1]
        for target in neighbours(node):
            new_cost = distance + 1
            if target in allowed and new_cost < cost.get(target, float('inf')):
                cost[target], parent[target] = new_cost, node
                heappush(queue, (new_cost + heuristic(target), new_cost, target))
    return None
