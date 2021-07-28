using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RT.KitchenSink.Geometry;
using RT.Util.ExtensionMethods;
using RT.Util.Geometry;
using UnityEngine;

using Rnd = UnityEngine.Random;

public class VoronoiMazeModule : MonoBehaviour
{
    public KMBombInfo Bomb;
    public KMBombModule Module;
    public KMSelectable ModuleSelectable;
    public MeshRenderer Frame;
    public KMAudio Audio;

    public MeshRenderer LineTemplate;
    public TextMesh TextTemplate;
    public KMSelectable SelectableTemplate;
    public GameObject StatusLightParent;
    public GameObject[] Keys;

    public Color DefaultRoomColor;
    public Color PassableColor;
    public Color ImpassableColor;
    public Color[] KeyColors;
    public Color[] DefaultRoomColors;
    public Color[] CurrentRoomColors;
    public Color[] WallColors;
    public Color SolvedColor;
    public Color SolvedWallColor;

    const int NumRooms = 20;

    private static int _moduleIdCounter = 1;
    private int _moduleId;
    private int _curRoom;
    private bool _exploring;
    private int _keysCollected;
    private bool _isSolved;
    private bool _locked;
    private List<int> _keys;
    private (MeshRenderer mr, MeshFilter mf, MeshFilter highlightMf, KMSelectable sel, Vector3 label)[] _rooms;
    private (MeshRenderer renderer, bool isPassable, int roomA, int roomB)[] _edges;
    private Vector3[][] _highlightTris;

