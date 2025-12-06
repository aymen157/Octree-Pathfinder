

using System;
using System.Collections.Generic;
using UnityEngine;
using static IrradiancePlacer;
public partial class IrradiancePlacer
{
    
    /// <summary>
    /// Calculate local bounds of an object (returns a Bounds whose center is local to obj.position).
    /// Extended to include Terrain children.
    /// </summary>
    public static Bounds CalculateLocalBounds(UnityEngine.Component _obj,
        bool resetRotation = true,
        bool excludeParticleSystems = true,
        bool excludeTerrains = true,
        bool ignoreTriggerColliders = true
        )
    {
        Transform obj = _obj.transform;
        Quaternion currentRotation = obj.rotation;

        if (resetRotation)
            obj.rotation = Quaternion.Euler(0f, 0f, 0f);

        // start with a zero-sized bounds at object's position (world-space)
        Bounds bounds = new Bounds(obj.position, Vector3.zero);

        // Include all renderers
        foreach (Renderer renderer in obj.gameObject.GetComponentsInChildren<Renderer>())
        {
            if (excludeParticleSystems && renderer.GetComponent<ParticleSystem>() != null)
                continue;

            if (ignoreTriggerColliders)
            {
                var collider = renderer.GetComponent<Collider>();
                if (collider != null && collider.isTrigger)
                    continue;
            }

            // renderer.bounds are in world-space, Encapsulate handles empty initial bounds correctly
            bounds.Encapsulate(renderer.bounds);
        }

        // Include terrain children (Terrain doesn't always present a standard Renderer)
        if (!excludeTerrains)
        {
            foreach (Terrain terrain in obj.gameObject.GetComponentsInChildren<Terrain>())
            {
                if (terrain == null || terrain.terrainData == null) continue;

                if (ignoreTriggerColliders)
                {
                    var tcol = terrain.GetComponent<TerrainCollider>();
                    if (tcol != null && tcol.isTrigger)
                        continue;
                }

                // TerrainData.bounds is local to terrain origin (0,0,0)
                Bounds tBounds = terrain.terrainData.bounds;
                // Convert to world space
                tBounds.center += terrain.GetPosition();

                bounds.Encapsulate(tBounds);
            }
        }

        // convert bounds center to be local relative to obj.position (so caller gets a local-space bounds)
        Vector3 localCenter = bounds.center - obj.position;
        bounds.center = localCenter;

        if (resetRotation)
            obj.rotation = currentRotation;

        return bounds;
    }
    
    public class Voxel // class not struct. avoids copying of millions of voxels.
    {
        public Bounds bounds;
        public int depth;
        public bool colliding;
        public Voxel parent;
        public Voxel[] children; // 8 children for octree, null if leaf

        public bool IsLeaf => children == null;
    }
    /// <summary>
    /// Counts the total number of voxels in the tree starting from the given root.
    /// </summary>
    public static int CountTree(Voxel root, bool onlyLeaves = true)
    {
        if (root == null) return 0;

        // Start with the root itself
        int count = 1;

        // If not a leaf, recurse into children
        if (!root.IsLeaf)
        {
            foreach (var child in root.children)
            {
                if (onlyLeaves && !child.IsLeaf)
                    continue;
                count += CountTree(child);
            }
        }

        return count;
    }

    /// <summary>
    /// Generates a hierarchical octree of voxels inside the bounds.
    /// Returns the root voxel containing the entire hierarchy.
    /// </summary>
    public static Voxel BuildOctree(
        Bounds bounds,
        float startVoxelSize = 64f,
        int maxDepth = 4,
        LayerMask layerMask = default)
    {
        int nx = Mathf.CeilToInt(bounds.size.x / startVoxelSize);
        int ny = Mathf.CeilToInt(bounds.size.y / startVoxelSize);
        int nz = Mathf.CeilToInt(bounds.size.z / startVoxelSize);
        int estimatedCount = nx * ny * nz * 2; // Rough estimate

        var rootVoxels = new List<Voxel>(estimatedCount);
        SubdivideVoxelAligned(bounds, startVoxelSize, maxDepth, 0, layerMask, rootVoxels);

        // Create a root container voxel
        return new Voxel
        {
            bounds = bounds,
            depth = -1,
            colliding = false,
            children = rootVoxels.ToArray()
        };
    }

