using Rhino;
using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// Draws a quad, triangulated or diagrid layout of straight members over one
/// surface.
/// <para>
/// The whole engine. Both the Grasshopper component and the Rhino command call
/// <see cref="Generate(Surface, SurfaceGridOptions?)"/> or one of its siblings
/// and nothing else, so the two front-ends cannot drift.
/// </para>
/// <para>
/// Three layers, kept apart because the next tools need them apart:
/// </para>
/// <list type="number">
/// <item><description>
/// <em>Where the grid lines go</em> is decided along the surface's edges, by the
/// same <see cref="StationLayout"/> that sets out a truss. The two edges running
/// in U are its chords for the U direction, the two running in V for the other,
/// so a division is measured by length along the edge rather than by surface
/// parameter — which bunches wherever the surface was built unevenly — and
/// kinks and snap points on an edge become grid lines, exactly as they become
/// panel points on a chord.
/// </description></item>
/// <item><description>
/// <em>The nodes</em> are a <see cref="Lattice"/>: each grid line's parameter is
/// taken from both edges it runs between and blended across the surface, then
/// the surface is evaluated there, so every node sits on it.
/// </description></item>
/// <item><description>
/// <em>The members</em> are a <see cref="GridPattern"/> read off the lattice by
/// index. Nothing in this step looks at geometry except to drop a member with
/// no length.
/// </description></item>
/// </list>
/// <para>
/// Not a mesher, and deliberately. A mesher copes with any shape and returns
/// something with no rows, no columns and a result that shifts between
/// releases; this returns a layout an engineer can predict before running it
/// and address afterwards. The price is that it wants one surface with one pair
/// of directions.
/// </para>
/// </summary>
public static class SurfaceGridGenerator
{
    public static SurfaceGrid Generate(Surface surface, SurfaceGridOptions? options = null)
    {
        if (surface is null) throw new ArgumentNullException(nameof(surface));

        return Build(surface, options ?? new SurfaceGridOptions(), trimmed: false);
    }

    /// <summary>
    /// A surface as Rhino hands it over, which is a Brep with one face. More
    /// than one face is refused: a joined polysurface has no single pair of
    /// directions, and gridding each face separately would leave the lines of
    /// one missing the lines of the next at every shared edge.
    /// </summary>
    public static SurfaceGrid Generate(Brep brep, SurfaceGridOptions? options = null)
    {
        if (brep is null) throw new ArgumentNullException(nameof(brep));

        if (brep.Faces.Count != 1)
            throw new ArgumentException(
                $"A grid needs a single surface, and this is a polysurface of {brep.Faces.Count}. "
                + "Its faces do not share a pair of directions to grid along; pick one of them, or "
                + "give the four curves round the outside instead.",
                nameof(brep));

        BrepFace face = brep.Faces[0];

        return Build(face.UnderlyingSurface(), options ?? new SurfaceGridOptions(), trimmed: !face.IsSurface);
    }

    /// <summary>
    /// The curves round the outside, for a model that has sticks and no
    /// surfaces. Two to four of them, meeting end to end in any order; the
    /// surface between is Rhino's edge surface, and is only ever a means of
    /// placing nodes — it is not returned.
    /// </summary>
    public static SurfaceGrid Generate(IReadOnlyList<Curve> edges, SurfaceGridOptions? options = null)
    {
        if (edges is null) throw new ArgumentNullException(nameof(edges));

        if (edges.Count is < 2 or > 4)
            throw new ArgumentException(
                $"A grid between curves takes two, three or four of them; {edges.Count} were given.",
                nameof(edges));

        if (edges.Any(edge => edge is null || !edge.IsValid))
            throw new ArgumentException("Every edge must be a valid curve.", nameof(edges));

        options ??= new SurfaceGridOptions();

        // Rhino will build a surface between curves that are nowhere near each
        // other, and a grid over that is nonsense presented with a straight
        // face. Two curves are allowed to stand apart — that is a strip between
        // two rails — but three or four have to close a loop.
        Brep? patch = edges.Count == 2 || ClosesALoop(edges, options.Tolerance)
            ? Brep.CreateEdgeSurface(edges)
            : null;

        if (patch is null || patch.Faces.Count != 1)
            throw new ArgumentException(
                "No surface could be built between those curves. They have to meet end to end, "
                + "round the outside of the area to grid.",
                nameof(edges));

        return Build(patch.Faces[0].UnderlyingSurface(), options, trimmed: false);
    }

