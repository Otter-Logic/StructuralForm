# Roadmap: small additions to Rhino for structural geometry

Rhino already draws lines, surfaces and solids well. What it lacks is a handful
of moves a structural engineer makes constantly and that take several native
commands each: a column at every crossing of some lines, braces in a bay, a
floor copied up a storey with its columns the right length, a hundred beams
split where they meet. Those are the tools here. Each one takes what Rhino
already has — curves, points, a number — and gives back lines or surfaces. No
grid object, no level object, no naming: a gridline is any curve the user
picks, and a level is a height typed in.

The rule for whether a tool belongs: **it does in one go something Rhino needs
three commands or a click per object to do, and the thing it does is
structural.** Where a native command already does the job, the tool is not
built, and that is recorded below so it is not proposed twice.

Every tool is a Rhino command and a Grasshopper component over one engine, with
options remembered between runs, a preview before anything is committed, and
`Notes` for whatever quietly did not work — the same shape as the five tools
that exist.

## Create

### Columns — `OtterColumn` *(built)*

Pick curves; type a base height and a top height. The curves are flattened to
Z = 0, their crossings are shown, and a vertical line stands at each from base
to top. See *GridColumns* in the README. Not built, and worth considering
later: picking points instead of curves for the column on no crossing, and a
`Storeys` list — `3600, 3x4000` — to stack a column per storey.

*Native check.* `Intersect` finds the crossings as points, but there is no
point-to-line extrude in Rhino, so it is then `Line` > `Vertical` once per
column. This is the gap, and the reason the tool exists.

### Grids — `OtterGrid`, `OtterRadialGrid` *(built)*

Pick an origin; type the bays each way as `6000` or `3x6000, 8000`. The
gridlines and a node at every crossing are previewed in the construction
plane, with the overhang and an angle to adjust, and baked to `OtterGrid1`.
The radial one takes a centre, the hole in the middle as two half-axes — both
zero for rays to the centre, equal for a round hole, different for the oval a
stadium sits round — the ring spacings, a sweep in degrees (90 for a quarter)
and a bay count; a full sweep closes its rings and does not draw the last ray
on the first. See *RectangularGrid* and *RadialGrid* in the README. Still not built, by the rule at the top: labels. A gridline is a line
the office names its own way, and a grid object that carried A, B, C would
have to be kept in step with every drawing that showed them.

*Native check.* Rhino has no grid object; `Grid` only toggles the CPlane
grid's display. A rectangular grid by hand is a `Line` per gridline, or
`Array` when every bay is the same, then `Intersect` for the nodes; a radial
one is `Line`, `ArrayPolar` and an `Arc` or `Circle` per ring, and
`ArrayPolar` cannot stop at a quarter without a count that happens to divide.
Grasshopper's own *Rectangular* and *Radial* grids (Vector > Grid) repeat one
spacing, always close the circle, and give cells rather than gridlines, so a
grid of unequal bays or a quarter grid is not one component there either.

### Beams — `OtterBeam` *(built)*

Pick the columns, pick the gridlines, type a level or pick a surface. Along
every gridline, a beam between each pair of neighbouring columns standing on
it, at the level or following the surface, and a node where each meets a
column. A crossing with no column gets no beam through it; nothing runs past
the last column. See *GridBeams* in the README. With this, Columns and Beam
Infill, a floor is drawn from its gridlines and two numbers.

*Native check.* `Split` a gridline at the column points, then `Move` the
pieces up, then delete the ends past the last column: three commands per
gridline, and the column points have to be found first. Grasshopper has
Shatter, which wants the parameters, and nothing that reads which columns are
on which line.

### Braced Bay — `OtterBracing`

Pick the two columns of a bay — or the four lines round it, or a closed
four-sided polyline — and choose a pattern: `X`, `Single`, `V`, `InvertedV`,
`K`. `Flip` mirrors the single diagonal or the K. The apex of a V or the
knee of a K lands on the beam as a point the beam should be split at; the
point is returned and the beam is left alone, the way Beam Infill returns the
nodes on the primaries.

Two columns that are not the same height, or a bay whose four lines are not
coplanar within tolerance, are handed back with a note rather than braced.

*Native check.* `Line` between osnapped ends, twice per bay per storey. The
pattern and the apex point are what a hand-drawn version keeps getting wrong.

### Copy Storey — `OtterCopyStorey`

Select a floor's worth of geometry and give storey heights: `4000` or
`3600, 3x4000`. The selection is copied up once per storey, and **vertical
lines are stretched to the storey they land in** — a 3600 column copied to a
4000 storey becomes a 4000 column. That is the case a native Copy gets wrong,
and it is the whole reason for the tool.

*Native check.* `Copy` takes several placements in one run, so the copies
themselves are one command with typed offsets. The column lengths are not.

### Centreline — `OtterCentreline`

A solid column or beam in, its axis line out; a solid wall or slab in, its
centre plane out. For turning an architect's or a BIM export's solids into a
stick and shell model without redrawing it. A solid whose ends are not
parallel, or whose section changes, gets its best-fit axis and a note.

*Native check.* `ExtractWireframe` and `DupEdge` give edges, never the axis.
`Volume` gives a centroid, not a line. There is no native centreline.

## Split and tidy