    private static void SubdivideVoxelAligned(
    Bounds bounds,
    float voxelSize,
    int maxDepth,
    int depth,
    LayerMask layerMask,
    List<Voxel> output,
    Voxel parent = null) // added parent parameter
    {
        // Calculate how many voxels fit along each axis
        int nx = Mathf.CeilToInt(bounds.size.x / voxelSize);
        int ny = Mathf.CeilToInt(bounds.size.y / voxelSize);
        int nz = Mathf.CeilToInt(bounds.size.z / voxelSize);

        Vector3 start = bounds.min;
        float halfSize = voxelSize * 0.5f;
        start.x += halfSize;
        start.y += halfSize;
        start.z += halfSize;

        Vector3 voxelSizeVec = new Vector3(voxelSize, voxelSize, voxelSize);
        Vector3 extents = Vector3.one * halfSize;
        bool isMaxDepth = depth >= maxDepth;

        for (int i = 0; i < nx; i++)
        {
            float x = start.x + i * voxelSize;
            for (int j = 0; j < ny; j++)
            {
                float y = start.y + j * voxelSize;
                for (int k = 0; k < nz; k++)
                {
                    float z = start.z + k * voxelSize;
                    Vector3 center = new Vector3(x, y, z);

                    bool intersects = Physics.CheckBox(center, extents, Quaternion.identity, layerMask);

                    if (!intersects || isMaxDepth)
                    {
                        // Leaf node
                        output.Add(new Voxel
                        {
                            bounds = new Bounds(center, voxelSizeVec),
                            depth = depth,
                            colliding = intersects,
                            children = null,
                            parent = parent // set parent
                        });
                        continue;
                    }

                    // Subdivide into 8 children
                    var children = new List<Voxel>(8);
                    float childSize = halfSize;
                    float quarterSize = voxelSize * 0.25f;

                    // Unrolled loop for 8 octants
                    SubdivideOctant(center, -quarterSize, -quarterSize, -quarterSize, childSize, maxDepth, depth + 1, layerMask, children, parent: null);
                    SubdivideOctant(center, quarterSize, -quarterSize, -quarterSize, childSize, maxDepth, depth + 1, layerMask, children, parent: null);
                    SubdivideOctant(center, -quarterSize, quarterSize, -quarterSize, childSize, maxDepth, depth + 1, layerMask, children, parent: null);
                    SubdivideOctant(center, quarterSize, quarterSize, -quarterSize, childSize, maxDepth, depth + 1, layerMask, children, parent: null);
                    SubdivideOctant(center, -quarterSize, -quarterSize, quarterSize, childSize, maxDepth, depth + 1, layerMask, children, parent: null);
                    SubdivideOctant(center, quarterSize, -quarterSize, quarterSize, childSize, maxDepth, depth + 1, layerMask, children, parent: null);
                    SubdivideOctant(center, -quarterSize, quarterSize, quarterSize, childSize, maxDepth, depth + 1, layerMask, children, parent: null);
                    SubdivideOctant(center, quarterSize, quarterSize, quarterSize, childSize, maxDepth, depth + 1, layerMask, children, parent: null);

                    // Create parent voxel for these children
                    Voxel voxel = new Voxel
                    {
                        bounds = new Bounds(center, voxelSizeVec),
                        depth = depth,
                        colliding = true,
                        children = children.ToArray(),
                        parent = parent // assign parent
                    };

                    // Assign this voxel as parent for all children
                    foreach (var child in voxel.children)
                    {
                        child.parent = voxel;
                    }

                    output.Add(voxel);
                }
            }
        }
    }

    private static void SubdivideOctant(
        Vector3 parentCenter,
        float offsetX, float offsetY, float offsetZ,
        float childSize,
        int maxDepth,
        int depth,
        LayerMask layerMask,
        List<Voxel> output,
        Voxel parent = null) // added parent parameter
    {
        Vector3 childCenter = new Vector3(
            parentCenter.x + offsetX,
            parentCenter.y + offsetY,
            parentCenter.z + offsetZ
        );

        Bounds childBounds = new Bounds(childCenter, Vector3.one * childSize);
        SubdivideVoxelAligned(childBounds, childSize, maxDepth, depth, layerMask, output, parent);
    }


    /// <summary>
    /// Flattens the hierarchical octree (SVO - Sparse Voxel Octree) into a list of leaf voxels.
    /// This is useful for irradiance (placing light probes)
    /// </summary>
    public static List<Voxel> FlattenSVO(
        Voxel root,
        bool pruneVoxelsWithNoIntersections = false,
        bool pruneFinalVoxelsWithIntersections = false)
    {
        var result = new List<Voxel>(1024); // Start with reasonable capacity
        FlattenRecursive(root, pruneVoxelsWithNoIntersections, pruneFinalVoxelsWithIntersections, result);
        return result;
    }