    private static bool ClosesALoop(IReadOnlyList<Curve> edges, double tolerance)
    {
        Curve[] joined = Curve.JoinCurves(edges, tolerance);

        return joined.Length == 1 && joined[0].IsClosed;
    }

    private static SurfaceGrid Build(Surface picked, SurfaceGridOptions options, bool trimmed)
    {
        Validate(options);

        // NURBS form, so that an edge curve's parameter is the surface's
        // parameter exactly. The edges of a surface of revolution are arcs, and
        // an arc has its own idea of what a parameter means.
        NurbsSurface surface = picked.ToNurbsSurface()
            ?? throw new ArgumentException("The surface could not be read.", nameof(picked));

        Interval domainU = surface.Domain(0);
        Interval domainV = surface.Domain(1);

        bool wrapU = surface.IsClosed(0);
        bool wrapV = surface.IsClosed(1);

        // IsoCurve(0, v) runs in U at constant v. An edge with no length is a
        // pole — a dome's apex, the tip of a fan — and cannot be a ruler.
        StationLayout.ChordRuler?[] edgesU =
        {
            Ruler(surface.IsoCurve(0, domainV.T0), options.Tolerance),
            Ruler(surface.IsoCurve(0, domainV.T1), options.Tolerance),
        };
        StationLayout.ChordRuler?[] edgesV =
        {
            Ruler(surface.IsoCurve(1, domainU.T0), options.Tolerance),
            Ruler(surface.IsoCurve(1, domainU.T1), options.Tolerance),
        };

        if (edgesU.All(edge => edge is null) || edgesV.All(edge => edge is null))
            throw new ArgumentException("The surface has no extent in one of its directions.", nameof(picked));

        SortSnapPoints(options, edgesU, edgesV, out List<Point3d> pointsU, out List<Point3d> pointsV, out int offEdge);

        bool even = options.Pattern == GridPattern.Diagrid;

        double[] stationsU = Stations(
            edgesU, options.DivisionsU, options.SpacingU, pointsU, options, even, out int unusedU, out int raisedU);
        double[] stationsV = Stations(
            edgesV, options.DivisionsV, options.SpacingV, pointsV, options, even, out int unusedV, out int raisedV);

        Lattice lattice = PlaceNodes(surface, edgesU, edgesV, stationsU, stationsV, wrapU, wrapV);

        int[] canonical = WeldPoles(lattice, edgesU, edgesV);
        List<GridMember> members = DrawPattern(lattice, canonical, options);

        return new SurfaceGrid(
            lattice, members, options, trimmed, offEdge, unusedU + unusedV, raisedU, raisedV);
    }

    private static void Validate(SurfaceGridOptions options)
    {
        if (options.DivisionsU < 0 || options.DivisionsV < 0)
            throw new ArgumentException("Divisions cannot be negative.", nameof(options));
        if (options.SpacingU < 0.0 || options.SpacingV < 0.0)
            throw new ArgumentException("Spacing cannot be negative.", nameof(options));
        if (options.Tolerance <= 0.0)
            throw new ArgumentException("Tolerance has to be greater than zero.", nameof(options));
        if (!Enum.IsDefined(options.Pattern))
            throw new ArgumentException(
                $"Grid pattern {(int)options.Pattern} does not exist. Valid values are "
                + $"0-{Enum.GetValues<GridPattern>().Length - 1}.",
                nameof(options));
        if (!Enum.IsDefined(options.Diagonals))
            throw new ArgumentException(
                $"Diagonal rule {(int)options.Diagonals} does not exist. Valid values are "
                + $"0-{Enum.GetValues<DiagonalRule>().Length - 1}.",
                nameof(options));
        if (!Enum.IsDefined(options.Strictness))
            throw new ArgumentException(
                $"Snap strictness {(int)options.Strictness} does not exist. Valid values are "
                + $"0-{Enum.GetValues<SnapStrictness>().Length - 1}.",
                nameof(options));
    }