    void Awake()
    {
        _moduleId = _moduleIdCounter++;


        // ##
        // ## GENERATE VORONOI DIAGRAM
        // ## Generates 100 random Voronoi diagrams, then selects the one whose shortest edge is the longest
        // ##

        tryEverythingAgain:
        VoronoiDiagram bestVoronoi = null;
        double bestVoronoiLength = 0;
        PointD[] bestPoints = null;
        var rnd = new System.Random(Rnd.Range(0, int.MaxValue));

        for (var voronoiIter = 0; voronoiIter < 100 || bestPoints == null; voronoiIter++)
        {
            var points = new List<PointD>();
            while (points.Count < NumRooms)
            {
                var newPoint = new PointD(rnd.NextDouble(), rnd.NextDouble());
                if (points.Any(p => p.Distance(newPoint) < 1 / 128d))
                    continue;
                points.Add(newPoint);
            }
            var voronoi = VoronoiDiagram.GenerateVoronoiDiagram(points.ToArray(), 1, 1, VoronoiDiagramFlags.IncludeEdgePolygons);
            var shortestEdge = voronoi.Edges.Min(e => e.start.Distance(e.end));
            if (shortestEdge > bestVoronoiLength)
            {
                bestVoronoiLength = shortestEdge;
                bestVoronoi = voronoi;
                bestPoints = points.ToArray();
            }
        }

        var numRooms = bestPoints.Length;


        // ##
        // ## GENERATE MAZE
        // ##

        var q = new Queue<int>();
        var visited = new bool[numRooms];
        var startRoom = Rnd.Range(0, numRooms);
        visited[startRoom] = true;
        var activeEdges = new List<(int roomA, int roomB)>(bestVoronoi.Edges.Where(tup => tup.siteA == startRoom || tup.siteB == startRoom).Select(tup => (roomA: tup.siteA, roomB: tup.siteB)));
        var passableEdges = new List<(int roomA, int roomB)>();
        while (activeEdges.Count > 0)
        {
            var edgeIx = Rnd.Range(0, activeEdges.Count);
            var (roomA, roomB) = activeEdges[edgeIx];
            passableEdges.Add((roomA, roomB));
            var newRoom = visited[roomA] ? roomB : roomA;
            visited[newRoom] = true;
            activeEdges.AddRange(bestVoronoi.Edges.Where(tup => tup.siteA == newRoom || tup.siteB == newRoom).Select(tup => (roomA: tup.siteA, roomB: tup.siteB)));
            activeEdges.RemoveAll(tup => visited[tup.roomA] && visited[tup.roomB]);
        }


        // ##
        // ## DETERMINE DISTANCES BETWEEN ROOMS
        // ##

        var distances = new int?[numRooms * numRooms];
        for (var a = 0; a < numRooms; a++)
            distances[a + numRooms * a] = 0;
        foreach (var (roomA, roomB) in passableEdges)
        {
            distances[roomA + numRooms * roomB] = 1;
            distances[roomB + numRooms * roomA] = 1;
        }
        while (distances.Any(d => d == null))
            for (var oth = 0; oth < numRooms; oth++)
                foreach (var (roomA, roomB) in passableEdges)
                {
                    if (distances[oth + numRooms * roomB] == null && distances[oth + numRooms * roomA] != null)
                        distances[oth + numRooms * roomB] = distances[oth + numRooms * roomA] + 1;
                    if (distances[oth + numRooms * roomA] == null && distances[oth + numRooms * roomB] != null)
                        distances[oth + numRooms * roomA] = distances[oth + numRooms * roomB] + 1;
                }


        // ##
        // ## DECIDE LOCATIONS OF KEYS AND STARTING POSITION
        // ## (pretend we’re placing 4 keys, then make the first one the starting position)
        // ##
        _keys = new List<int>();
        var iter = 0;
        tryAgain:
        iter++;
        if (iter > 100)
            goto tryEverythingAgain;
        _keys.Clear();
        _keys.Add(Rnd.Range(0, numRooms));
        while (_keys.Count < 4)
        {
            var otherKeys = Enumerable.Range(0, numRooms).Where(room => _keys.All(key => distances[room + numRooms * key] >= 3)).ToArray();
            if (otherKeys.Length == 0)
                goto tryAgain;
            _keys.Add(otherKeys[Rnd.Range(0, otherKeys.Length)]);
        }
        _curRoom = _keys[0];
        _keys.RemoveAt(0);



        // ##
        // ## GENERATE GAMEOBJECTS FOR ROOMS
        // ## (Includes a polygon (the room itself), a collider, and a highlight for each room. The highlight consists of triangles pointing at other rooms)
        // ##

        _rooms = new (MeshRenderer mr, MeshFilter mf, MeshFilter highlightMf, KMSelectable sel, Vector3 label)[numRooms];
        _highlightTris = new Vector3[numRooms * numRooms][];
        var children = new List<KMSelectable>();
        for (var roomIx = 0; roomIx < bestPoints.Length; roomIx++)
        {
            var polygon = bestVoronoi.Polygons[roomIx];

            // Label (not visible in game)
            var labelPoint = convertPointToVector(polygon == null ? bestPoints[roomIx] : polygon.GetLabelPoint(.005), .0102f);
            /*
            if (Application.isEditor)
            {
                var label = Instantiate(TextTemplate, transform);
                label.gameObject.name = $"Label {(char) ('A' + roomIx)}";
                label.transform.localPosition = labelPoints[roomIx];
                label.text = $"{roomIx}";
            }
            /**/

            var selectable = Instantiate(SelectableTemplate, transform);
            children.Add(selectable);
            var meshFilter = selectable.GetComponent<MeshFilter>();

            // Room and collider
            var roomMeshTris = new List<int>();
            var colliderMeshTris = new List<Vector3>();
            var edgeLengths = new List<double>();
            for (var i = 0; i < polygon.Vertices.Count; i++)
            {
                var j = (i + 1) % polygon.Vertices.Count;

                const float colliderTop = .01001f;
                const float colliderBottom = 0;
                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[i], colliderBottom));
                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[i], colliderTop));
                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[j], colliderTop));

                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[j], colliderTop));
                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[j], colliderBottom));
                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[i], colliderBottom));

                if (i != 0 && j != 0)
                {
                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[0], colliderTop));
                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[j], colliderTop));
                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[i], colliderTop));

                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[0], colliderBottom));
                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[i], colliderBottom));
                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[j], colliderBottom));

                    roomMeshTris.Add(0);
                    roomMeshTris.Add(j);
                    roomMeshTris.Add(i);
                }

                edgeLengths.Add(polygon.Vertices[j].Distance(polygon.Vertices[i]));
            }
            var colliderMesh = new Mesh();
            colliderMesh.vertices = colliderMeshTris.ToArray();
            colliderMesh.triangles = Enumerable.Range(0, colliderMeshTris.Count).ToArray();
            colliderMesh.normals = Enumerable.Repeat(new Vector3(0, 1, 0), colliderMeshTris.Count).ToArray();
            selectable.gameObject.GetComponent<MeshCollider>().sharedMesh = colliderMesh;

            var roomMesh = new Mesh();
            roomMesh.vertices = roomMeshTris.Select(pIx => convertPointToVector(polygon.Vertices[pIx], 0)).ToArray();
            roomMesh.triangles = Enumerable.Range(0, roomMeshTris.Count).ToArray();
            roomMesh.normals = Enumerable.Repeat(new Vector3(0, 1, 0), roomMeshTris.Count).ToArray();
            var totalLength = edgeLengths.Sum();
            Vector2 uvVector(double proportion) => new Vector2(Mathf.Cos(2 * Mathf.PI * (float) proportion), Mathf.Sin(2 * Mathf.PI * (float) proportion)) * .5f + new Vector2(.5f, .5f);
            roomMesh.uv = roomMeshTris.Select(ix => uvVector(edgeLengths.Take(ix).Sum() / totalLength)).ToArray();
            meshFilter.sharedMesh = roomMesh;

            _rooms[roomIx] = (selectable.GetComponent<MeshRenderer>(), selectable.GetComponent<MeshFilter>(), selectable.Highlight.GetComponent<MeshFilter>(), selectable, labelPoint);
            _rooms[roomIx].mr.material.color = DefaultRoomColor;
            selectable.OnInteract = RoomPressed(roomIx);
        }

        Destroy(LineTemplate.gameObject);
        Destroy(TextTemplate.gameObject);
        Destroy(SelectableTemplate.gameObject);

        ModuleSelectable.Children = children.ToArray();
        ModuleSelectable.UpdateChildren();


        // ##
        // ## GENERATE GAMEOBJECTS FOR EDGES
        // ## (dividing lines between rooms in the maze)
        // ##

        _edges = new (MeshRenderer renderer, bool isPassable, int roomA, int roomB)[bestVoronoi.Edges.Count];
        for (var ix = 0; ix < bestVoronoi.Edges.Count; ix++)
        {
            var (start, end, roomA, roomB) = bestVoronoi.Edges[ix];
            var isPassable = passableEdges.Any(tup => (tup.roomA == roomA && tup.roomB == roomB) || (tup.roomB == roomA && tup.roomA == roomB));
            var midPoint = convertPoint((start + end) / 2);
            var length = start.Distance(end) * cf;
            var edge = Instantiate(LineTemplate, transform);
            edge.gameObject.name = $"Edge {roomA} → {roomB}";
            edge.transform.localPosition = new Vector3((float) midPoint.X, isPassable ? .0102f : .0103f, (float) midPoint.Y);
            edge.transform.localEulerAngles = new Vector3(90, (float) (90 - Math.Atan2(end.Y - start.Y, end.X - start.X) * 180 / Math.PI), 0);
            edge.transform.localScale = new Vector3(.001f, (float) length + .001f, .1f);
            _edges[ix] = (edge, isPassable, roomA, roomB);


            // Highlight
            foreach (var way in new[] { false, true })
            {
                // Figure out which room is “left” of the line
                var vA = bestPoints[roomA] - start;
                var vM = end - start;
                var (tStart, tEnd) = (Math.Atan2(vA.Y * vM.X - vA.X * vM.Y, vA.X * vM.X + vA.Y * vM.Y) < 0 ^ way) ? (end, start) : (start, end);

                var nX = (tEnd - tStart) / (tEnd - tStart).Distance();
                var nY = nX.Rotated(Math.PI / 2);
                var mid = (tStart + tEnd) / 2;

                _highlightTris[way ? (roomA + numRooms * roomB) : (roomB + numRooms * roomA)] = new Vector3[]
                {
                    convertPointToVector(mid - .02 * nY, 0),
                    convertPointToVector(mid - .02 * nX + .02 * nY, 0),
                    convertPointToVector(mid + .02 * nX + .02 * nY, 0)
                };
            }
        }

        for (var i = 0; i < Keys.Length; i++)
            Keys[i].transform.localPosition = _rooms[_keys[i]].label;

        _exploring = true;
        StartCoroutine(UpdateVisualsLater());
    }

    private IEnumerator UpdateVisualsLater(float? delay = null)
    {
        _locked = true;
        yield return delay == null ? null : new WaitForSeconds(delay.Value);
        UpdateVisuals();
        _locked = false;
    }

    private void SetHighlightMesh(MeshFilter mf, Vector3[] tris)
    {
        var mesh = new Mesh();
        if (tris != null)
        {
            mesh.vertices = tris;
            mesh.triangles = Enumerable.Range(0, tris.Length).ToArray();
            mesh.normals = Enumerable.Repeat(new Vector3(0, 1, 0), tris.Length).ToArray();
        }

        mf.sharedMesh = mesh;
        var child = mf.transform.Find("Highlight(Clone)");
        var filter = child?.GetComponent<MeshFilter>();
        if (filter != null)
            filter.sharedMesh = mesh;
    }

    private KMSelectable.OnInteractHandler RoomPressed(int roomIx)
    {
        return delegate
        {
            _rooms[roomIx].sel.AddInteractionPunch(.5f);
            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, _rooms[roomIx].sel.transform);

            if (_isSolved)
                return false;

            if (_exploring)
            {
                _exploring = false;
                UpdateVisuals();
                return false;
            }

            var edgeIx = _edges.IndexOf(e => (e.roomA == roomIx && e.roomB == _curRoom) || (e.roomB == roomIx && e.roomA == _curRoom));
            if (edgeIx == -1)
                return false;

            var (renderer, isPassable, roomA, roomB) = _edges[edgeIx];
            if (!isPassable)
            {
                Debug.Log($"[Voronoi Maze #{_moduleId}] You attempted to go from Room {_curRoom + 1} to Room {roomIx + 1} but there’s a wall there. Strike.");
                Module.HandleStrike();
                _exploring = true;
                StartCoroutine(UpdateVisualsLater(1.5f));
                return false;
            }

            _curRoom = roomIx;
            if (_curRoom == _keys[_keysCollected])
            {
                _keysCollected++;
                if (_keysCollected >= _keys.Count)
                {
                    _isSolved = true;
                    Module.HandlePass();
                }
            }
            UpdateVisuals();

            return false;
        };
    }

    private void UpdateVisuals()
    {
        foreach (var (renderer, isPassable, roomA, roomB) in _edges)
        {
            renderer.material.color = _isSolved ? SolvedWallColor : _exploring ? (isPassable ? PassableColor : ImpassableColor) : WallColors[_keysCollected];
            renderer.transform.localPosition = new Vector3(renderer.transform.localPosition.x, isPassable || !_exploring ? .0102f : .0103f, renderer.transform.localPosition.z);
        }

        for (var roomIx = 0; roomIx < _rooms.Length; roomIx++)
        {
            _rooms[roomIx].mr.material.color =
                _isSolved ? SolvedColor :
                (_exploring && _keys.Contains(roomIx)) ? KeyColors[_keys.IndexOf(roomIx)] :
                _exploring ? DefaultRoomColor :
                roomIx == _curRoom ? CurrentRoomColors[_keysCollected] : DefaultRoomColors[_keysCollected];

            SetHighlightMesh(_rooms[roomIx].highlightMf,
                _exploring ? Enumerable.Range(0, NumRooms)
                    .Where(r => _edges.Any(e => e.isPassable && ((e.roomA == roomIx && e.roomB == r) || (e.roomB == roomIx && e.roomA == r))))
                    .SelectMany(adj => _highlightTris[roomIx + NumRooms * adj])
                    .ToArray() :
                _highlightTris[_curRoom + NumRooms * roomIx]);
        }

        for (var i = 0; i < 3; i++)
            Keys[i].SetActive(_exploring && i >= _keysCollected);

        StatusLightParent.SetActive(!_exploring);
        StatusLightParent.transform.localPosition = _rooms[_curRoom].label;
        Frame.material.color = _isSolved ? SolvedColor : _exploring ? ImpassableColor : CurrentRoomColors[_keysCollected];
    }

    const double cf = .0835 / .5;
    PointD convertPoint(PointD orig) => (orig - new PointD(.5, .5)) * cf;
    Vector3 convertPointToVector(PointD orig, float y)
    {
        var p = convertPoint(orig);
        return new Vector3((float) p.X, y, (float) p.Y);
    }
}