    private static void FlattenRecursive(
        Voxel node,
        bool pruneNoIntersections,
        bool pruneFinalIntersections,
        List<Voxel> output)
    {
        if (node == null) return;

        // If it's a leaf node, apply pruning rules
        if (node.children == null)
        {
            if ((node.colliding && pruneFinalIntersections) ||
                (!node.colliding && pruneNoIntersections))
                return;

            output.Add(node);
        }
        else
        {
            // Not a leaf - recurse into children
            var children = node.children;
            for (int i = 0; i < children.Length; i++)
            {
                FlattenRecursive(children[i], pruneNoIntersections, pruneFinalIntersections, output);
            }
        }
    }

    // Legacy compatibility method
    public static List<Voxel> Voxelize(
        Bounds bounds,
        float startVoxelSize = 64f,
        int maxDepth = 4,
        bool pruneVoxelsWithNoIntersections = false,
        bool pruneFinalVoxelsWithIntersections = false,
        LayerMask layerMask = default)
    {
        var octree = BuildOctree(bounds, startVoxelSize, maxDepth, layerMask);
        return FlattenSVO(octree, pruneVoxelsWithNoIntersections, pruneFinalVoxelsWithIntersections);
    }
}

public class OctreePathfinder
{
    private EdgeCornerGraph graph;
    private Voxel root;

    public OctreePathfinder(Voxel root)
    {
        this.root = root;
        this.graph = new EdgeCornerGraph(root);
    }

    public List<Vector3> FindPath(Vector3 start, Vector3 end)
    {
        var startLeaf = FindLeaf(root, start);
        var endLeaf = FindLeaf(root, end);

        if (startLeaf == null || endLeaf == null || startLeaf.colliding || endLeaf.colliding)
            return null;

        var startNode = graph.GetOrCreateTempNode(start, startLeaf);
        var endNode = graph.GetOrCreateTempNode(end, endLeaf);

        if (!graph.AreConnected(startNode, endNode))
        {
            graph.RemoveTempNodes();
            return null;
        }

        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        var path = AStar(startNode, endNode);
        Debug.Log($"{(float)sw.ElapsedTicks / TimeSpan.TicksPerMillisecond} ");
        graph.RemoveTempNodes();

        return path;
    }

    private List<Vector3> AStar(GraphNode start, GraphNode goal)
    {
        var openSet = new FastPriorityQueue<GraphNode>();
        var nodeData = new Dictionary<GraphNode, NodeSearchData>();
        var closed = new HashSet<GraphNode>();

        nodeData[start] = new NodeSearchData { g = 0, f = Heuristic(start.position, goal.position), parent = null };
        openSet.Enqueue(start, nodeData[start].f);

        while (openSet.Count > 0)
        {
            var current = openSet.Dequeue();

            if (current == goal)
                return ReconstructPath(nodeData, goal);

            closed.Add(current);

            foreach (var neighbor in current.neighbors)
            {
                if (closed.Contains(neighbor)) continue;

                if (!nodeData.TryGetValue(neighbor, out var neighborData))
                {
                    neighborData = new NodeSearchData
                    {
                        g = float.PositiveInfinity,
                        f = float.PositiveInfinity,
                        h = Heuristic(neighbor.position, goal.position),
                        parent = null
                    };
                    nodeData[neighbor] = neighborData;
                }

                var tentativeG = nodeData[current].g + Heuristic(current.position, neighbor.position);
                if (tentativeG < nodeData[neighbor].g)
                {
                    nodeData[neighbor].g = tentativeG;
                    nodeData[neighbor].f = tentativeG + neighborData.h;
                    nodeData[neighbor].parent = current;
                    openSet.Enqueue(neighbor, nodeData[neighbor].f);
                }
            }
        }

        return null; // no path found
    }