    private static StationLayout.ChordRuler? Ruler(Curve? edge, double tolerance)
        => edge is null || edge.GetLength() <= tolerance
            ? null
            : new StationLayout.ChordRuler(edge, tolerance, onPlan: false);

    /// <summary>
    /// Hand each snap point to the direction it can set a line in.
    /// <para>
    /// A point on an edge running in U fixes a position along U, and the grid
    /// line through it runs the other way. Sorted here rather than offered to
    /// both directions, because the layout counts a point that is on none of
    /// its chords as a mistake — and a point on the other pair of edges is not
    /// one.
    /// </para>
    /// </summary>
    private static void SortSnapPoints(
        SurfaceGridOptions options,
        StationLayout.ChordRuler?[] edgesU,
        StationLayout.ChordRuler?[] edgesV,
        out List<Point3d> pointsU,
        out List<Point3d> pointsV,
        out int offEdge)
    {
        pointsU = new List<Point3d>();
        pointsV = new List<Point3d>();
        offEdge = 0;

        static double Nearest(StationLayout.ChordRuler?[] edges, Point3d point)
        {
            double nearest = double.MaxValue;

            foreach (StationLayout.ChordRuler? edge in edges)
            {
                if (edge is null) continue;

                edge.StationNearest(point, out double distance);
                nearest = Math.Min(nearest, distance);
            }

            return nearest;
        }

        foreach (Point3d point in options.SnapPoints)
        {
            double toU = Nearest(edgesU, point);
            double toV = Nearest(edgesV, point);

            if (Math.Min(toU, toV) > options.Tolerance)
                offEdge++;
            else if (toU <= toV)
                pointsU.Add(point);
            else
                pointsV.Add(point);
        }
    }

    /// <summary>
    /// The stations for one direction, from the edges that run in it.
    /// <para>
    /// A diagrid has to come out with an even number of panels. When the first
    /// layout is odd it is laid out again one panel up — strictly, if nothing
    /// but the geometry was driving, since those points were every one of them
    /// a grid line and asking for a count must not be what loses them.
    /// </para>
    /// </summary>
    private static double[] Stations(
        StationLayout.ChordRuler?[] edges,
        int divisions,
        double spacing,
        List<Point3d> snapPoints,
        SurfaceGridOptions options,
        bool even,
        out int unused,
        out int raised)
    {
        StationLayout.ChordRuler[] live = edges.OfType<StationLayout.ChordRuler>().ToArray();

        var request = new StationRequest(divisions, spacing, options.Strictness, snapPoints, options.Tolerance);

        double[] stations = StationLayout.Resolve(live, request, out unused, out _);
        raised = 0;

        bool geometryDrove = divisions <= 0 && spacing <= 0.0;

        // Strict can grow the count past what was asked for, so this is a loop
        // rather than a single retry; it cannot run long, since every pass asks
        // for a count at least as large as the number of fixed points.
        for (int attempt = 0; even && (stations.Length - 1) % 2 == 1 && attempt < 4; attempt++)
        {
            request = request with
            {
                Divisions = stations.Length,
                Strictness = geometryDrove ? SnapStrictness.Strict : options.Strictness,
            };

            stations = StationLayout.Resolve(live, request, out unused, out _);
            raised = 1;
        }

        return stations;
    }

