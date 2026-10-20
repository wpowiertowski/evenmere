using System.Collections.Immutable;

namespace Evenmere.Content.Lint.Rules;

/// <summary>
/// EVL003: the tech tree and the civics tree are acyclic, and every node can be reached from a root (a node with no
/// <c>requires</c>). A node is reachable when all of its requirements are, which in an acyclic tree is every node; so
/// any node that is not reachable is in a cycle or depends on one, and the cycle's path is reported.
/// </summary>
internal static class TreeRule
{
    public static void Run(LintContext context)
    {
        ContentDb db = context.Db;
        CheckTree(context, ContentType.Techs, "tech", db.Techs.Names, [.. db.Techs.Items.Select(static t => IndexesOf(t.Requires, static id => id.Value))]);
        CheckTree(context, ContentType.Civics, "civic", db.Civics.Names, [.. db.Civics.Items.Select(static c => IndexesOf(c.Requires, static id => id.Value))]);
    }

    private static int[] IndexesOf<TId>(ImmutableArray<TId> requires, Func<TId, ushort> value)
    {
        int[] indexes = new int[requires.Length];
        for (int i = 0; i < indexes.Length; i++)
        {
            indexes[i] = value(requires[i]);
        }

        return indexes;
    }

    private static void CheckTree(LintContext context, ContentType type, string noun, ImmutableArray<string> names, int[][] requires)
    {
        bool[] inCycle = new bool[names.Length];

        var cycles = new List<int[]>();
        foreach (int[] component in StronglyConnectedComponents(requires))
        {
            if (component.Length > 1 || Array.IndexOf(requires[component[0]], component[0]) >= 0)
            {
                cycles.Add(component);
                foreach (int member in component)
                {
                    inCycle[member] = true;
                }
            }
        }

        // Components come out in an order that depends on the walk, so report them by their lowest node.
        cycles.Sort(static (a, b) => a[0].CompareTo(b[0]));
        foreach (int[] component in cycles)
        {
            ReportCycle(context, type, noun, names, requires, component);
        }

        bool[] reachable = ReachableFromRoots(requires);
        for (int node = 0; node < names.Length; node++)
        {
            if (reachable[node] || inCycle[node])
            {
                continue;
            }

            // Not in a cycle itself, so some requirement is unreachable; point at the first one.
            int first = Array.FindIndex(requires[node], target => !reachable[target]);
            string blocker = names[requires[node][first]];
            context.Error(
                LintCodes.Evl003,
                context.FileOf(type, names[node]),
                LintContext.ItemPointer("requires", first),
                $"{noun} '{names[node]}' can never be reached from a root: it requires '{blocker}', which is in or behind a cycle");
        }
    }

    private static void ReportCycle(LintContext context, ContentType type, string noun, ImmutableArray<string> names, int[][] requires, int[] component)
    {
        int start = component[0];
        int[] path = ShortestCycleThrough(start, requires, component);

        // path is start, a, b, ..., z; the cycle closes with z requiring start. Find which requires entry leaves start.
        int next = path.Length > 1 ? path[1] : start;
        int entry = Array.IndexOf(requires[start], next);

        string chain = string.Join(" -> ", path.Select(node => names[node]).Append(names[start]));
        context.Error(
            LintCodes.Evl003,
            context.FileOf(type, names[start]),
            LintContext.ItemPointer("requires", entry),
            $"{noun} cycle: {chain} (each {noun} requires the next); a cycle can never be completed");
    }

    /// <summary>Finds the shortest way from a node back to itself through the members of its component, by breadth-first search.</summary>
    private static int[] ShortestCycleThrough(int start, int[][] requires, int[] component)
    {
        bool[] member = new bool[requires.Length];
        foreach (int node in component)
        {
            member[node] = true;
        }

        int[] parent = new int[requires.Length];
        Array.Fill(parent, -1);
        bool[] seen = new bool[requires.Length];
        seen[start] = true;

        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            foreach (int target in requires[current])
            {
                if (!member[target])
                {
                    continue;
                }

                if (target == start)
                {
                    var path = new List<int>();
                    for (int node = current; node != -1; node = parent[node])
                    {
                        path.Add(node);
                    }

                    path.Reverse();
                    return [.. path];
                }

                if (!seen[target])
                {
                    seen[target] = true;
                    parent[target] = current;
                    queue.Enqueue(target);
                }
            }
        }

        // Unreachable for a real component: every member can get back to every other.
        return [start];
    }

    /// <summary>Marks every node whose requirements are all reachable, starting from the nodes with none (Kahn's algorithm).</summary>
    private static bool[] ReachableFromRoots(int[][] requires)
    {
        int count = requires.Length;
        int[] waitingOn = new int[count];
        var dependents = new List<int>[count];
        for (int node = 0; node < count; node++)
        {
            dependents[node] = [];
        }

        for (int node = 0; node < count; node++)
        {
            waitingOn[node] = requires[node].Length;
            foreach (int target in requires[node])
            {
                dependents[target].Add(node);
            }
        }

        bool[] reachable = new bool[count];
        var ready = new Queue<int>();
        for (int node = 0; node < count; node++)
        {
            if (waitingOn[node] == 0)
            {
                ready.Enqueue(node);
            }
        }

        while (ready.Count > 0)
        {
            int node = ready.Dequeue();
            reachable[node] = true;
            foreach (int dependent in dependents[node])
            {
                waitingOn[dependent]--;
                if (waitingOn[dependent] == 0)
                {
                    ready.Enqueue(dependent);
                }
            }
        }

        return reachable;
    }

    /// <summary>
    /// Tarjan's algorithm, without recursion so that a long chain cannot overflow the stack. Each component is sorted;
    /// a component with more than one node (or one node that requires itself) is a cycle.
    /// </summary>
    private static List<int[]> StronglyConnectedComponents(int[][] edges)
    {
        int count = edges.Length;
        int[] order = new int[count];
        Array.Fill(order, -1);
        int[] low = new int[count];
        bool[] onStack = new bool[count];
        var tarjanStack = new Stack<int>();
        var work = new Stack<(int Node, int NextEdge)>();
        var components = new List<int[]>();
        int counter = 0;

        for (int root = 0; root < count; root++)
        {
            if (order[root] != -1)
            {
                continue;
            }

            order[root] = low[root] = counter++;
            tarjanStack.Push(root);
            onStack[root] = true;
            work.Push((root, 0));

            while (work.Count > 0)
            {
                (int node, int nextEdge) = work.Pop();
                if (nextEdge < edges[node].Length)
                {
                    work.Push((node, nextEdge + 1));
                    int target = edges[node][nextEdge];
                    if (order[target] == -1)
                    {
                        order[target] = low[target] = counter++;
                        tarjanStack.Push(target);
                        onStack[target] = true;
                        work.Push((target, 0));
                    }
                    else if (onStack[target])
                    {
                        low[node] = Math.Min(low[node], order[target]);
                    }

                    continue;
                }

                if (low[node] == order[node])
                {
                    var members = new List<int>();
                    int member;
                    do
                    {
                        member = tarjanStack.Pop();
                        onStack[member] = false;
                        members.Add(member);
                    }
                    while (member != node);

                    members.Sort();
                    components.Add([.. members]);
                }

                if (work.Count > 0)
                {
                    int parent = work.Peek().Node;
                    low[parent] = Math.Min(low[parent], low[node]);
                }
            }
        }

        return components;
    }
}