    private List<Vector3> ReconstructPath(Dictionary<GraphNode, NodeSearchData> nodeData, GraphNode goal)
    {
        var path = new List<Vector3>();
        var current = goal;

        while (current != null)
        {
            path.Add(current.position);
            current = nodeData[current].parent;
        }

        path.Reverse();
        return path;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private float Heuristic(Vector3 a, Vector3 b) => (a - b).sqrMagnitude; // sqr euclidean
    //private float Heuristic(Vector3 a, Vector3 b) => (a - b).magnitude; // euclidean
    //private float Heuristic(Vector3 a, Vector3 b) => // manhattan
    //    Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z);
    //private float Heuristic(Vector2 a, Vector2 b) => // chebychev
    //    Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
    //private float Heuristic(Vector3 a, Vector3 b)
    //{
    //    Vector3 d = a - b;
    //    float sqr = d.x * d.x + d.y * d.y + d.z * d.z;
    //    return FastSqrt(sqr);
    //}

    // Example: Quake III fast inverse sqrt
    private float FastSqrt(float x)
    {
        return x * FastInvSqrt(x);
    }

    private float FastInvSqrt(float x)
    {
        unsafe
        {
            float xhalf = 0.5f * x;
            int i = *(int*)&x;
            i = 0x5f3759df - (i >> 1);
            float y = *(float*)&i;
            return y * (1.5f - xhalf * y * y);
        }
    }


    private Voxel FindLeaf(Voxel root, Vector3 point)
    {
        Voxel current = root;
        while (current != null)
        {
            if (!current.bounds.Contains(point))
                return null;

            if (current.IsLeaf)
                return current;

            Voxel next = null;
            if (current.children != null)
            {
                foreach (var child in current.children)
                {
                    if (child != null && child.bounds.Contains(point))
                    {
                        next = child;
                        break;
                    }
                }
            }

            if (next == null) return current; // fallback
            current = next;
        }

        return null;
    }


    private class NodeSearchData
    {
        public float g;
        public float f;

        public float h; // heuristic to goal
        public GraphNode parent;
    }
}
public class EdgeCornerGraph
{
    private Dictionary<Vector3Int, GraphNode> nodes;
    private Dictionary<(Vector3Int, Vector3Int), bool> edges;
    private Dictionary<GraphNode, int> connectedSets;
    private List<GraphNode> tempNodes;
    private Voxel root;

    public EdgeCornerGraph(Voxel root)
    {
        this.root = root;
        nodes = new Dictionary<Vector3Int, GraphNode>();
        edges = new Dictionary<(Vector3Int, Vector3Int), bool>();
        connectedSets = new Dictionary<GraphNode, int>();
        tempNodes = new List<GraphNode>();

        BuildGraph(root);
        ComputeConnectedSets();
    }

    private void BuildGraph(Voxel root)
    {
        var leaves = new List<Voxel>();
        CollectLeaves(root, leaves);

        // Create nodes for all corners of non-colliding leaves
        foreach (var leaf in leaves)
        {
            if (leaf.colliding) continue;

            var corners = GetCorners(leaf.bounds);
            foreach (var corner in corners)
            {
                GetOrCreateNode(corner);
            }
        }

        // Connect neighboring corners
        foreach (var leaf in leaves)
        {
            if (leaf.colliding) continue;

            var corners = GetCorners(leaf.bounds);

            // Connect corners within the same voxel (edges and faces)
            for (int i = 0; i < corners.Length; i++)
            {
                for (int j = i + 1; j < corners.Length; j++)
                {
                    // Check if they share an edge or face
                    int diff = 0;
                    if (Mathf.Abs(corners[i].x - corners[j].x) > 0.001f) diff++;
                    if (Mathf.Abs(corners[i].y - corners[j].y) > 0.001f) diff++;
                    if (Mathf.Abs(corners[i].z - corners[j].z) > 0.001f) diff++;

                    if (diff <= 2) // Edge or face neighbors
                    {
                        AddEdge(corners[i], corners[j]);
                    }
                }
            }
        }
    }

    private void CollectLeaves(Voxel node, List<Voxel> leaves)
    {
        if (node.IsLeaf)
        {
            leaves.Add(node);
            return;
        }

        if (node.children != null)
        {
            foreach (var child in node.children)
            {
                if (child != null)
                    CollectLeaves(child, leaves);
            }
        }
    }