    /// <summary>
    /// Evaluate the surface at every crossing of the grid lines.
    /// <para>
    /// A grid line in V sits at station <c>s</c> along <em>both</em> U edges,
    /// and on a surface whose edges differ — a fan, a taper — that is a
    /// different parameter on each. The line is run between the two by blending
    /// across, which lands it exactly on its station at either edge and moves
    /// it evenly from one to the other in between. A pole has no parameter of
    /// its own to offer, so it borrows the opposite edge's.
    /// </para>
    /// </summary>
    private static Lattice PlaceNodes(
        NurbsSurface surface,
        StationLayout.ChordRuler?[] edgesU,
        StationLayout.ChordRuler?[] edgesV,
        double[] stationsU,
        double[] stationsV,
        bool wrapU,
        bool wrapV)
    {
        static double[][] Parameters(StationLayout.ChordRuler?[] edges, double[] stations)
        {
            StationLayout.ChordRuler near = edges[0] ?? edges[1]!;
            StationLayout.ChordRuler far = edges[1] ?? edges[0]!;

            return new[]
            {
                stations.Select(near.ParameterAtStation).ToArray(),
                stations.Select(far.ParameterAtStation).ToArray(),
            };
        }

        double[][] u = Parameters(edgesU, stationsU);
        double[][] v = Parameters(edgesV, stationsV);

        // A direction that wraps has its last station on top of its first.
        int countU = wrapU ? stationsU.Length - 1 : stationsU.Length;
        int countV = wrapV ? stationsV.Length - 1 : stationsV.Length;

        var nodes = new Point3d[countU * countV];

        for (int j = 0; j < countV; j++)
            for (int i = 0; i < countU; i++)
            {
                double across = stationsV[j];
                double along = stationsU[i];

                nodes[j * countU + i] = surface.PointAt(
                    u[0][i] + (u[1][i] - u[0][i]) * across,
                    v[0][j] + (v[1][j] - v[0][j]) * along);
            }

        return new Lattice(nodes, countU, countV, wrapU, wrapV);
    }

    /// <summary>
    /// One index for every node stacked on a pole.
    /// <para>
    /// Where an edge has collapsed to a point, a whole row of the lattice sits
    /// on it, and members drawn to different nodes of that row are the same
    /// member several times over — the case a truss meets where its chords
    /// converge. Referring to the row by its first node lets the ordinary
    /// no-member-twice check catch them, and leaves the model with one node at
    /// the apex rather than a dozen.
    /// </para>
    /// </summary>
    private static int[] WeldPoles(
        Lattice lattice, StationLayout.ChordRuler?[] edgesU, StationLayout.ChordRuler?[] edgesV)
    {
        int[] canonical = Enumerable.Range(0, lattice.Nodes.Count).ToArray();

        if (!lattice.WrapV)
            foreach (int j in new[] { 0, lattice.CountV - 1 })
                if (edgesU[j == 0 ? 0 : 1] is null)
                    for (int i = 0; i < lattice.CountU; i++)
                        canonical[lattice.Index(i, j)] = lattice.Index(0, j);

        if (!lattice.WrapU)
            foreach (int i in new[] { 0, lattice.CountU - 1 })
                if (edgesV[i == 0 ? 0 : 1] is null)
                    for (int j = 0; j < lattice.CountV; j++)
                        canonical[lattice.Index(i, j)] = lattice.Index(i, 0);

        return canonical;
    }

