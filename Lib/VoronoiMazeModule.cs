using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
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
    public Color[] RevealedWallColors;
    public Color SolvedColor;
    public Color StrikeColor;
    public Color SolvedWallColor;
    public Color SolvedRevealedWallColor;

    const int NumRooms = 10;

    private static int _moduleIdCounter = 1;
    private int _moduleId;
    private int _curRoom;
    private bool _exploring;
    private bool _striking;
    private int _keysCollected;
    private bool _isSolved;
    private bool _locked;
    private List<int> _keys;
    private (MeshRenderer mr, MeshFilter mf, MeshFilter highlightMf, KMSelectable sel)[] _rooms;
    private PointD[] _roomLabelPositions;
    private (MeshRenderer renderer, bool isPassable, int roomA, int roomB)[] _edges;
    private Vector3[][] _highlightTris;
    private bool[] _revealedWalls;
    private readonly Queue<(int from, int to, bool strike, string sound)> _animationQueue = new Queue<(int from, int to, bool strike, string sound)>();
    private VoronoiDiagram _voronoi;

    void Start()
    {
        _moduleId = _moduleIdCounter++;

        var rnd = new System.Random(Rnd.Range(0, int.MaxValue));


        // ##
        // ## GENERATE VORONOI DIAGRAM
        // ## Generates random Voronoi diagrams, then selects the first one where:
        // ##   • the shortest edge is at least .05 long;
        // ##   • the smallest room has at least .025 distance from the label point to every edge; and
        // ##   • no edge goes closer than .05 into the bottom-left corner
        // ##

        tryEverythingAgain:
        var pointsList = new List<PointD>();
        while (pointsList.Count < NumRooms)
        {
            var newPoint = new PointD(rnd.NextDouble(), rnd.NextDouble());
            if (pointsList.Any(p => p.Distance(newPoint) < 1 / 128d))
                continue;
            pointsList.Add(newPoint);
        }
        _voronoi = VoronoiDiagram.GenerateVoronoiDiagram(pointsList.ToArray(), 1, 1, VoronoiDiagramFlags.IncludeEdgePolygons);
        _roomLabelPositions = _voronoi.Polygons.Select(p => p.GetLabelPoint(.005)).ToArray();

        // Discard unwanted Voronoi diagrams
        if (_voronoi.Edges.Any(e => e.edge.Length < .05) ||
            _voronoi.Edges.Any(e => Math.Min(e.edge.Start.Distance(), e.edge.End.Distance()) < .05) ||
            _voronoi.Edges.Any(e => e.edge.Distance(_roomLabelPositions[e.siteA]) < .025 || e.edge.Distance(_roomLabelPositions[e.siteB]) < .025))
            goto tryEverythingAgain;

        var points = pointsList.ToArray();
        var numRooms = points.Length;
        _revealedWalls = new bool[_voronoi.Edges.Count];


        // ##
        // ## GENERATE MAZE from the serial number
        // ##
        var passable = new bool[_voronoi.Edges.Count];
        var visited = new bool[numRooms];

        // Obtain serial number
        var serialNumber = JObject.Parse(Bomb.QueryWidgets(KMBombInfo.QUERYKEY_GET_SERIAL_NUMBER, null).First())["serial"].ToString();
        // Convert from base-36
        var val = serialNumber.Aggregate(0UL, (p, n) => p * 36 + (ulong) (n <= '9' ? (n - '0') : (n - 'A' + 10)));

        // Function to successively modulo-divide the serial number to obtain rooms and edges
        var logMessages = new List<string>();
        int obtainModulo(List<int> options, bool isRoom)
        {
            var oldVal = val;
            var ix = (int) (val % (ulong) options.Count);
            var selectedOption = options[ix];
            val /= (ulong) options.Count;
            var json = new JObject
            {
                ["sel"] = selectedOption,
                ["arr"] = new JArray(options.Select(i => (object) i).ToArray()),
                ["old"] = oldVal.ToString(),
                ["ix"] = ix,
                ["new"] = val.ToString(),
                ["passable"] = new JArray(passable.SelectIndexWhere(b => b).Select(i => (object) i).ToArray()),
                ["visited"] = new JArray(visited.SelectIndexWhere(b => b).Select(i => (object) i).ToArray())
            };
            if (isRoom)
                json["isRoom"] = true;
            else
                json["isStep"] = true;
            logMessages.Add($"[Voronoi Maze #{_moduleId}] {json.ToString(Newtonsoft.Json.Formatting.None)}");
            return selectedOption;
        }

        // Find rooms that are on the fringe of the maze (bottom, right, top, left)
        var fringePolys = Enumerable.Range(0, _voronoi.Polygons.Length).Where(polyIx => _voronoi.Polygons[polyIx].Vertices.Any(p => p.Y == 0)).OrderBy(polyIx => _voronoi.Polygons[polyIx].Vertices.Where(p => p.Y == 0).Max(p => p.X))
            .Concat(Enumerable.Range(0, _voronoi.Polygons.Length).Where(polyIx => _voronoi.Polygons[polyIx].Vertices.Any(p => p.X == 1)).OrderBy(polyIx => _voronoi.Polygons[polyIx].Vertices.Where(p => p.X == 1).Max(p => p.Y)))
            .Concat(Enumerable.Range(0, _voronoi.Polygons.Length).Where(polyIx => _voronoi.Polygons[polyIx].Vertices.Any(p => p.Y == 1)).OrderByDescending(polyIx => _voronoi.Polygons[polyIx].Vertices.Where(p => p.Y == 1).Min(p => p.X)))
            .Concat(Enumerable.Range(0, _voronoi.Polygons.Length).Where(polyIx => _voronoi.Polygons[polyIx].Vertices.Any(p => p.X == 0)).OrderByDescending(polyIx => _voronoi.Polygons[polyIx].Vertices.Where(p => p.X == 0).Min(p => p.Y)))
            .Distinct().ToList();

        var startRoom = obtainModulo(fringePolys, true);
        visited[startRoom] = true;

        // When discovering a new room, returns the new edges
        IEnumerable<int> edgesFromRoom(int roomIx, PointD startPoint)
        {
            var poly = _voronoi.Polygons[roomIx];
            var startIx = poly.Vertices.IndexOf(startPoint);
            return Enumerable.Range(0, poly.Vertices.Count)
                .Select(i => poly.Vertices[(i + startIx) % poly.Vertices.Count])
                .ConsecutivePairs(true)
                .SelectMany(pair => _voronoi.Edges.SelectIndexWhere(e => (e.edge.Start == pair.Item1 && e.edge.End == pair.Item2) || (e.edge.Start == pair.Item2 && e.edge.End == pair.Item1)));
        }

        // Iteratively discover the remaining rooms and mark edges as passable
        var activeEdges = new List<int>(edgesFromRoom(startRoom, _voronoi.Polygons[startRoom].Vertices.Where(v => v.X == 0 || v.X == 1 || v.Y == 0 || v.Y == 1).First()));
        while (activeEdges.Count > 0)
        {
            var edgeIx = obtainModulo(activeEdges, false);
            var (edge, roomA, roomB) = _voronoi.Edges[edgeIx];
            var newRoom = visited[roomA] ? roomB : roomA;
            passable[edgeIx] = true;
            visited[newRoom] = true;

            activeEdges.AddRange(edgesFromRoom(newRoom, edge.Start).Where(e => !activeEdges.Contains(e)));
            activeEdges.RemoveAll(eIx => visited[_voronoi.Edges[eIx].siteA] && visited[_voronoi.Edges[eIx].siteB]);
        }


        // ##
        // ## DETERMINE DISTANCES BETWEEN ROOMS
        // ##

        var distances = new int?[numRooms * numRooms];
        for (var a = 0; a < numRooms; a++)
            distances[a + numRooms * a] = 0;
        for (var edgeIx = 0; edgeIx < passable.Length; edgeIx++)
            if (passable[edgeIx])
            {
                var (_, roomA, roomB) = _voronoi.Edges[edgeIx];
                distances[roomA + numRooms * roomB] = 1;
                distances[roomB + numRooms * roomA] = 1;
            }
        while (distances.Any(d => d == null))
            for (var oth = 0; oth < numRooms; oth++)
                for (var edgeIx = 0; edgeIx < passable.Length; edgeIx++)
                    if (passable[edgeIx])
                    {
                        var (_, roomA, roomB) = _voronoi.Edges[edgeIx];
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

        logMessages.Add($@"[Voronoi Maze #{_moduleId}] {new JObject
        {
            ["passable"] = new JArray(passable.SelectIndexWhere(b => b).Select(i => (object) i).ToArray()),
            ["visited"] = new JArray(visited.SelectIndexWhere(b => b).Select(i => (object) i).ToArray()),
            ["isFinal"] = true,
            ["keys"] = new JArray(_keys.Select(i => (object) i).ToArray())
        }.ToString(Newtonsoft.Json.Formatting.None)}");



        // ##
        // ## GENERATE GAMEOBJECTS FOR ROOMS
        // ## (Includes a polygon (the room itself), a collider, and a highlight for each room. The highlight consists of triangles pointing at other rooms)
        // ##

        _rooms = new (MeshRenderer mr, MeshFilter mf, MeshFilter highlightMf, KMSelectable sel)[numRooms];
        _highlightTris = new Vector3[numRooms * numRooms][];
        var children = new List<KMSelectable>();
        for (var roomIx = 0; roomIx < points.Length; roomIx++)
        {
            var polygon = _voronoi.Polygons[roomIx];
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

                const float colliderTop = 0;
                const float colliderBottom = -.01f;
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

            _rooms[roomIx] = (selectable.GetComponent<MeshRenderer>(), selectable.GetComponent<MeshFilter>(), selectable.Highlight.GetComponent<MeshFilter>(), selectable);
            _rooms[roomIx].mr.material.color = DefaultRoomColor;
            selectable.OnInteract = RoomPressed(roomIx);
        }

        Destroy(LineTemplate.gameObject);
        Destroy(SelectableTemplate.gameObject);

        ModuleSelectable.Children = children.ToArray();
        ModuleSelectable.UpdateChildren();


        // ##
        // ## GENERATE GAMEOBJECTS FOR EDGES
        // ## (dividing lines between rooms in the maze)
        // ##

        _edges = new (MeshRenderer renderer, bool isPassable, int roomA, int roomB)[_voronoi.Edges.Count];
        for (var eIx = 0; eIx < _voronoi.Edges.Count; eIx++)
        {
            var (edge, roomA, roomB) = _voronoi.Edges[eIx];
            var midPoint = convertPoint((edge.Start + edge.End) / 2);
            var length = edge.Start.Distance(edge.End) * cf;
            var edgeObj = Instantiate(LineTemplate, transform);
            edgeObj.gameObject.name = $"Edge {roomA} → {roomB}";
            edgeObj.transform.localPosition = new Vector3((float) midPoint.X, .0101f, (float) midPoint.Y);
            edgeObj.transform.localEulerAngles = new Vector3(90, (float) (90 - Math.Atan2(edge.End.Y - edge.Start.Y, edge.End.X - edge.Start.X) * 180 / Math.PI), 0);
            edgeObj.transform.localScale = new Vector3(.001f, (float) length + .001f, .1f);
            _edges[eIx] = (edgeObj, passable[eIx], roomA, roomB);


            // Highlight
            foreach (var way in new[] { false, true })
            {
                // Figure out which room is “left” of the line
                var vA = points[roomA] - edge.Start;
                var vM = edge.End - edge.Start;
                var (tStart, tEnd) = (Math.Atan2(vA.Y * vM.X - vA.X * vM.Y, vA.X * vM.X + vA.Y * vM.Y) < 0 ^ way) ? (edge.End, edge.Start) : (edge.Start, edge.End);

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


        var svgEdgeLabels = new StringBuilder();
        for (var edgeIx = 0; edgeIx < _voronoi.Edges.Count; edgeIx++)
        {
            var (edge, siteA, siteB) = _voronoi.Edges[edgeIx];
            var labelPos = (edge.Start + edge.End) / 2;
            labelPos.Y = 1 - labelPos.Y;
            svgEdgeLabels.Append($"<path class='edgepath edgepath-{edgeIx}' stroke-width='.0075' stroke='black' fill='none' d='M{edge.Start.X} {1 - edge.Start.Y} {edge.End.X} {1 - edge.End.Y}' />");
            svgEdgeLabels.Append($"<circle class='edgecircle edgecircle-{edgeIx}' cx='{labelPos.X}' cy='{labelPos.Y}' r='.03' fill='rgba(255, 255, 255, .7)' />");
            svgEdgeLabels.Append($"<text class='edgelabel edgelabel-{edgeIx}' x='{labelPos.X}' y='{labelPos.Y + .0175}' font-size='.05' font-family='Trebuchet MS'></text>");
        }
        var svgRooms = new StringBuilder();
        for (var roomIx = 0; roomIx < _rooms.Length; roomIx++)
        {
            svgRooms.Append($"<path class='room room-{roomIx}' d='M{_voronoi.Polygons[roomIx].Vertices.Select(p => $"{p.X} {1 - p.Y}").JoinString(" ")}z' fill='white' stroke='none' />");
            if (fringePolys.Contains(roomIx))
                svgRooms.Append($"<text class='roomlabel roomlabel-{roomIx}' x='{_roomLabelPositions[roomIx].X}' y='{1 - _roomLabelPositions[roomIx].Y + .0175}' font-size='.05' font-family='Trebuchet MS'></text>");
        }
        var keys = new StringBuilder();
        keys.Append($"<path class='key' fill='black' transform='translate({_roomLabelPositions[_keys[0]].X} {1 - _roomLabelPositions[_keys[0]].Y})' d='m -0.01004361,-0.02396981 c -0.0063034,-2.82e-5 -0.01164162,0.0046414 -0.01245215,0.01089258 -8.8791e-4,0.00684643 0.0039426,0.01311628683 0.01078906,0.0140039051 0.00221413,2.866649e-4 0.00446462,-2.491e-5 0.00651758,-9.02343997e-4 l 0.01635059,0.0212207089 0.0019072,0.0024746 0.0049502,-0.0038135 -7.6172e-4,-9.9023e-4 0.003126,-0.0024082 0.0014844,-0.0011445 -0.0022881,-0.0029707 -0.0014863,0.0011435 -0.003126,0.0024082 -0.0152041181,-0.01973433 C 0.00113489,-0.00555157 0.00200993,-0.0076483 0.00229721,-0.00986238 c 8.8791e-4,-0.00684643 -0.00394259,-0.0131163 -0.01078906,-0.01400391 -2.798e-5,-3.66e-6 -5.598e-5,-7.23e-6 -8.399e-5,-1.07e-5 -4.8701e-4,-5.97e-5 -9.7711e-4,-9.07e-5 -0.00146777,-9.282e-5 z m -1.2207e-4,0.005 c 3.2571e-4,-2.95e-6 6.5127e-4,1.53e-5 9.7461e-4,5.47e-5 1.856e-5,2.53e-6 3.712e-5,5.14e-6 5.566e-5,7.82e-6 0.00410755,5.3248e-4 0.00700582,0.0042938 0.00647364,0.0084014 -5.3248e-4,0.00410753 -0.00429379,0.00700579 -0.00840137,0.00647364 -0.0041075,-5.3251e-4 -0.0070057,-0.00429381 -0.0064736,-0.00840137 4.8102e-4,-0.0037132 0.003627,-0.0065028 0.0073711,-0.0065362 z' />");
        keys.Append($"<path class='key' fill='black' transform='translate({_roomLabelPositions[_keys[1]].X} {1 - _roomLabelPositions[_keys[1]].Y})' d='m -0.01004361,-0.02396981 c -0.0063034,-2.82e-5 -0.01164162,0.0046414 -0.01245215,0.01089258 -8.8791e-4,0.00684643 0.0039426,0.01311628683 0.01078906,0.0140039051 0.00221413,2.866649e-4 0.00446462,-2.491e-5 0.00651758,-9.02343997e-4 l 0.01635059,0.0212207089 0.0019072,0.0024746 0.0049502,-0.0038135 -7.6172e-4,-9.9023e-4 0.003126,-0.0024082 0.0014844,-0.0011445 -0.0022881,-0.0029707 -0.0014863,0.0011435 -0.003126,0.0024082 -0.0020254,-0.0026299 0.003125,-0.0024082 0.0014853,-0.00114453 -0.0022891,-0.0029707 -0.0014853,0.00114355 -0.003126,0.0024082 -0.0108886181,-0.01413275 C 0.00113489,-0.00555157 0.00200993,-0.0076483 0.00229721,-0.00986238 c 8.8791e-4,-0.00684643 -0.00394259,-0.0131163 -0.01078906,-0.01400391 -2.798e-5,-3.66e-6 -5.598e-5,-7.23e-6 -8.399e-5,-1.07e-5 -4.8701e-4,-5.97e-5 -9.7711e-4,-9.07e-5 -0.00146777,-9.282e-5 z m -1.2207e-4,0.005 c 3.2571e-4,-2.95e-6 6.5127e-4,1.53e-5 9.7461e-4,5.47e-5 1.856e-5,2.53e-6 3.712e-5,5.14e-6 5.566e-5,7.82e-6 0.00410755,5.3248e-4 0.00700582,0.0042938 0.00647364,0.0084014 -5.3248e-4,0.00410753 -0.00429379,0.00700579 -0.00840137,0.00647364 -0.0041075,-5.3251e-4 -0.0070057,-0.00429381 -0.0064736,-0.00840137 4.8102e-4,-0.0037132 0.003627,-0.0065028 0.0073711,-0.0065362 z' />");
        keys.Append($"<path class='key' fill='black' transform='translate({_roomLabelPositions[_keys[2]].X} {1 - _roomLabelPositions[_keys[2]].Y})' d='m -0.00997484,-0.02396973 a 0.0125,0.0125 0 0 0 -0.01245215,0.01089258 0.0125,0.0125 0 0 0 0.01078906,0.0140039089 0.0125,0.0125 0 0 0 0.00651758,-9.02343996e-4 l 0.01635059,0.0212207051 0.0019072,0.0024746 0.0049502,-0.0038135 -7.6172e-4,-9.9023e-4 0.003126,-0.0024082 0.0014844,-0.0011445 -0.0022881,-0.0029707 -0.0014863,0.0011435 -0.003126,0.0024082 -0.0020254,-0.0026299 0.003125,-0.0024082 0.0014853,-0.00114453 -0.0022891,-0.0029707 -0.0014853,0.00114355 -0.003126,0.0024082 L 0.0086941,0.00771289 0.01182008,0.00530469 0.01330543,0.00416016 0.01101637,0.00119043 0.00953102,0.00233399 0.00640504,0.00474219 -1.682e-4,-0.00379004 a 0.0125,0.0125 0 0 0 0.00253418,-0.00607226 0.0125,0.0125 0 0 0 -0.01078906,-0.01400391 0.0125,0.0125 0 0 0 -8.399e-5,-1.074e-5 0.0125,0.0125 0 0 0 -0.00146777,-9.278e-5 z m -1.2207e-4,0.005 a 0.0075,0.0075 0 0 1 9.7461e-4,5.469e-5 0.0075,0.0075 0 0 1 5.566e-5,7.82e-6 0.0075,0.0075 0 0 1 0.00647364,0.0084014 0.0075,0.0075 0 0 1 -0.00840137,0.00647364 0.0075,0.0075 0 0 1 -0.0064736,-0.00840137 0.0075,0.0075 0 0 1 0.0073711,-0.0065362 z' />");
        var svgId = $"{serialNumber}-{_moduleId}";
        Debug.Log($"[Voronoi Maze #{_moduleId}]=svg[Maze:]" +
            $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='-.25 -.01 1.5 1.02' text-anchor='middle' stroke-linecap='round'>" +
            $"<defs><marker id='marker-{svgId}' viewBox='0 0 10 10' refX='5' refY='5' markerWidth='4' markerHeight='4' orient='auto-start-reverse'><path d='M 0 0 L 10 5 L 0 10 z' /></marker></defs>" +
            $"<rect x='0' y='0' width='1' height='1' stroke-width='.01' stroke='black' fill='none' />" +
            $"{svgRooms}{svgEdgeLabels}{keys}" +
            $"<path class='arrow' d='' stroke='black' stroke-width='.025' fill='none' marker-end='url(#marker-{svgId})' />" +
            $"</svg>");
        foreach (var msg in logMessages)
            Debug.Log(msg);

        for (var keyIx = 0; keyIx < Keys.Length; keyIx++)
            Keys[keyIx].transform.localPosition = convertPointToVector(_roomLabelPositions[_keys[keyIx]], .0101f);

        _exploring = true;
        StatusLightParent.transform.localPosition = convertPointToVector(_roomLabelPositions[_curRoom], .01f);
        StartCoroutine(UpdateVisualsLater());
        StartCoroutine(AnimationQueue());
    }

    private IEnumerator AnimationQueue()
    {
        while (true)
        {
            if (_animationQueue.Count > 0)
            {
                var (from, to, isStrike, sound) = _animationQueue.Dequeue();
                var fromPoint = _roomLabelPositions[from];
                var toPoint = _roomLabelPositions[to];
                var edgeIx = _voronoi.Edges.IndexOf(e => (e.siteA == from && e.siteB == to) || (e.siteB == from && e.siteA == to));
                var (edge, siteA, siteB) = _voronoi.Edges[edgeIx];
                var mid = (edge.Start + edge.End) / 2;

                Audio.PlaySoundAtTransform(sound, transform);

                var elapsed = 0f;
                var duration = .22f;
                var halfReached = false;
                while (elapsed < duration)
                {
                    var point = BézierPoint(fromPoint, mid, mid, toPoint, Easing.InOutQuad(isStrike && elapsed > duration / 2 ? duration - elapsed : elapsed, 0, 1, duration));
                    StatusLightParent.transform.localPosition = convertPointToVector(point, .01f);
                    yield return null;
                    elapsed += Time.deltaTime;
                    if (elapsed >= duration / 2 && !halfReached)
                    {
                        if (isStrike)
                        {
                            _revealedWalls[edgeIx] = true;
                            Module.HandleStrike();
                            _striking = true;
                            StartCoroutine(UpdateVisualsLater(1f));
                        }
                        halfReached = true;
                        UpdateVisuals();
                    }
                }
                StatusLightParent.transform.localPosition = convertPointToVector(isStrike ? fromPoint : toPoint, .01f);
            }

            yield return null;
        }
    }

    private IEnumerator UpdateVisualsLater(float? delay = null)
    {
        yield return delay == null ? null : new WaitForSeconds(delay.Value);
        if (_striking)
        {
            _striking = false;
            _exploring = true;
        }
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

        for (var i = 0; i < mf.transform.childCount; i++)
        {
            var child = mf.transform.GetChild(i);
            if (child != null && child.name == "Highlight(Clone)" && child.GetComponent<MeshFilter>() is MeshFilter childMf)
                childMf.sharedMesh = mesh;
        }
    }

    private string Dump(GameObject obj, int indent)
    {
        var sb = new StringBuilder();
        var components = obj.GetComponents<Component>();
        for (var i = 0; i < components.Length; i++)
            sb.AppendLine($"{new string(' ', indent * 4)}- {components[i].GetType().FullName}");
        for (var i = 0; i < obj.transform.childCount; i++)
        {
            sb.AppendLine($"{new string(' ', indent * 4)}• {obj.transform.GetChild(i).name}");
            sb.Append(Dump(obj.transform.GetChild(i).gameObject, indent + 4));
        }
        return sb.ToString();
    }

    private KMSelectable.OnInteractHandler RoomPressed(int roomIx)
    {
        return delegate
        {
            _rooms[roomIx].sel.AddInteractionPunch(.5f);
            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, _rooms[roomIx].sel.transform);

            if (_isSolved || _locked)
                return false;

            if (_exploring)
            {
                Audio.PlaySoundAtTransform("Find", transform);
                _exploring = false;
                UpdateVisuals();
                return false;
            }

            var edgeIx = _edges.IndexOf(e => (e.roomA == roomIx && e.roomB == _curRoom) || (e.roomB == roomIx && e.roomA == _curRoom));
            if (edgeIx == -1)
                return false;

            var (renderer, isPassable, roomA, roomB) = _edges[edgeIx];
            var sound = "Move";
            var prevRoom = _curRoom;
            if (!isPassable)
            {
                var p1 = _roomLabelPositions[prevRoom];
                var p2 = _roomLabelPositions[roomIx];
                var mid = (_voronoi.Edges[edgeIx].edge.Start + _voronoi.Edges[edgeIx].edge.End) / 2;
                Debug.Log($@"[Voronoi Maze #{_moduleId}] {new JObject
                {
                    ["isStrike"] = true,
                    ["from"] = _curRoom,
                    ["to"] = roomIx,
                    ["edge"] = edgeIx,
                    ["d"] = $"M{p1.X} {1 - p1.Y}C{mid.X} {1 - mid.Y} {mid.X} {1 - mid.Y} {p2.X} {1 - p2.Y}",
                    ["passable"] = new JArray(_edges.SelectIndexWhere(e => e.isPassable).Select(i => (object) i).ToArray())
                }.ToString(Newtonsoft.Json.Formatting.None)}");
                _locked = true;
            }
            else
            {
                _curRoom = roomIx;
                if (_curRoom == _keys[_keysCollected])
                {
                    _keysCollected++;
                    if (_keysCollected >= _keys.Count)
                    {
                        Debug.Log($@"[Voronoi Maze #{_moduleId}] {new JObject { ["isSolve"] = true, ["passable"] = new JArray(_edges.SelectIndexWhere(e => e.isPassable).Select(i => (object) i).ToArray()) }.ToString(Newtonsoft.Json.Formatting.None)}");
                        _isSolved = true;
                        Module.HandlePass();
                        sound = "Solve";
                    }
                    else
                        sound = "Find";
                }
            }
            _animationQueue.Enqueue((prevRoom, roomIx, !isPassable, sound));

            return false;
        };
    }

    private static PointD BézierPoint(PointD start, PointD control1, PointD control2, PointD end, double t) =>
        Math.Pow(1 - t, 3) * start + 3 * Math.Pow(1 - t, 2) * t * control1 + 3 * (1 - t) * t * t * control2 + Math.Pow(t, 3) * end;

    private void UpdateVisuals()
    {
        for (var edgeIx = 0; edgeIx < _edges.Length; edgeIx++)
        {
            _edges[edgeIx].renderer.material.color =
                _isSolved ? (_revealedWalls[edgeIx] ? SolvedRevealedWallColor : SolvedWallColor) :
                _exploring ? (_revealedWalls[edgeIx] ? ImpassableColor : PassableColor) :
                _striking ? (_revealedWalls[edgeIx] ? StrikeColor : WallColors[0]) :
                _revealedWalls[edgeIx] ? RevealedWallColors[_keysCollected] :
                WallColors[_keysCollected];

            _edges[edgeIx].renderer.transform.localPosition = new Vector3(
                _edges[edgeIx].renderer.transform.localPosition.x,
                _revealedWalls[edgeIx] ? .0102f : .0101f,
                _edges[edgeIx].renderer.transform.localPosition.z);
        }

        for (var roomIx = 0; roomIx < _rooms.Length; roomIx++)
        {
            _rooms[roomIx].mr.material.color =
                 _striking ? StrikeColor :
                _isSolved ? SolvedColor :
                (_exploring && _keys.Contains(roomIx)) ? KeyColors[_keys.IndexOf(roomIx)] :
                _exploring ? DefaultRoomColor :
                roomIx == _curRoom ? CurrentRoomColors[_keysCollected] : DefaultRoomColors[_keysCollected];

            SetHighlightMesh(_rooms[roomIx].highlightMf,
                _isSolved ? null :
                _exploring ? Enumerable.Range(0, NumRooms)
                    .Where(r => _edges.Any(e => (e.roomA == roomIx && e.roomB == r) || (e.roomB == roomIx && e.roomA == r)))
                    .SelectMany(adj => _highlightTris[roomIx + NumRooms * adj])
                    .ToArray() :
                _highlightTris[_curRoom + NumRooms * roomIx]);
        }

        for (var keyIx = 0; keyIx < 3; keyIx++)
            Keys[keyIx].SetActive(!_striking && _exploring && keyIx >= _keysCollected);

        StatusLightParent.SetActive(_striking || !_exploring);
        Frame.material.color = _striking ? StrikeColor : _isSolved ? SolvedColor : _exploring ? ImpassableColor : CurrentRoomColors[_keysCollected];
    }

    const double cf = .0835 / .5;
    PointD convertPoint(PointD orig) => (orig - new PointD(.5, .5)) * cf;
    Vector3 convertPointToVector(PointD orig, float y)
    {
        var p = convertPoint(orig);
        return new Vector3((float) p.X, y, (float) p.Y);
    }
}