    private Vector3[] GetCorners(Bounds bounds)
    {
        var min = bounds.min;
        var max = bounds.max;

        return new Vector3[]
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, max.y, max.z)
        };
    }

    private GraphNode GetOrCreateNode(Vector3 position)
    {
        var key = QuantizePosition(position);

        if (!nodes.TryGetValue(key, out var node))
        {
            node = new GraphNode(position);
            nodes[key] = node;
        }

        return node;
    }

    private void AddEdge(Vector3 from, Vector3 to)
    {
        var fromNode = GetOrCreateNode(from);
        var toNode = GetOrCreateNode(to);

        var key1 = (QuantizePosition(from), QuantizePosition(to));
        var key2 = (QuantizePosition(to), QuantizePosition(from));

        if (!edges.ContainsKey(key1) && !edges.ContainsKey(key2))
        {
            fromNode.neighbors.Add(toNode);
            toNode.neighbors.Add(fromNode);
            edges[key1] = true;
        }
    }

    public GraphNode GetOrCreateTempNode(Vector3 position, Voxel leaf)
    {
        // Create a temporary node at the position
        var node = new GraphNode(position);
        tempNodes.Add(node);

        // Connect only to the closest free corner nodes of the leaf
        var corners = GetCorners(leaf.bounds);
        GraphNode closestCorner = null;
        float closestDist = float.MaxValue;

        foreach (var corner in corners)
        {
            var cornerNode = GetOrCreateNode(corner);
            float dist = (corner - position).sqrMagnitude; // squared distance for efficiency
            if (dist < closestDist)
            {
                closestDist = dist;
                closestCorner = cornerNode;
            }
        }

        if (closestCorner != null)
        {
            node.neighbors.Add(closestCorner);
            closestCorner.neighbors.Add(node);
            if (connectedSets.TryGetValue(closestCorner, out int setId))
            {
                connectedSets[node] = setId;
            }
        }

        return node;
    }


    public void RemoveTempNodes()
    {
        foreach (var tempNode in tempNodes)
        {
            foreach (var neighbor in tempNode.neighbors)
            {
                neighbor.neighbors.Remove(tempNode);
            }
            connectedSets.Remove(tempNode);
        }
        tempNodes.Clear();
    }

    private void ComputeConnectedSets()
    {
        int setId = 0;
        var visited = new HashSet<GraphNode>();

        foreach (var node in nodes.Values)
        {
            if (!visited.Contains(node))
            {
                BFS(node, setId, visited);
                setId++;
            }
        }
    }

    private void BFS(GraphNode start, int setId, HashSet<GraphNode> visited)
    {
        var queue = new Queue<GraphNode>();
        queue.Enqueue(start);
        visited.Add(start);
        connectedSets[start] = setId;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var neighbor in current.neighbors)
            {
                if (!visited.Contains(neighbor))
                {
                    visited.Add(neighbor);
                    connectedSets[neighbor] = setId;
                    queue.Enqueue(neighbor);
                }
            }
        }
    }

    public bool AreConnected(GraphNode a, GraphNode b)
    {
        return connectedSets.TryGetValue(a, out int setA) &&
               connectedSets.TryGetValue(b, out int setB) &&
               setA == setB;
    }

    private Vector3Int QuantizePosition(Vector3 pos)
    {
        float scale = 10000f;
        return new Vector3Int(
            Mathf.RoundToInt(pos.x * scale),
            Mathf.RoundToInt(pos.y * scale),
            Mathf.RoundToInt(pos.z * scale)
        );
    }
}
public class GraphNode
{
    public Vector3 position;
    public List<GraphNode> neighbors;

    public GraphNode(Vector3 position)
    {
        this.position = position;
        this.neighbors = new List<GraphNode>();
    }
}
public class FastPriorityQueue<T> where T : class
{
    private List<(T item, float priority)> heap;

    public int Count => heap.Count;

    public FastPriorityQueue(int capacity = 256)
    {
        heap = new List<(T item, float priority)>(capacity);
    }

    public void Enqueue(T item, float priority)
    {
        heap.Add((item, priority));
        int ci = heap.Count - 1;
        HeapifyUp(ci);
    }

    public T Dequeue()
    {
        var frontItem = heap[0].item;
        var last = heap[heap.Count - 1];
        heap[0] = last;
        heap.RemoveAt(heap.Count - 1);
        HeapifyDown(0);
        return frontItem;
    }

    private void HeapifyUp(int ci)
    {
        while (ci > 0)
        {
            int pi = (ci - 1) / 2;
            if (heap[ci].priority >= heap[pi].priority) break;
            Swap(ci, pi);
            ci = pi;
        }
    }

    private void HeapifyDown(int ci)
    {
        int li = heap.Count - 1;
        while (true)
        {
            int lc = 2 * ci + 1;
            if (lc > li) break;
            int rc = lc + 1;
            int sc = (rc <= li && heap[rc].priority < heap[lc].priority) ? rc : lc;
            if (heap[ci].priority <= heap[sc].priority) break;
            Swap(ci, sc);
            ci = sc;
        }
    }

    private void Swap(int a, int b)
    {
        var tmp = heap[a];
        heap[a] = heap[b];
        heap[b] = tmp;
    }

    public void Clear()
    {
        heap.Clear();
    }
}

