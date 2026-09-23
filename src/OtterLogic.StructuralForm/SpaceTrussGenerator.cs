using Rhino.Geometry;

namespace OtterLogic.StructuralForm;

/// <summary>
/// Builds a double-layer space truss on a <see cref="SurfaceGrid"/>.
/// <para>
/// The whole engine. Both the Grasshopper component and the Rhino command call
/// <see cref="Generate"/> and nothing else, so the two front-ends cannot drift.
/// </para>
/// <para>
/// The grid is the top layer, exactly as it was made: its nodes, its members,
/// its openings if it was clipped. The truss adds a second layer under it and
/// a web between the two, and where the second layer's nodes go is the one
/// decision <see cref="SpaceTrussType"/> makes:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="SpaceTrussType.Offset"/> puts a node under the centre of every
/// cell and joins it to the cell's corners — a pyramid per cell, the second
/// layer a grid of the apexes. Under a diagrid, a node under the centre of
/// every diamond, joined to its four corners, and a second diagrid between the
/// apexes. Everything is index arithmetic over the lattice.
/// </description></item>
/// <item><description>
/// <see cref="SpaceTrussType.Aligned"/> puts a node under every node and draws
/// the grid's own pattern between them, then runs a flat truss along every
/// grid line — rows and columns of a quad or triangulated grid, the diagonal
/// runs of a diagrid — through the same <see cref="WebBuilder"/> a flat truss
/// uses, so <see cref="SpaceTrussOptions.Web"/> means what
/// <see cref="FlatTrussOptions.Type"/> means. An opening splits a line into
/// runs, and each run is a truss of its own, with its own end posts.
/// </description></item>
/// </list>
/// <para>
/// A pole in the grid is one node however many positions sit on it, and the
/// second layer is welded the same way, through the grid's own map of them.
/// </para>
/// </summary>
public static class SpaceTrussGenerator
{
    private static readonly FaceRoles WebRoles =
        new(TrussMemberRole.Vertical, TrussMemberRole.Diagonal, TrussMemberRole.EndPost);

    /// <summary>
    /// How level a surface has to be, on the whole, before it is read as
    /// facing up or down. The mean normal's vertical component, on a unit
    /// vector: below this the surface is stood on end and has no underside.
    /// </summary>
    private const double Level = 1e-3;

    public static SpaceTruss Generate(SurfaceGrid grid, SpaceTrussOptions? options = null)
    {
        if (grid is null) throw new ArgumentNullException(nameof(grid));

        options ??= new SpaceTrussOptions();
        Validate(options);

        Lattice top = grid.Lattice;
        double tolerance = grid.Options.Tolerance;
        int topCount = top.Nodes.Count;

        // Which side the second layer goes, decided once for the whole surface
        // rather than node by node: a dome's normals turn from up at the crown
        // to level at the springing, and a rule read per node would flip the
        // layer inside out part way down.
        Vector3d mean = Vector3d.Zero;
        int counted = 0;

        for (int k = 0; k < topCount; k++)
        {
            if (!top.IsPresent(k)) continue;
            mean += grid.Normals[k];
            counted++;
        }

        if (counted > 0) mean /= counted;

        bool sideways = Math.Abs(mean.Z) <= Level;
        double sign = (mean.Z > Level ? -1.0 : 1.0) * (options.FlipDepth ? -1.0 : 1.0);

        Vector3d Direction(Vector3d normal) => options.DepthAlong == DepthDirection.Vertical
            ? new Vector3d(0.0, 0.0, options.FlipDepth ? 1.0 : -1.0)
            : normal * sign;

        Func<Line, bool>? excluded = grid.Trim is null ? null : grid.Trim.Excludes;

        var members = new List<SpaceTrussMember>();

        // The top layer is the grid, member for member.
        foreach (GridMember m in grid.Members)
            members.Add(new SpaceTrussMember(m.Line, TrussMemberRole.TopChord, m.Role, m.StartNode, m.EndNode));

        Lattice bottom;
        int crossing;
        bool noLines = false;

        switch (options.Type)
        {
            case SpaceTrussType.Aligned:
                bottom = Aligned(grid, options, Direction, excluded, members, out crossing, out noLines);
                break;

            case SpaceTrussType.Offset when grid.Options.Pattern == GridPattern.Diagrid:
                bottom = OffsetUnderDiagrid(grid, options, Direction, excluded, members, out crossing);
                break;

            case SpaceTrussType.Offset:
                bottom = OffsetUnderCells(grid, options, Direction, excluded, members, out crossing);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Type, "Unhandled space truss type.");
        }