    /// <summary>
    /// The members, read off the lattice by position.
    /// <para>
    /// Cell (i, j) has corners a = (i, j), b = (i+1, j), c = (i+1, j+1) and
    /// d = (i, j+1). Its <em>forward</em> diagonal is a–c and its <em>back</em>
    /// one b–d, and every pattern with diagonals is a rule for which of the two
    /// a cell gets.
    /// </para>
    /// </summary>
    private static List<GridMember> DrawPattern(Lattice lattice, int[] canonical, SurfaceGridOptions options)
    {
        var members = new List<GridMember>();
        var seen = new HashSet<(int, int)>();

        void Add(int start, int end, GridMemberRole role, bool alongU, int gridLine)
        {
            start = canonical[start];
            end = canonical[end];

            if (start == end) return;
            if (!seen.Add(start < end ? (start, end) : (end, start))) return;

            var line = new Line(lattice.Nodes[start], lattice.Nodes[end]);
            if (line.Length <= options.Tolerance) return;   // drop degenerate members

            members.Add(new GridMember(line, role, start, end, alongU, gridLine));
        }

        // A side is only an edge if the grid stops there. Round a tower there
        // is no first or last column, just a seam nobody should be able to find.
        bool EdgeRow(int j) => !lattice.WrapV && (j == 0 || j == lattice.CountV - 1);
        bool EdgeColumn(int i) => !lattice.WrapU && (i == 0 || i == lattice.CountU - 1);

        bool diagrid = options.Pattern == GridPattern.Diagrid;

        if (!diagrid)
        {
            for (int j = 0; j < lattice.CountV; j++)
                for (int i = 0; i < lattice.PanelsU; i++)
                    Add(lattice.Index(i, j), lattice.Index(i + 1, j),
                        EdgeRow(j) ? GridMemberRole.Edge : GridMemberRole.U, alongU: true, j);

            for (int i = 0; i < lattice.CountU; i++)
                for (int j = 0; j < lattice.PanelsV; j++)
                    Add(lattice.Index(i, j), lattice.Index(i, j + 1),
                        EdgeColumn(i) ? GridMemberRole.Edge : GridMemberRole.V, alongU: false, i);
        }

        if (options.Pattern == GridPattern.Quad) return members;

        // Which nodes a diagrid stands on: the ones whose column and row add up
        // even, or under Flip the ones that add up odd.
        int parity = options.Flip ? 1 : 0;

        foreach (LatticeCell cell in lattice.Cells)
        {
            int a = cell.Corners[0], b = cell.Corners[1], c = cell.Corners[2], d = cell.Corners[3];
            bool chequer = (cell.I + cell.J) % 2 == 0;

            bool forward = diagrid
                ? chequer == (parity == 0)
                : options.Flip != options.Diagonals switch
                {
                    DiagonalRule.OneWay => true,
                    DiagonalRule.Alternating => chequer,

                    // Ties go forward, so a flat regular grid comes out one-way
                    // rather than at the mercy of rounding.
                    DiagonalRule.Shorter =>
                        lattice.Nodes[a].DistanceTo(lattice.Nodes[c])
                        <= lattice.Nodes[b].DistanceTo(lattice.Nodes[d]) + options.Tolerance,

                    _ => throw new ArgumentOutOfRangeException(
                        nameof(options), options.Diagonals, "Unhandled diagonal rule."),
                };

            if (forward) Add(a, c, GridMemberRole.Diagonal, alongU: false, -1);
            else Add(b, d, GridMemberRole.Diagonal, alongU: false, -1);
        }

        if (!diagrid) return members;

        // Closing the diagrid round the outside: along each edge, from one node
        // the diagrid stands on to the next, which is two divisions on. The
        // nodes between are not part of it, so an edge member runs straight
        // past them.
        for (int j = 0; j < lattice.CountV; j++)
        {
            if (!EdgeRow(j)) continue;

            for (int i = 0; i + 2 <= lattice.PanelsU; i++)
                if ((i + j) % 2 == parity)
                    Add(lattice.Index(i, j), lattice.Index(i + 2, j), GridMemberRole.Edge, alongU: true, j);
        }

        for (int i = 0; i < lattice.CountU; i++)
        {
            if (!EdgeColumn(i)) continue;

            for (int j = 0; j + 2 <= lattice.PanelsV; j++)
                if ((i + j) % 2 == parity)
                    Add(lattice.Index(i, j), lattice.Index(i, j + 2), GridMemberRole.Edge, alongU: false, i);
        }

        return members;
    }
}