### Split at Intersections — `OtterSplitAtIntersections`

Select curves. Every one is split where another crosses it or ends on it,
within tolerance, in one go. With a boundary curve picked, anything outside
it is dropped too. The pieces are what an analysis wants: a beam between each
pair of columns, a column per storey.

Running this on picked gridlines is how **beams on grid** are made — the
gridlines themselves, split at their crossings, are the beams. No separate
tool.

*Native check.* `Intersect` gives the points; `Split` then wants one curve and
its points at a time. On a floor of two hundred beams that is the afternoon.

### Snap Ends — `OtterSnapEnds`

Select lines, pick targets, give a reach. Every line end within reach of a
target is moved onto it: a target is a curve, a point, or a height. Ends that
land within tolerance of each other are merged. Anything further than the
reach is left where it is and counted.

The targets a floor needs, in one tool: the column ten millimetres off the
gridline (target: the gridline), the beam modelled high (target: a height),
the ends that nearly meet (target: each other). The reach is typed, never
read from the model, because moving a member is a change and its largest
size should be chosen.

*Native check.* `SetPt` moves every control point of a line to the same Z,
which flattens a column to nothing. `ExtendCrv` to a boundary is one click per
curve. `SelDup` finds exact duplicates only. Nothing native moves ends to a
target in a batch.

## Rotate and orient by gridline

Most rotation is native, and the tools here are only the two moves Rhino
cannot make in one command. What is *not* built, and how it is done natively:

| Want | Native |
|---|---|
| Rotate about a grid intersection so an element lines up with the other gridline | `Rotate` with the intersection as centre, then two reference points along the gridlines |
| Move and turn an element from one bay to another | `Orient` with `Scale=No` |
| Copies of a bay round a curved grid | `ArrayPolar` centred on the grid's centre; `ArrayCrv` along the gridline |
| A construction plane along a gridline, or at a level | `CPlane` > `Curve`, `CPlane` > `Elevation` |
| Mirror across a gridline | `Mirror` with osnaps on the gridline's ends |
| The angle between two gridlines | `Angle` |

### Orient to Gridline — `OtterOrientToGridline`

Select objects — column solids, section blocks, footings — and pick the
gridlines. Each object is turned about its own vertical axis until its local X
follows the nearest gridline's tangent at that point, plus a typed angle. For
the radial or skewed grid where every column should face its gridline, and for
a curved facade where the mullions should follow the curve.

An object equidistant from two gridlines, or further than a typed reach from
any, is left alone and counted.

*Native check.* `OrientOnCrv` places *one* object along a curve. `Rotate` is
one object at a time. There is no batch orient of existing objects to nearby
curves.

### Rotate About Curve — `OtterRotateAboutCurve`

`Rotate3D` with the axis picked as a line instead of two points, with `Copy`
and a typed angle. For tilting a roof about its eaves beam, or turning a
brace about the column it lands on.

*Native check.* `Rotate3D` asks for two points on the axis; with osnaps that
is two clicks on the line's ends, so this is a small saving. Kept because it
is asked for often and the engine is one line.

## Later, and still geometry only

Bigger than the tools above but on the same rules: no loads, no analysis
attributes, ambiguity handed back.

- **Floor Grillage** — beams both ways across a plate at two spacings inside a
  boundary; the `Lattice`'s next user, as the README says.
- **Tributary Polygons** — for each beam and column round a set of floor
  panels, the polygon of floor it carries, as a surface with its area. One-way
  or two-way is picked per panel because the tool cannot know it.
- **Constrained Slab Mesh** — a quad mesh of a floor panel whose edge nodes are
  exactly the beam split points and column positions, so plate and beam
  elements share nodes. *Native check:* `Mesh` and `QuadRemesh` do not honour
  edge nodes.

## Not built, because Rhino has it

Recorded so it is not proposed again.

| Proposed | Native |
|---|---|
| Wall from a gridline and a height | `ExtrudeCrv` |
| Floor panels from a set of beams | `CurveBoolean` with `AllRegions`, then `PlanarSrf` |
| Openings in a wall or slab | `Trim`, or `MakeHole` |
| Divide a curved beam into straight segments | `Divide` with `Split=Yes`, then `Polyline` through the points, or `Convert` |
| Beams between every grid intersection, columns or not | `OtterSplitAtIntersections` on the gridlines, above; `OtterBeam` where the beams should follow the columns |
| Extend beams to a gridline, or trim them back | `ExtendCrv` with the gridline as boundary, `Trim` |
| Portal frames at bay spacing | draw one frame; `ArrayLinear` |
| Levels as planes | `CPlane` > `Elevation`, or a `Plane` on a locked layer |
| Node points at member ends | `Divide` with count 1, `MarkEnds=Yes` |
| Column at every intersection of a grid drawn as a surface's isocurves | `ExtractIsocurve`, then `OtterColumns` |

## Order

1. **Columns**, **Grids**, **Beams**, **Split at Intersections**, **Copy
   Storey** — with these and the existing Beam Infill, a framed building is
   drawn from a few numbers.
2. **Braced Bay**, **Snap Ends**.
3. **Orient to Gridline**, **Rotate About Curve**, **Centreline**.
4. Grillage, Tributary Polygons, Slab Mesh.
