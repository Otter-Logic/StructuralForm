# OtterLogic.StructuralForm

[![build](https://github.com/Otter-Logic/StructuralForm/actions/workflows/build.yml/badge.svg)](https://github.com/Otter-Logic/StructuralForm/actions/workflows/build.yml)

Trusses and structural layouts for [OtterLogic](https://github.com/Otter-Logic/Rhino3D).

A **domain**: it owns its types *and* its logic. `TrussType`, `FlatTrussOptions`,
`FlatTruss` and `FlatTrussGenerator` live together because they change together.

Five tools so far, built the same way: the user draws the geometry that governs,
the tool does the setting-out, and anything that quietly did not work is said
out loud in `Notes` (a `FormNote`, shared by all of them).

| | |
|---|---|
| [FlatTruss](#flattruss) | bracing between a top and a bottom chord |
| [BoxTruss](#boxtruss) | the same in 3D: a triangular or box truss on three or four chords |
| [SurfaceGrid](#surfacegrid) | a quad, triangulated or diagrid layout over a surface, on a reusable `Lattice` |
| [SpaceTruss](#spacetruss) | a double-layer space truss on a surface grid: pyramids, or flat trusses both ways |
| [BeamInfill](#beaminfill) | secondary members in every panel a floor's primary beams enclose |

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

`Type` is the one decision, and it says where the second layer's nodes go:

| | |
|---|---|
| `Offset` *(default)* | a node under the **centre of every cell**, joined to the cell's four corners: a pyramid per cell, and the apexes as a quad grid of their own. The space frame in its usual form, with no verticals and no pattern to choose, since the pyramids are the whole web. Under a diagrid the same rule puts a node under the centre of every diamond and the second layer comes out a diagrid of the other parity |
| `Aligned` | a node under **every node**, the grid's own pattern between them, and a flat truss along every grid line — rows and columns of a quad or triangulated grid, the diagonal runs of a diagrid — through the same `WebBuilder` a flat truss uses. `Web`, `FlipWeb` and `GenerateEndPosts` mean what `FlatTrussOptions` says they mean, with posts wherever a line ends: round the outside, and at the rim of an opening, which splits a line into runs that are each a truss of their own. A line round a tower is one closed run, with a vertical at every node and no ends |

A pole is one node however many positions sit on it, and the second layer is
welded the same way; a pyramid against a pole is three members, not four with
two on top of each other.

`DepthAlong` is which way the second layer is offset. `SurfaceNormal`
*(default)* keeps the truss the same depth everywhere and follows the surface —
a dome's second layer is a smaller dome. The side is decided **once for the
whole surface**, from the mean of its normals: down where the surface faces up
on the whole, whichever way it happened to be built, because a rule read node
by node would turn a dome's layer inside out part way down where its normals
go level. A surface that stands on end has no underside, so its layer goes on
the side it faces and a note says so; `FlipDepth` puts it on the other, and
serves for the roof that wanted its truss above. `Vertical` drops the layer
straight down instead, so every web member is plumb — and warns on a surface
standing on end, where that puts the second layer in the surface's own plane.

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

## Rules

- Depends on [Core](https://github.com/Otter-Logic/Core) and nothing else. Never another domain, never an
  adaptor.
- No UI. No Grasshopper. Adaptors wrap this; it does not know they exist.

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
