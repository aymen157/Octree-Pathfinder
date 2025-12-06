# Usage example

```cs
// getting the scene and its bounds
var targetScene = GameObject.Find("xyzrgb_dragon").transform;
var bounds = IrradiancePlacer.CalculateLocalBounds(targetScene, false, true, false, false);
// expanding bounds by a small margin to allow agents to fly/walk more freely
bounds.Expand(300);
// Building Sparsed voxels octree encapsulating geometry inside the bounds
var root = IrradiancePlacer.BuildOctree(bounds, layerMask: 1 << targetScene.gameObject.layer, maxDepth: 4);
// Performing pathfinding on the SVO
var start = GameObject.Find("S1").transform.position;
var end = GameObject.Find("S2").transform.position;
path = oc.FindPath(start, end); // list<vector3> of the points connecting path lines
```
# Vizualisation Example

```cs
private void OnDrawGizmos()
{
    if (path != null) // visualize path
        for (int i = 0; i < path.Count - 1; i++)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(path[i], path[i + 1]);
        }

    if (root != null) // visualize octree
        DrawVoxel(root);
}
private void DrawVoxel(Voxel voxel)
{
    Gizmos.color = voxel.colliding ? Color.red : Color.green;
    if (!voxel.colliding && voxel.IsLeaf) Gizmos.DrawWireCube(voxel.bounds.center, voxel.bounds.size);

    if (!voxel.IsLeaf)
    {
        foreach (var child in voxel.children)
        {
            if (child != null)
                DrawVoxel(child);
        }
    }
}
```
# Showcase

As an example, we use the `xyzrgb_dragon` model as our level/map, and we place 2 boid sphere agents.

<div align="center">
  <img src="media/demo_model.png" width="400" alt="Demo model">
</div>

### Scene Videos (GIFs)

We place the dragon as the level, and spheres as our end/start points. (see code above)
<div align="center">
  <img src="media/demo_scene.gif" width="400" alt="Demo Scene">
</div>

**SVO**  
<div align="center">
  <img src="media/svo_leaves.gif" width="400" alt="SVO Leaves">
  <img src="media/svo_leaves_colliding.gif" width="400" alt="SVO Leaves Colliding">
  <img src="media/svo_leaves_free.gif" width="400" alt="SVO Leaves Free">
  <br>
  <em>From left to right: all leaf voxels of the octree (colliding + non-colliding), only the colliding voxels, only the free voxels.</em>
</div>


### Path Results for Sphere Agents
<div align="center">
  <img src="media/eg0.gif" width="300" alt="Example 0">
  <img src="media/eg1.png" width="300" alt="Demo result 2">
  <img src="media/eg2.png" width="300" alt="Demo result 3">
</div>


### Performance
This was more of an experimentation.
Performance can be pretty hefty for a realtime application usage (like per frame or throttled game agent AI)
But then it depends on the Heuristic of the A* (which is by default the Euclidean distance, which is more accurate but slower than Manhattan distance, which is more accurate but slower than Squared Euclidean distance) (all the heuristics are in the cs file)
For instance, on an i7 12k, per path: Euc ~ on avg 20ms, Manh ~ 10ms, SqEuc ~ 1ms, on a 800meter distance between start and end point, with an octree of startSize=64 and depth=4, and a very complex fully fledged production ready level geometry
This is not multithreaded/parallelized by the way. so you can squeeze out more performance out of it.
In the case of multi agent, we can also implement a GPU Compute Shader in which we batch dispatch the Agents points and can get multiple paths in microseconds (for example in a roguelike where there are hundreds of flying monsters) (or in real life controlling hundreds of drones that needs to go thru pre-modeled complex streets or buildings) but that's another thing.
Generally, if you do not care about optimal path, and you want to rather prioritize performance, a steering approach is better.

### License
Use it as you wish.


