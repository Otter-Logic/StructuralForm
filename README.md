# OtterLogic.StructuralForm

[![build](https://github.com/Otter-Logic/StructuralForm/actions/workflows/build.yml/badge.svg)](https://github.com/Otter-Logic/StructuralForm/actions/workflows/build.yml)

Trusses and structural layouts for [OtterLogic](https://github.com/Otter-Logic/Rhino3D).

A **domain**: it owns its types *and* its logic. `TrussType`, `FlatTrussOptions`,
`FlatTruss` and `FlatTrussGenerator` live together because they change together.

Nine tools so far, built the same way: the user draws the geometry that
governs, or types the numbers that do, the tool does the setting-out, and
anything that quietly did not work is said out loud in `Notes` (a `FormNote`,
shared by every tool that has something it could fail to say).

| | |
|---|---|
| [FlatTruss](#flattruss) | bracing between a top and a bottom chord |
| [BoxTruss](#boxtruss) | the same in 3D: a triangular or box truss on three or four chords |
| [SurfaceGrid](#surfacegrid) | a quad, triangulated or diagrid layout over a surface, on a reusable `Lattice` |
| [SpaceTruss](#spacetruss) | a double-layer space truss on a surface grid: pyramids, or flat trusses both ways |
| [BeamInfill](#beaminfill) | secondary members in every panel a floor's primary beams enclose |
| [GridColumns](#gridcolumns) | a column at every crossing of a set of gridlines, between two heights |
| [GridBeams](#gridbeams) | a beam along every gridline between each pair of neighbouring columns on it, at a level or on a surface |
| [RectangularGrid](#rectangulargrid) | gridlines both ways from typed bay spacings, with a node at every crossing |
| [RadialGrid](#radialgrid) | rays and rings about a centre or an oval hole, over any sweep, with a node wherever they meet |

## FlatTruss

Nodes sit at *stations* — positions along the chords measured as a fraction of
length. One list **per chord**, paired by index, so top node `i` and bottom node
`i` are the same panel point and every web pattern reduces to index arithmetic.

Per chord rather than pooled, because a snap point belongs to the chord it was
picked on. Seven points along the top chord and seven along the bottom, not
quite above each other, are seven panel points with a leaning vertical at each —
not fourteen. Pooling them gave fourteen: every point became a station on *both*
chords, so each arrived twice, a hand's width apart, with a vertical between the
halves of the pair.

Points on different chords are read as one panel point when each is nearer to
the other than to the next point along its own chord — a local judgement with no
scale in it, so it does not shift with the division count. Where only one chord
pins a panel point the others stay at that same station, which is how a lone
chord vertex still squares the truss under it.

`MeasureOnPlan` says which length. **Off by default**, so each chord is divided
along itself: pure curve geometry, which is the only reading that works for a
truss standing on end and the only honest one for a truss running through space.
**On** measures both chords by their *plan* length — the chord projected onto
world XY — which is what a roof truss wants: a pitched top chord is longer than
the level bottom chord under it, so dividing each by its own length lands node
`i` a different distance along each and leaves every vertical leaning. Chords
are converted to NURBS before projecting so a parameter means the same place on
the chord and on its plan ruler — projecting an arc directly reparameterises it,
which on a 12 m chord is a 24 mm error in every node. A chord edge-on in plan has
no plan length to divide and measures along itself either way.

`Divisions` fixes the panel count up front and lays it out evenly. The
stations then **snap** onto nearby points rather than adding to them, in two
passes with a priority between them:

1. **The chords' own points win.** Polyline vertices and curve kinks on either
   chord are checked first and take every station they can reach. A node
   anywhere but a kink leaves a chord member cutting that corner, so these are
   not negotiable.
2. **Then the picked points**, offered whatever stations the first pass left
   free. A node takes the one point nearest to it, and a point moves one node,
   so several points crowded around a station cannot collapse the panels either
   side of it.

Reach is capped at half a panel each way, so no snap can move a station past its
neighbour and fold the truss over. Whatever did
not snap is then **spread evenly between the ones that did**, so the panels
either side of a snapped node do not come out short and long against an
otherwise regular truss.

`Strictness` decides what happens to a point that cap puts out of reach — one
placed away from the conventional spacing, which on a six-panel 12 m truss means
anything more than a metre off a panel point:

| | |
|---|---|
| `Relaxed` *(default)* | the division wins. Panel count is exactly what was asked for, the spacing stays regular, and a point too far off is left unused — reported on `FlatTruss.UnusedSnapPoints` and said out loud in `Notes`, since nothing about the geometry would otherwise show it |
| `Strict` | the points win. Every one becomes a node, and the panels are shared between them in proportion to the gaps they leave, so spacing is even *within* each bay rather than across the whole truss. The count only grows when there are more points than panels to give them |

Relaxed is the default because a regular truss is what most chords want, and an
oddly placed point is more often a stray pick than an intention. The count of
unused points is what keeps that a choice rather than a silent loss.

**A snap point has to lie on one of the two chords**, within `SnapTolerance` —
the same document tolerance Rhino's own On-Curve osnap works to. Anything else
is discounted and counted on `FlatTruss.OffChordSnapPoints`.

That requirement is what keeps the rule predictable. A point floating beside the
truss has no honest answer: projected square onto a sloped chord it lands at the
foot of the perpendicular, which is not the plan position it was picked at, and
the further off it sits the further that drifts. On the chord the point is
already at a station, and that station is where the node goes — nothing to
decide, and everything downstream works in plain station space.

Which chord it lies on decides where along the truss it lands, but a panel point
is where the whole truss steps, so it anchors *both*.

`Spacing` is the same question asked by length instead of count: the span is
divided by it and rounded to whole panels. `Divisions` overrides it whenever
both are set. Leave both at zero and control inverts: every polyline vertex,
curve kink and picked point becomes a node, and with nothing competing for a
fixed node count there is no priority and no strictness to apply.

Where the two chords converge — the tip of a cantilever, the apex of a tapered
truss — that end panel gets neither an end post nor a diagonal. Both nodes there
are the same point, so a diagonal out of it would run to the next node along one
chord or the other, which is that chord member drawn a second time.

Six bracing patterns — Warren, Warren with verticals, Pratt, Howe, Vierendeel
and cross-braced — with optional end posts and a flip that mirrors every
diagonal within its own panel.

Every member carries a `TrussMemberRole`: top chord, bottom chord, vertical,
diagonal or end post. Verticals and diagonals are separate roles because they
are specified separately, which is what lets a front-end sort a truss into
section groups without inspecting geometry. `FlatTruss.Web` returns both together
for callers that do not care about the distinction, and `DisplayName()` gives the
one spelling of each role that every front-end uses.

The generated `FlatTruss` answers the questions a front-end would otherwise have
to work out for itself, so that two of them cannot come to different answers:

| | |
|---|---|
| `Notes` | what is worth telling the user — a warped truss, a suppressed end post — with a level the host maps onto whatever it has |
| `DistinctNodes` | the nodes with coincident ones merged, for drawing or baking; `Nodes` keeps the duplicates, since members index into it |
| `Options` | what the truss was generated from, so "no end post here" can be told apart from "no end posts wanted" |

`FlatTrussGenerator` validates its own options and throws `ArgumentException` with
the message to show. Front-ends are expected to catch it and display it, not to
keep a second copy of the rules.

## BoxTruss

One or two top chords and one or two bottom chords: two and one (either way up)
is a triangular truss, two and two is a box. One of each is refused with a
pointer to FlatTruss, because the result would be a flat truss without the
things that tool knows to say about one.

**A box truss is flat trusses sharing chords, and is built as exactly that.**
Every chord is divided at *one* shared station list by `StationLayout` — the
layout lifted out of `FlatTrussGenerator` for the purpose, so divisions, spacing,
`MeasureOnPlan`, the two-pass snapping and `Strictness` all mean precisely what
they mean above, over three or four chords instead of two. A kink or a snap
point on any chord is a panel point on all of them, which is what keeps a panel
point a single cross-section through the truss rather than four near misses.

The web then goes in face by face, through the same `WebBuilder` a flat truss
uses:

| | |
|---|---|
| **side faces** — top chord to bottom chord | a flat truss in every respect: `Type`, verticals, diagonals, end posts. A test pins this: the side face of a box is member-for-member the `FlatTruss` between the same two chords |
| **lacing faces** — between twin chords, top to top and bottom to bottom | `LacingType`, the same six patterns read the same way, giving `Strut`s for verticals and `Lacing` for diagonals. Exists only where a chord has a twin, which is the whole difference between the shapes |

`BoxTrussOptions` says the same thing in its shape: everything shared with a flat
truss is *nested* as `Sides` (a `FlatTrussOptions`) rather than copied field by
field, and only `LacingType` and `FlipLacing` are added. Lacing defaults to
Warren with verticals — a strut at every panel point and a diagonal per panel —
because a lacing face holds two chords both in line and at their spacing, and it
takes both members to do both. `Vierendeel` gives struts alone, for when a deck
or roof sheet does the bracing.

`Strut` and `Lacing` are their own `TrussMemberRole`s, apart from `Vertical` and
`Diagonal`, because they are sized for a different job.

Chords can be handed over in any order and drawn in either direction. Each is
turned to run the way the first top chord does, and with two and two, the bottom
chords are matched to the top chord each sits under — judged at mid-length,
since chords drawn to a point at the ends say nothing there. Paired wrongly,
both side faces would run corner to corner through the middle of the box.

Where two chords of a face meet at an end — twin top chords drawn to a point
over one support — that face gets no end member and no end diagonal there, for
the reason a flat truss does not. `EndsSuppressed` and a note say so.

Not generated: cross-frames (diagonals *across* the section at a panel point).
Whether a box needs diaphragms, and where, is a torsion question for the
engineer, and the panel points are all there to draw them between.

## SurfaceGrid

Straight members over one surface — quad, triangulated or diagrid — with every
node on the surface. The input is a single surface, or instead the two to four
curves round the outside of an area, for a stick model that has no surfaces in
it (the surface between them is Rhino's edge surface, used to place nodes and
never returned).

**A structured grid, not a mesher, and deliberately.** A mesher copes with any
shape and returns something with no rows, no columns, and a result that shifts
between Rhino releases. This returns a layout that can be predicted before
running it and addressed afterwards. Three layers, kept apart because the next
tools need them apart:

1. **Where the grid lines go** is decided along the surface's edges by the same
   `StationLayout` that sets out a truss: the two edges running in U are its
   chords for U, the two in V for V. So a division is measured by *length*
   rather than by surface parameter — which bunches wherever a surface was built
   unevenly — and a kink or a snap point on an edge becomes a grid line exactly
   as it becomes a panel point on a chord. `Divisions`/`Spacing`, `Strictness`
   and the zero-from-both rule mean what they mean on FlatTruss, once per
   direction.
2. **The nodes** are a `Lattice`. Each grid line's parameter is taken from both
   edges it runs between and blended across, so on a fan or a taper the line
   lands exactly on its station at either edge; the surface is then evaluated
   there.
3. **The members** are a `GridPattern` read off the lattice by index — the truss
   web trick played along two axes.

| | |
|---|---|
| `Quad` | members along every grid line, both ways |
| `Triangulated` | quad, plus one diagonal per cell: `OneWay`, `Alternating` (diamonds), or `Shorter`, cell by cell, which on a warped or sheared surface folds each cell least |
| `Diagrid` | diagonals only, **between every other node**, closed round the outside by edge members. Both diagonals of every cell would cross in mid-air with no node at the crossing — two structures that happen to overlap — whereas taken chequer-board fashion members only ever meet at nodes. It needs an even count each way to close, and is given one more when asked for an odd one (`RaisedU`/`RaisedV`, and a note). `Flip` shifts the chequer-board off the corners |

Members carry a `GridMemberRole` — `U`, `V`, `Diagonal`, and `Edge` apart from
the rest because an edge beam is sized differently — plus the grid line they lie
along, so a whole beam can be put back together from its segments without
comparing coordinates.

**`Lattice` is public, and is the part meant to outlive this tool.** It knows
nothing about surfaces: nodes addressed by `(i, j)`, `LineU(j)`/`LineV(i)` as
whole ordered lines, `Cells` with corners and an `Area`, wrap flags, and an
`IsPresent` mask. SpaceTruss is its second user already — the bottom layer of a
truss is a lattice too, read by the same positions — and a floor grillage is
the planned third: beam `k` is grid line `k`, already whole, and a load
take-down is the cells, each with an area and four corners to share it between.
That will bring a second *source* of node positions — two world directions
across a plate rather than a surface's own — and nothing above the lattice
should need to change.

The mask is what **`ClipToTrim`** fills in. Off, a **trimmed** surface is
gridded whole, over the surface underneath the trim, with a warning. On, a node
that falls in an opening or outside the trimmed edge is absent, every member
that ran to it goes with it, and so does any member whose middle crosses an
opening — a member is left whole or left out, never cut at the rim, because a
member cut there would end where there is no node. Rows and columns keep their
numbering either way, so a definition reading the grid by position still can.
The test is asked in 3D against the trimmed face rather than in surface
parameters, because the grid is built on a NURBS copy whose parameterisation
need not match the face's own; a point is the same point on both.
`ClippedNodes` and `ClippedMembers` say what went.

Every node also carries the surface's unit **normal**, on `Normals`, which is
what a second layer is offset along.

Closed surfaces wrap: round a tower there is a seam in the surface and none in
the structure — no edge members there, no second set of nodes, and the same
valence either side. Where an edge collapses to a point — a dome's apex — the
whole row stacked on it is referred to by one index, so there is one node at the
pole and no member drawn twice.

A **polysurface** is refused, since its faces share no pair of directions. A
snap point has to lie on an **edge**: one out in the middle would have to move
a line both ways at once, so it is counted on `OffEdgeSnapPoints` and reported
instead.

## SpaceTruss

Takes a `SurfaceGrid` — made first, with everything above already decided —
and builds a double-layer truss on it: the grid is the top layer, member for
member; a second layer sits a given `Depth` under it; a web joins the two.
Made from the grid rather than from the surface again so that the truss and
the grid cannot disagree about where a node is, and so that the openings
clipped out of the grid are clipped out of the truss.

**Three settings: `Depth`, `Type` and `FlipDepth`.** There were seven. The
web pattern, its flip and its end posts have folded into `Type`, and the
vertical depth has gone; the reasons are below, because each was a control
somebody could reach for that did nothing under the combination they had.

`Type` is the one decision: what runs between the layers, which also says
where the second layer's nodes go.

| | |
|---|---|
| `Pyramid` *(default)* | the second layer offset half a cell: a node under the **centre of every cell**, joined to the cell's four corners, so every cell carries a pyramid and the apexes are a quad grid of their own. The space frame in its usual form, square on square offset, with no verticals and no pattern to choose, since the pyramids are the whole web. Under a diagrid the same rule puts a node under the centre of every diamond and the second layer comes out a diagrid of the other parity |
| `Warren`, `WarrenWithVerticals`, `Pratt`, `Howe`, `Vierendeel`, `CrossBraced` | the second layer aligned: a node under **every node**, the grid's own pattern between them, and a flat truss of that name along every grid line — rows and columns of a quad or triangulated grid, the diagonal runs of a diagrid — through the same `WebBuilder` a flat truss uses, so each is exactly the `TrussType` of the same name (a test holds the two to it, line by line). Two-way trusses on a grid rather than a space frame. A post closes every line wherever it ends: round the outside, and at the rim of an opening, which splits a line into runs that are each a truss of their own. A line round a tower is one closed run, with a vertical at every node and no ends |

Why one list rather than a layer type and a web pattern: the pattern was dead
whenever the layers were offset, and a remark had to say so. Why no flip: with
`Pratt` and `Howe` both in the list, a flip was Howe by another name, and on
Warren it only chose which way the first zigzag leaned. Why posts always: a
two-way truss with no post at its supported edge is the case nobody asks for,
and the post is one member to delete where it is not wanted.

**Type against pattern.** Every combination is drawn as asked. Pyramids on a
quad grid and on a diagrid are the two classic space frames. A flat-truss type
on any pattern is two-way trusses, along the diagonals for a diagrid, which
only fails where a diagrid closes on itself both ways and its lines have no
ends to start from; that is said as a warning. The one combination worth a
remark is pyramids under a **triangulated** grid: every cell is then braced
twice, by its diagonal in the top layer and by the pyramid below, with the
diagonal passing over the apex with no node between them. It is drawn,
`DoublesTheTopBracing` is set, and the note says a quad grid is the usual top
layer for pyramids.

A pole is one node however many positions sit on it, and the second layer is
welded the same way; a pyramid against a pole is three members, not four with
two on top of each other.

`Depth` is measured along the surface normal at every node, so the truss is
the same depth everywhere and follows the surface — a dome's second layer is a
smaller dome. The side is decided **once for the whole surface**, from the
mean of its normals: down where the surface faces up on the whole, whichever
way it happened to be built, because a rule read node by node would turn a
dome's layer inside out part way down where its normals go level. A surface
that stands on end has no underside, so its layer goes on the side it faces
and a note says so; `FlipDepth` puts it on the other, and serves for the roof
that wanted its truss above. A depth measured vertically was offered once and
is not any more: on a curved roof it thins the truss toward the sides, on a
tower it puts the second layer in the surface's own plane, and a space truss
is a constant depth or it is something else.

Every member is a `SpaceTrussMember` with a `TrussMemberRole` — the flat
truss's five — and, for a chord, a `GridRole` saying which part of its layer's
grid it is, so an edge beam sized apart on the grid can be sized apart on the
truss. Node indices run through the top layer first, then the bottom, and
`BottomLattice` is the bottom layer by position.

Not generated: cross-frames or bracing in the plane of the bottom layer beyond
what the pattern gives; whether a space frame needs them is the engineer's.

## BeamInfill

Takes a floor's worth of primary beams — window-selected, in any order, not
split where they cross — and fills every panel they enclose with evenly spaced
members. Nothing is said about which beams bound which panel: wherever the
beams close a loop, that loop is a panel.

Three steps, and the split between them is the design:

1. **Finding the panels is done flat**, on the plane that best fits the beams,
   because enclosure is a flat question. Fitted rather than assumed to be world
   XY, so a pitched roof is searched square-on and a wall of rails between posts
   works at all.
2. **Building each panel goes back to the beams themselves.** Every stretch of
   an outline is cut from the curve it came from, at the parameters the flat
   search reported, so a member lands on the beam rather than on its shadow —
   purlins between pitched rafters sit on the rafters.
3. **Filling is the truss rule turned on its side.** The two supporting sides
   are divided into the same number of equal parts along their own length, and
   member `i` joins point `i` of one to point `i` of the other. In a rectangle
   that is parallel members at even centres; in a splayed bay they fan, dividing
   both beams evenly, which never runs a member into a side.

Members run **the long way** across each panel — the usual arrangement, with
secondaries spanning the longer dimension onto primaries spanning the shorter —
judged per panel on the mean of each pair of opposite sides. `Flip` runs them
the short way instead. A square has no long way, so it goes to whichever pair
lies closer to world X: arbitrary, but the same in every panel, where leaving
it to corner order would chequer a floor of square bays.

`Divisions` is bays per panel, so one more than the members placed. `Spacing`
is the same question by length: each panel divides the longer of its two
supporting sides by it and rounds, so one value suits a floor of uneven bays.
`Divisions` overrides it, exactly as on FlatTruss. Zero from both finds the
panels and leaves them empty, which is the way to check the selection reads as
intended.

A side is not a beam. Corners are where the outline *turns* by more than
`CornerAngle` (30° by default), so two beams in line either side of a column are
one side, and so is a faceted or curved edge beam.

**Only four-sided panels are filled.** Members go between opposite sides, and a
triangle or an L has none. Any rule for those would be a guess at a decision
that is the engineer's, so the panel is handed back on `SkippedPanels` — as
geometry, because "three were skipped" is no help on a floor of two hundred.
One beam drawn across it usually turns it into panels that can be filled.

| | |
|---|---|
| `Panels` | each with its `Outline`, `Corners`, `Members`, and `StartNodes` / `EndNodes` paired with the members by index |
| `Nodes` | where members land on the beams, merged where two panels share one — the points to split the primaries at |
| `SkippedPanels` | outlines found and left empty: `IrregularPanels` (not four sides) and `PanelsWithOpenings` (a separate loop inside) |
| `LoosePanels` | beams that cross seen square-on but do not touch in space — nearly always a selection spanning two levels |
| `EdgeOnBeams` | curves square to the floor, ignored: columns caught in the window selection |

The picked beams are never split or moved. They are the user's model; `Nodes`
is there so that splitting them is a decision taken knowingly.

## GridColumns

Takes the gridlines as the user drew them — lines, arcs, polylines, in any
order and at any height — and stands a column at every place two of them
cross, from `Base` to `Top`. The smallest tool here, and the one a building
starts with: gridlines and two numbers are a floor of columns.

**Crossing is a plan question, so the grid is flattened first.** Every curve
is projected to world XY before any pair is crossed. A gridline traced at
ground and one traced off a third-floor plan are the same grid, and reading
them as not meeting because they sit at different heights would be wrong.
`Plan` returns the flattened curves so a front-end can show the grid the
columns were actually read from. A curve that flattens to nothing — a column
already in the model, swept up in a window selection — is ignored and counted
on `PlumbCurves`, since picking round columns is the tedious part of picking
a grid.

Crossings are merged within `Tolerance`, so three gridlines through one point
are one column, and a gridline that ends on another is a crossing too. Two
gridlines that run along each other rather than crossing — the same one
picked twice, usually — get no column, and the pair is counted on
`OverlappingPairs` and said in `Notes`, because the alternative is a column
somewhere along the overlap that nobody chose. Crossings come out ordered by
X then Y, not by pick order, so the same grid gives the same result however
it was selected.

`Base` and `Top` are world heights, not heights above anything. A column is
the line from one to the other, so `Top` below `Base` is a column drawn
downward — a pile from a pile cap — rather than a mistake. The two equal is
not a mistake either: it places nothing, reports the crossings, and says so,
which is the way to check the gridlines read as intended before deciding how
tall the columns are.

**Every crossing gets a column.** Which ones should not — the crossing in an
atrium, the one on a transfer — is a decision the engineer takes on the picked
curves or on the result. Nothing here guesses at it.

## GridBeams

The partner of GridColumns. Takes the columns and the gridlines, and runs a
beam along every gridline between each pair of neighbouring columns standing
on it, at a `Level` or projected onto a `Surface`. GridColumns reads the
crossings; this reads the **columns**, and that is the point: a crossing in an
atrium has no column and gets no beam through it, and a column deleted after
the grid was made takes its beams with it. Together, gridlines and a few
numbers are a floor of columns and its primary beams; Beam Infill then fills
the panels between them.

Three steps. **Every column becomes a point in plan**: where it crosses the
level, so a leaning column is read where the beam actually meets it, or the
nearer end when it stops short, which is counted on `ColumnsShortOfLevel` and
said. Columns stacked storey on storey, picked together, are one point, and
the point reaches if any of them does. A curve that runs further in plan than
it rises is a beam or a brace swept up in the pick, counted on `NotColumns`
and left out. **Every gridline is flattened** through the same `PlanView` that
GridColumns uses, and the columns on it are found within `Reach` and sorted
along it. **The piece of gridline between each consecutive pair is a beam**:
straight where the gridline is straight, an arc where it is an arc, lifted to
the level or projected onto the surface straight up or down. A gridline's end
past its last column gets nothing: a cantilever is a decision, not a default.

`Reach` is how far a column may sit off a gridline and still count as on it.
Zero uses the tolerance, which is right for columns the grid tools placed; a
model drawn by hand has its columns a few millimetres off, and this is the
knob for that. It is chosen rather than read from the model, because a column
found by reaching further gets a beam drawn to the gridline, not to the
column. What went unmatched is counted both ways: `ColumnsOffGrid` for a
column on no line, `GridlinesWithOneColumn` for a line with nothing to span.

`ByGridline` groups the beams by the gridline they lie along, in `Plan` order
with an empty list for a line that got none, so a definition can size a
gridline's beams together. `Nodes` are where beams meet columns, merged and
ordered by X then Y, on the level or on the surface.

## RectangularGrid

Takes a plane and the bay spacings each way, and sets out the gridlines and a
node at every crossing. The tool a model starts with, before there is anything
to pick: gridlines are what Grid Columns reads and what beams are split on,
and drawing them by hand is a `Line` per gridline because Rhino's array can
only repeat one spacing.

`XSpacings` are the bays measured along the plane's X axis, so each places a
gridline that runs the full depth of the grid in Y; `YSpacings` the other way.
Unequal bays are just a list — `6, 6, 8` — and one more gridline than bays
each way. `Overhang` runs every gridline past the outer ones at both ends, for
the bubbles; it moves no node. The plane is the whole of where and which way:
a skewed grid is a rotated plane, so there is no angle of its own.

`Rows[i][j]` is where the `i`-th X gridline meets the `j`-th Y gridline, and
`Nodes` is the same flattened by X then Y — the order Grid Columns sorts its
crossings into, so feeding the gridlines to it gives the same nodes in the same
order (a test holds the two to that). `XOffsets` and `YOffsets` are the running
totals, for anything that labels or sets out from them.

No `Notes`: the inputs are numbers, every one is checked up front, and a grid
that passes is drawn in full. No labels either, by the rule in
[docs/roadmap.md](docs/roadmap.md): a gridline is a line the office names its
own way.

`Spacings` reads the typed form — `6000`, `3x6000, 8000`, `*` for `x` — and
writes it back collapsed, so a Rhino prompt can show the remembered list the
way it was typed. Numbers are read in the invariant culture; a comma is always
a separator.

## RadialGrid

Takes a plane, the hole in the middle, the ring spacings measured outward from
it, how much of the way round to cover and how many bays to cut it into, and
sets out rays, rings and a node wherever a ray meets a ring. Rhino draws the
round case as a `Line`, an `ArrayPolar` and an `Arc` per ring, and the polar
array cannot stop at a quarter; the oval case it does not draw at all.

**The hole is two half-axes, `InnerU` along the plane's X and `InnerV` along
its Y.** Both zero, the default, runs the rays into the centre, which is then
**one node shared by every ray**, and gets no ring: a ring of no size is a
point. Equal is a round hole with a ring round it and no centre. Different is
the stadium and arena case: an oval hole, and every ring an oval with the
spacing added to both half-axes, which is how those grids are set out in
practice (a true offset of an oval is not an oval). One zero and the other not
is refused as a slit.

**Rays on an oval are straight lines set out by the ring's parameter, not by
the angle from the centre.** Ray `t` starts at `(U cos t, V sin t)` and runs
in the direction `(cos t, sin t)`, because that direction meets every ring at
the same parameter: the nodes along a ray are exactly the spacing apart, the
bays line up ring to ring, and they come out wider along the long sides and
tighter round the ends, which is the look of every stadium grid. A ray aimed
at the centre would cross the rings at drifting parameters. On a circle the
parameter is the angle and the two constructions are the same. What is a
little less than the spacing, between the axes, is the clear width between
rings measured square to them, because a ray is not quite square to an oval
there.

`Sweep` is in degrees from `StartAngle`, anticlockwise about the plane's Z,
and on an oval it is the ring parameter, which is the true angle on the axes.
**A full 360 is drawn with `Bays` rays, not `Bays + 1`,** because the last ray
of a full sweep sits on the first, and two lines in one place is the mistake a
grid tool exists to avoid; the rings close. Anything less is `Bays + 1` rays
and rings from the first ray to the last. `Bays` is a count rather than an
angle because a sweep that is not a whole number of angle steps leaves a part
bay at one end, and which end is a guess.

`Rows[i]` is the nodes along ray `i`; when the rays meet at the centre it is
item 0 of every row, the same point repeated, so that item `k + 1` is always
the `k`-th ring whether or not there is a hole. `Nodes` carries it once.
`RingOffsets` is how far out each ring sits from the inner ring, zero for the
inner ring itself when there is one. `Overhang` runs the rays past the outer
ring and never extends a ring: a ring past the end ray of a partial grid would
be a bay that is not there.

One thing said in `Notes`: bays so narrow somewhere on the innermost ring that
neighbouring nodes fall within tolerance of each other, which a column tool
downstream will read as one column where several were meant.

## Rules

- Depends on [Core](https://github.com/Otter-Logic/Core) and nothing else. Never another domain, never an
  adaptor.
- No UI. No Grasshopper. Adaptors wrap this; it does not know they exist.

## Roadmap

What comes next — columns at the crossings of picked curves, braced bays, a
storey copied up with its columns the right length, curves split where they
meet, ends snapped to a gridline — is set out tool by tool in
[docs/roadmap.md](docs/roadmap.md), each checked against the native Rhino
command that comes closest.

## Build and test

```
dotnet build OtterLogic.slnx
dotnet test OtterLogic.slnx
```

Tests need Rhino installed — they boot it in-process through Rhino.Inside,
because anything touching `Curve` or `Mesh` needs the real thing. That is why
CI builds but does not test.

Clone [Core](https://github.com/Otter-Logic/Core) as a sibling folder and the project reference resolves
against your working copy; without it the build falls back to the published
package.

## License

[MIT](LICENSE).
