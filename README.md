# OtterLogic.StructuralForm

[![build](https://github.com/Otter-Logic/StructuralForm/actions/workflows/build.yml/badge.svg)](https://github.com/Otter-Logic/StructuralForm/actions/workflows/build.yml)

Trusses and structural layouts for [OtterLogic](https://github.com/Otter-Logic/Rhino3D).

A **domain**: it owns its types *and* its logic. `TrussType`, `Truss2DOptions`,
`Truss2D` and `Truss2DGenerator` live together because they change together.

## Truss2D

Nodes sit at *stations* — normalised arc-length positions along each chord.
Each chord carries its own station list of the same length, so top node `i`
always pairs with bottom node `i` and every web pattern reduces to index
arithmetic, while the two chords stay free to place that node at different
points along their own length.

`Divisions` fixes the panel count up front; the stations are then **snapped**
onto nearby snap points rather than adding to them. Reach is half a panel, and
no two stations can claim the same point. Leave `Divisions` at zero and control
inverts: every polyline vertex, curve kink and picked point becomes a node.

Six bracing patterns — Warren, Warren with verticals, Pratt, Howe, Vierendeel
and cross-braced — with optional end posts and a flip that mirrors every
diagonal within its own panel.

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
