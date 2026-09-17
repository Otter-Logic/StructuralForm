# OtterLogic.StructuralForm

[![build](https://github.com/Otter-Logic/StructuralForm/actions/workflows/build.yml/badge.svg)](https://github.com/Otter-Logic/StructuralForm/actions/workflows/build.yml)

Trusses and structural layouts for [OtterLogic](https://github.com/Otter-Logic/Rhino3D).

A **domain**: it owns its types *and* its logic. `TrussType`, `FlatTrussOptions`,
`FlatTruss` and `FlatTrussGenerator` live together because they change together.

## FlatTruss

Nodes sit at *stations* — positions along the chords measured as a fraction of
**plan** length, shared by both chords, so top node `i` and bottom node `i` sit
at the same plan position and every web pattern reduces to index arithmetic.

Plan rather than along-the-chord because a pitched top chord is longer than the
level bottom chord under it: divide each by its own length and node `i` lands a
different distance along each, leaving every vertical leaning. Chords are
converted to NURBS before projecting so a parameter means the same place on the
chord and on its plan ruler — projecting an arc directly reparameterises it,
which on a 12 m chord is a 24 mm error in every node. A chord edge-on in plan has
no plan length to divide and measures along itself instead.

`Divisions` fixes the panel count up front and lays it out evenly on plan. The
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