        return new SpaceTruss(grid, bottom, members, options, sideways, crossing, noLines);
    }

    private static void Validate(SpaceTrussOptions options)
    {
        if (!(options.Depth > 0.0) || double.IsInfinity(options.Depth))
            throw new ArgumentException("Depth has to be greater than zero.", nameof(options));
        if (!Enum.IsDefined(options.Type))
            throw new ArgumentException(
                $"Space truss type {(int)options.Type} does not exist. Valid values are "
                + $"0-{Enum.GetValues<SpaceTrussType>().Length - 1}.",
                nameof(options));
        if (!Enum.IsDefined(options.Web))
            throw new ArgumentException(
                $"Truss type {(int)options.Web} does not exist. Valid values are "
                + $"0-{Enum.GetValues<TrussType>().Length - 1}.",
                nameof(options));
        if (!Enum.IsDefined(options.DepthAlong))
            throw new ArgumentException(
                $"Depth direction {(int)options.DepthAlong} does not exist. Valid values are "
                + $"0-{Enum.GetValues<DepthDirection>().Length - 1}.",
                nameof(options));
    }

    /// <summary>
    /// The top layer dropped through the depth, node for node, with the grid's
    /// own pattern between the dropped nodes and a flat truss along every line.
    /// </summary>
    private static Lattice Aligned(
        SurfaceGrid grid,
        SpaceTrussOptions options,
        Func<Vector3d, Vector3d> direction,
        Func<Line, bool>? excluded,
        List<SpaceTrussMember> members,
        out int crossing,
        out bool noLines)
    {
        Lattice top = grid.Lattice;
        int topCount = top.Nodes.Count;

        var under = new Point3d[topCount];
        var present = new bool[topCount];

        for (int k = 0; k < topCount; k++)
        {
            under[k] = top.Nodes[k] + direction(grid.Normals[k]) * options.Depth;
            present[k] = top.IsPresent(k);
        }

        var bottom = new Lattice(under, top.CountU, top.CountV, top.WrapU, top.WrapV, present);

        // The second layer's chords: the same pattern over the same positions,
        // welded at the same poles.
        foreach (GridMember m in SurfaceGridGenerator.DrawPattern(bottom, grid.Canonical, grid.Options, grid.Trim, out crossing))
            members.Add(new SpaceTrussMember(
                m.Line, TrussMemberRole.BottomChord, m.Role, topCount + m.StartNode, topCount + m.EndNode));

        var web = new WebBuilder(top.Nodes.Concat(under).ToArray(), grid.Options.Tolerance, excluded);

        List<(int[] Nodes, bool Closed)> runs = Runs(top, grid.Canonical, grid.Options);
        noLines = runs.Count == 0;

        // Every place a line ends — the outside of the grid, the rim of an
        // opening — is an end of a truss, and a node there is an end node
        // however many lines run through it. Posts go in first so that the
        // vertical a crossing line would put there is the post, not a vertical.
        var ends = new HashSet<int>();

        foreach ((int[] nodes, bool closed) in runs)
        {
            if (closed) continue;
            ends.Add(nodes[0]);
            ends.Add(nodes[^1]);
        }

        if (options.GenerateEndPosts)
            foreach (int end in ends)
                web.Add(end, topCount + end, TrussMemberRole.EndPost);

        foreach ((int[] nodes, bool closed) in runs)
            web.AddFace(
                nodes, nodes.Select(k => topCount + k).ToArray(),
                options.Web, options.FlipWeb, endPosts: false, meetAtStart: false, meetAtEnd: false,
                WebRoles, closed);

        List<TrussMember> built = web.Members;

        // Without posts the ends are open, and a line running through an end
        // node of another line does not get to close it either.
        if (!options.GenerateEndPosts)
            built.RemoveAll(m => m.Role == TrussMemberRole.Vertical && ends.Contains(m.StartNode));

        foreach (TrussMember m in built)
            members.Add(new SpaceTrussMember(m.Line, m.Role, null, m.StartNode, m.EndNode));

        crossing += web.Excluded;

        return bottom;
    }

    /// <summary>
    /// The lines a flat truss runs along, each as a run of the nodes that are
    /// there: rows and columns for a quad or triangulated grid, the two
    /// families of diagonal runs for a diagrid. An opening breaks a line into
    /// runs; a line round a tower is one closed run.
    /// </summary>
    private static List<(int[] Nodes, bool Closed)> Runs(Lattice lattice, int[] canonical, SurfaceGridOptions options)
    {
        var runs = new List<(int[], bool)>();

        bool InRange(int i, int j)
            => (lattice.WrapU || (i >= 0 && i < lattice.CountU))
               && (lattice.WrapV || (j >= 0 && j < lattice.CountV));

        if (options.Pattern != GridPattern.Diagrid)
        {
            for (int j = 0; j < lattice.CountV; j++)
                Split(lattice.LineU(j), lattice.WrapU);

            for (int i = 0; i < lattice.CountU; i++)
                Split(lattice.LineV(i), lattice.WrapV);

            return runs;
        }

        // A diagrid stands on the nodes of one parity, and its lines are the
        // runs of those nodes stepping (1, 1) and (1, -1). A run starts where
        // it has no predecessor, which a grid closed both ways never offers.
        int parity = options.Flip ? 1 : 0;

        foreach ((int du, int dv) in new[] { (1, 1), (1, -1) })
            for (int j = 0; j < lattice.CountV; j++)
                for (int i = 0; i < lattice.CountU; i++)
                {
                    if ((i + j) % 2 != parity) continue;
                    if (InRange(i - du, j - dv)) continue;

                    var line = new List<int>();
                    int limit = lattice.CountU * lattice.CountV;

                    for (int ci = i, cj = j; InRange(ci, cj) && line.Count <= limit; ci += du, cj += dv)
                        line.Add(lattice.Index(ci, cj));

                    Split(line, wraps: false);
                }

        return runs;

        void Split(IReadOnlyList<int> line, bool wraps)
        {
            // Welded, so a row stacked on a pole reads as one node — and a run
            // of one node is no line at all.
            var welded = new List<int>(line.Count);
            foreach (int k in line)
                if (welded.Count == 0 || welded[^1] != canonical[k])
                    welded.Add(canonical[k]);

            if (wraps)
            {
                // A wrapped line repeats its first node at the end. With every
                // node there it is a ring; otherwise it is opened at a gap and
                // read like any other line from there round.
                if (welded.Count > 1 && welded[0] == welded[^1])
                    welded.RemoveAt(welded.Count - 1);

                int gap = welded.FindIndex(k => !lattice.IsPresent(k));

                if (gap < 0)
                {
                    if (welded.Count >= 3)
                        runs.Add((welded.ToArray(), true));
                    return;
                }

                welded = welded.Skip(gap).Concat(welded.Take(gap)).ToList();
            }

            var run = new List<int>();

            foreach (int k in welded)
            {
                if (lattice.IsPresent(k))
                {
                    run.Add(k);
                    continue;
                }

                Flush();
            }

            Flush();

            void Flush()
            {
                if (run.Count >= 2) runs.Add((run.ToArray(), false));
                run = new List<int>();
            }
        }
    }

    /// <summary>
    /// A node under the centre of every cell, joined to its corners; the
    /// second layer is the apexes as a quad grid of their own.
    /// <para>
    /// The apex is found by projecting the cell's centroid onto the surface
    /// and offsetting from there, rather than by averaging the corners'
    /// parameters: parameters wrap at a seam and are borrowed at a pole, and
    /// a point is neither.
    /// </para>
    /// </summary>
    private static Lattice OffsetUnderCells(
        SurfaceGrid grid,
        SpaceTrussOptions options,
        Func<Vector3d, Vector3d> direction,
        Func<Line, bool>? excluded,
        List<SpaceTrussMember> members,
        out int crossing)
    {
        Lattice top = grid.Lattice;
        int topCount = top.Nodes.Count;
        int cellsU = top.PanelsU, cellsV = top.PanelsV;

        var apex = new Point3d[cellsU * cellsV];
        var present = new bool[cellsU * cellsV];
        var corners = new int[cellsU * cellsV][];

        for (int j = 0; j < cellsV; j++)
            for (int i = 0; i < cellsU; i++)
            {
                int c = j * cellsU + i;
                corners[c] = new[] { top.Index(i, j), top.Index(i + 1, j), top.Index(i + 1, j + 1), top.Index(i, j + 1) };

                Point3d centroid = Point3d.Origin;
                Vector3d normal = Vector3d.Zero;
                foreach (int k in corners[c])
                {
                    centroid += top.Nodes[k];
                    normal += grid.Normals[k];
                }
                centroid /= 4.0;

                Point3d on = centroid;
                if (grid.Surface.ClosestPoint(centroid, out double u, out double v))
                {
                    on = grid.Surface.PointAt(u, v);
                    Vector3d at = grid.Surface.NormalAt(u, v);
                    if (at.Unitize()) normal = at;
                }

                if (!normal.Unitize()) normal = Vector3d.ZAxis;

                apex[c] = on + direction(normal) * options.Depth;

                // A cell with a corner missing has no pyramid; nor does one
                // whose apex would sit over an opening the corners straddle.
                present[c] = corners[c].All(top.IsPresent) && !(grid.Trim?.Excludes(on) ?? false);
            }

        var bottom = new Lattice(apex, cellsU, cellsV, top.WrapU, top.WrapV, present);

        // Apex to apex: a quad grid, whatever the top layer's pattern, because
        // the pyramids already triangulate everything between the layers.
        int[] identity = Enumerable.Range(0, apex.Length).ToArray();
        SurfaceGridOptions quad = grid.Options with { Pattern = GridPattern.Quad };

        foreach (GridMember m in SurfaceGridGenerator.DrawPattern(bottom, identity, quad, grid.Trim, out crossing))
            members.Add(new SpaceTrussMember(
                m.Line, TrussMemberRole.BottomChord, m.Role, topCount + m.StartNode, topCount + m.EndNode));

        var web = new WebBuilder(top.Nodes.Concat(apex).ToArray(), grid.Options.Tolerance, excluded);

        for (int c = 0; c < apex.Length; c++)
        {
            if (!present[c]) continue;

            // Welded corners, so a pyramid on a cell against a pole is three
            // members, not four with two on top of each other.
            foreach (int k in corners[c])
                web.Add(grid.Canonical[k], topCount + c, TrussMemberRole.Diagonal);
        }

        foreach (TrussMember m in web.Members)
            members.Add(new SpaceTrussMember(m.Line, m.Role, null, m.StartNode, m.EndNode));

        crossing += web.Excluded;

        return bottom;
    }

    /// <summary>
    /// Under a diagrid: a node under the centre of every diamond, which is a
    /// lattice position the diagrid does not stand on, joined to the four
    /// diagrid nodes round it. The apexes are then a diagrid of the other
    /// parity, and the second layer's chords run between them.
    /// </summary>
    private static Lattice OffsetUnderDiagrid(
        SurfaceGrid grid,
        SpaceTrussOptions options,
        Func<Vector3d, Vector3d> direction,
        Func<Line, bool>? excluded,
        List<SpaceTrussMember> members,
        out int crossing)
    {
        Lattice top = grid.Lattice;
        int topCount = top.Nodes.Count;
        int parity = grid.Options.Flip ? 1 : 0;

        bool InRange(int i, int j)
            => (top.WrapU || (i >= 0 && i < top.CountU))
               && (top.WrapV || (j >= 0 && j < top.CountV));

        bool Standing(int i, int j) => InRange(i, j) && top.IsPresent(top.Index(i, j));

        var apex = new Point3d[topCount];
        var present = new bool[topCount];

        for (int j = 0; j < top.CountV; j++)
            for (int i = 0; i < top.CountU; i++)
            {
                int k = top.Index(i, j);
                apex[k] = top.Nodes[k] + direction(grid.Normals[k]) * options.Depth;

                // A diamond centre is a position of the other parity with all
                // four diagrid nodes round it there. On the edge of the grid
                // there are only two or three, and no diamond.
                present[k] = (i + j) % 2 != parity
                    && top.IsPresent(k)
                    && Standing(i + 1, j) && Standing(i - 1, j) && Standing(i, j + 1) && Standing(i, j - 1);
            }

        var bottom = new Lattice(apex, top.CountU, top.CountV, top.WrapU, top.WrapV, present);

        var web = new WebBuilder(top.Nodes.Concat(apex).ToArray(), grid.Options.Tolerance, excluded);

        for (int j = 0; j < top.CountV; j++)
            for (int i = 0; i < top.CountU; i++)
            {
                int k = top.Index(i, j);
                if (!present[k]) continue;

                foreach ((int di, int dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    web.Add(grid.Canonical[top.Index(i + di, j + dj)], topCount + k, TrussMemberRole.Diagonal);

                // Apex to the next apex along each diagonal, once each way.
                foreach ((int di, int dj) in new[] { (1, 1), (1, -1) })
                {
                    if (!InRange(i + di, j + dj)) continue;

                    int next = top.Index(i + di, j + dj);
                    if (present[next])
                        web.Add(topCount + k, topCount + next, TrussMemberRole.BottomChord);
                }
            }

        foreach (TrussMember m in web.Members)
            members.Add(new SpaceTrussMember(
                m.Line, m.Role,
                m.Role == TrussMemberRole.BottomChord ? GridMemberRole.Diagonal : null,
                m.StartNode, m.EndNode));

        crossing = web.Excluded;

        return bottom;
    }
}
