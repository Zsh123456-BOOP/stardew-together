import unittest
from agent.navigation import astar, neighbours

class NavigationTests(unittest.TestCase):
    def test_detour_and_shortest_path(self):
        cells = {(x, y) for x in range(5) for y in range(3)} - {(2, 1)}
        route = astar((0, 1), [(4, 1)], cells)
        self.assertEqual(len(route)-1, 6)
        self.assertNotIn((2, 1), route)
        self.assertTrue(all(b in neighbours(a) for a, b in zip(route, route[1:])))

    def test_unreachable(self):
        self.assertIsNone(astar((0, 0), [(2, 0)], [(0, 0), (2, 0)]))

    def test_interaction_stance(self):
        cells = {(x, y) for x in range(4) for y in range(4)} - {(2, 2)}
        route = astar((0, 0), neighbours((2, 2)), cells)
        self.assertIn(route[-1], neighbours((2, 2)))
        self.assertNotIn((2, 2), route)

    def test_already_at_goal(self):
        self.assertEqual(astar((1, 1), [(1, 1)], []), [(1, 1)])

if __name__ == '__main__':
    unittest.main()
