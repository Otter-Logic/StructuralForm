# OtterLogic.StructuralForm

[![build](https://github.com/Otter-Logic/StructuralForm/actions/workflows/build.yml/badge.svg)](https://github.com/Otter-Logic/StructuralForm/actions/workflows/build.yml)

Trusses and structural layouts for [OtterLogic](https://github.com/Otter-Logic/Rhino3D).

A **domain**: it owns its types *and* its logic. `TrussType`, `Truss2DOptions`,
`Truss2D` and `Truss2DGenerator` live together because they change together.

## Truss2D

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
stations then **snap** onto nearby points rather than adding to them — reach is
half a panel, and no two stations can claim the same point — and whatever did not
snap is **spread evenly between the ones that did**, so the panels either side of
a snapped node do not come out short and long against an otherwise regular truss.

Snap points anchor *both* chords. A point is measured against whichever chord it
sits nearer to, since that decides where along the truss it lands, but a panel
point is where the whole truss steps. Leave `Divisions` at zero and control
inverts: every polyline vertex, curve kink and picked point becomes a node.

Six bracing patterns — Warren, Warren with verticals, Pratt, Howe, Vierendeel
and cross-braced — with optional end posts and a flip that mirrors every
diagonal within its own panel.

Every member carries a `TrussMemberRole`: top chord, bottom chord, vertical,
diagonal or end post. Verticals and diagonals are separate roles because they
are specified separately, which is what lets a front-end sort a truss into
section groups without inspecting geometry. `Truss2D.Web` returns both together
for callers that do not care about the distinction, and `DisplayName()` gives the
one spelling of each role that every front-end uses.

The generated `Truss2D` answers the questions a front-end would otherwise have
to work out for itself, so that two of them cannot come to different answers:

| | |
|---|---|
| `Notes` | what is worth telling the user — a warped truss, a suppressed end post — with a level the host maps onto whatever it has |
| `DistinctNodes` | the nodes with coincident ones merged, for drawing or baking; `Nodes` keeps the duplicates, since members index into it |
| `Options` | what the truss was generated from, so "no end post here" can be told apart from "no end posts wanted" |

`Truss2DGenerator` validates its own options and throws `ArgumentException` with
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
